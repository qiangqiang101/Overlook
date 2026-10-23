using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BigBro.App.Editor;
using BigBro.Core.Sensors;
using BigBro.Core.Settings;
using BigBro.Core.Theming;
using BigBro.Core.Updates;

namespace BigBro.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly ObservableCollection<ThemeItem> _items = [];
    private readonly List<ThemeItem> _allItems = [];
    private MonitorWindow? _monitor;
    private ThemeEditorWindow? _editor;

    // For the previewer / XAML loader.
    public MainWindow() : this(null!) { }

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        if (services is null) return;

        Icon = AppIcon.Load();
        ThemeList.ItemsSource = _items;

        SearchBox.TextChanged += (_, _) => ApplyFilter();
        ThemeList.SelectionChanged += (_, _) => ShowSelected();
        ShowButton.Click += (_, _) => ShowPanel();
        HideButton.Click += (_, _) => HidePanel();
        EditorButton.Click += (_, _) => OpenEditor(Selected);
        SettingsButton.Click += async (_, _) => await OpenSettingsAsync();
        OpenFolderButton.Click += (_, _) => OpenThemesFolder();

        services.Hub.Updated += OnHubUpdated;
        Loc.Changed += ApplyLanguage;
        PropertyChanged += OnWindowPropertyChanged;
        Closed += (_, _) =>
        {
            services.Hub.Updated -= OnHubUpdated;
            Loc.Changed -= ApplyLanguage;
            _monitor?.Close();
            _editor?.Close();
            services.SaveSettings();
        };

        ReloadThemes(services.Settings.CurrentTheme);
        ApplyLanguage();
        Opened += (_, _) =>
        {
            if (services.Settings.AutoShowMonitor && ThemeList.SelectedItem is not null) ShowPanel();
            if (services.Settings.CheckForUpdates) _ = CheckForUpdateAsync();
        };
    }

    // ------------------------------------------------------------------ updates

    private async Task CheckForUpdateAsync()
    {
        var result = await UpdateChecker.CheckAsync();
        if (result.Update is not { } update) return;      // up to date, or GitHub unreachable: nothing to tell the user

        UpdateButton.Content = Loc.T("main.updateAvailable", "Update available: {0}", update.Tag);
        UpdateButton.IsVisible = true;
        UpdateButton.Click += async (_, _) =>
        {
            try { await Launcher.LaunchUriAsync(new Uri(update.Url)); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        };
    }

    internal void SelectForScreenshot(string? themeName)
    {
        ThemeList.SelectedItem = _items.FirstOrDefault(i => i.Name == themeName);
    }

    // ------------------------------------------------------------------ language / tray

    private void ApplyLanguage()
    {
        Title = "Overlook";
        ThemesHeading.Text = Loc.T("main.themes", "Themes");
        SearchBox.PlaceholderText = Loc.T("main.search", "Search name, size or tag");
        EditorButton.Content = Loc.T("main.themeEditor", "Theme editor...");
        OpenFolderButton.Content = Loc.T("main.openFolder", "Open themes folder");
        SettingsButton.Content = Loc.T("main.settings", "Settings...");
        ShowButton.Content = Loc.T("main.show", "Show panel");
        HideButton.Content = Loc.T("main.hide", "Hide panel");
        NoThemeText.Text = Loc.T("main.noTheme", "Pick a theme on the left. To bring your 1.x themes over, open the theme editor and use Import.");
        CustomizeExpander.Header = Loc.T("main.customize", "Customize this theme");
        if (Selected is not null) ShowSelected();
        UpdateStatus();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized && _services.Settings.MinimizeToTray)
            Hide();
    }

    /// <summary>Brings the window back from the tray.</summary>
    public void Restore()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public bool PanelVisible => _monitor is not null;

    public void TogglePanel()
    {
        if (_monitor is not null) HidePanel();
        else ShowPanel();
    }

    // ------------------------------------------------------------------ theme list

    private void ReloadThemes(string? select = null)
    {
        _services.RescanThemes();
        _allItems.Clear();
        _allItems.AddRange(_services.Themes.Select(t => new ThemeItem(t)));
        ApplyFilter(select);

        if (_services.ThemeErrors.Count > 0)
            StatusText.Text = Loc.T("main.themeErrors", "{0} theme(s) could not be read: {1}", _services.ThemeErrors.Count, _services.ThemeErrors[0].Message);
    }

    private void ApplyFilter(string? select = null)
    {
        var keep = select ?? (ThemeList.SelectedItem as ThemeItem)?.Name;
        _items.Clear();
        foreach (var item in _allItems.Where(i => i.Matches(SearchBox.Text ?? ""))) _items.Add(item);
        ThemeList.SelectedItem = _items.FirstOrDefault(i => i.Name.Equals(keep, StringComparison.OrdinalIgnoreCase));
        ShowSelected();
    }

    private ThemeEntry? Selected => (ThemeList.SelectedItem as ThemeItem)?.Entry;

    private void ShowSelected()
    {
        var entry = Selected;
        NoThemeText.IsVisible = entry is null;
        ThemeDetails.IsVisible = entry is not null;
        CustomizePanel.Children.Clear();
        CustomizeExpander.IsVisible = false;
        if (entry is null) return;

        var doc = entry.Document;
        ThemeTitle.Text = doc.Name;
        var tags = doc.Tags.Count > 0 ? "\n" + Loc.T("main.tags", "Tags: {0}", string.Join(", ", doc.Tags)) : "";
        ThemeInfo.Text = $"{doc.Width} x {doc.Height}  ·  {Loc.T("main.version", "version {0}", doc.Version)}" +
                         (string.IsNullOrWhiteSpace(doc.Author) ? "" : $"  ·  {Loc.T("main.by", "by {0}", doc.Author)}") +
                         $"  ·  {Loc.T("main.updates", "updates every {0} s", (doc.UpdateInterval / 1000.0).ToString("0.#"))}{tags}";

        if (doc.NeedsSensors)
            ThemeInfo.Text += "\n" + Loc.T("main.needsSensors", "This theme still uses 1.x sensors. Open it in the theme editor to choose them.");

        PreviewImage.Source = null;
        if (entry.PreviewBytes is { } preview)
        {
            try
            {
                using var stream = new MemoryStream(preview);
                PreviewImage.Source = Bitmap.DecodeToWidth(stream, 640);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { }
        }

        BuildCustomizeRows(entry);
    }

    // ------------------------------------------------------------------ customize

    private static readonly Dictionary<SensorKind, string> ParamHints = new()
    {
        [SensorKind.HddTotalSize] = @"Drive of this computer, e.g. C:\ or /",
        [SensorKind.HddTotalFreeSpace] = @"Drive of this computer, e.g. C:\ or /",
        [SensorKind.HddUsage] = @"Drive of this computer, e.g. C:\ or /",
        [SensorKind.Ping] = "Host to ping, e.g. 8.8.8.8",
        [SensorKind.CustomDateTime] = "Date format, e.g. dddd dd MMMM yyyy HH:mm",
        [SensorKind.SystemUptime] = @"Format, e.g. d\.hh\:mm\:ss",
    };

    private void BuildCustomizeRows(ThemeEntry entry)
    {
        var doc = entry.Document;
        var rows = 0;

        foreach (var el in doc.Flatten())
        {
            switch (el)
            {
                case TextElement { AllowUserEdit: true, Sensor: null or { Kind: SensorKind.None } } t:
                    AddRow(doc, el, $"Text of '{t.Name}'", t.Text, ov => ov.Text, (ov, v) => ov.Text = v);
                    rows++;
                    break;
                case ImageElement { AllowUserEdit: true } i:
                    AddImageRow(doc, i);
                    rows++;
                    break;
                case YoutubeElement y:
                    AddRow(doc, el, $"YouTube video id of '{y.Name}'", y.YoutubeId ?? "", ov => ov.YoutubeId, (ov, v) => ov.YoutubeId = v);
                    rows++;
                    break;
            }

            if (el.Sensor is { Kind: not SensorKind.None })
            {
                AddSensorRow(doc, el);
                rows++;
            }

            if (el.Sensor is { } s && ParamHints.TryGetValue(s.Kind, out var hint))
            {
                AddRow(doc, el, $"'{el.Name}': {hint}", s.Param ?? "", ov => ov.Param, (ov, v) => ov.Param = v);
                rows++;
            }
        }

        CustomizeExpander.IsVisible = rows > 0;
        CustomizeExpander.Header = $"{Loc.T("main.customize", "Customize this theme")} ({rows})";
    }

    private void AddRow(ThemeDocument doc, ThemeElement el, string label, string themeDefault,
        Func<ElementOverride, string?> get, Action<ElementOverride, string?> set)
    {
        var current = _services.Settings.FindOverride(doc.Name, el.Name) is { } ov ? get(ov) : null;
        var box = new TextBox { Text = current ?? themeDefault, PlaceholderText = themeDefault };
        box.LostFocus += (_, _) =>
        {
            var text = box.Text ?? "";
            SetOverride(doc.Name, el.Name, o => set(o, text == themeDefault || text.Length == 0 ? null : text));
        };
        CustomizePanel.Children.Add(Labeled(label, box));
    }

    /// <summary>The live sensor list; when the panel isn't running (nothing is polling) it is fetched once from the configured address.</summary>
    private async Task<SensorSnapshot> LoadSnapshotAsync()
    {
        var snapshot = _services.Hub.Snapshot;
        if (!snapshot.IsEmpty || !LhmClient.TryNormalizeUrl(_services.Settings.LhmUrl, out var url)) return snapshot;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            return await _services.Hub.TestAsync(url, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return snapshot;
        }
    }

    /// <summary>"Custom this theme": swap the sensor an element shows without touching the theme file.</summary>
    private void AddSensorRow(ThemeDocument doc, ThemeElement el)
    {
        var own = el.Sensor!.Kind == SensorKind.Lhm ? el.Sensor.Param : el.Sensor.Kind.ToString();
        var picker = new SensorPicker(LoadSnapshotAsync, includeSpecials: true, Loc.T("main.themeSensor", "(theme's own: {0})", own ?? ""))
        {
            Value = _services.Settings.FindOverride(doc.Name, el.Name)?.Sensor,
        };
        var reset = new Button { Content = Loc.T("main.reset", "Reset") };

        void Save(SensorBinding? binding)
        {
            SetOverride(doc.Name, el.Name, o => o.Sensor = binding);
            // the running panel read its elements when it opened; reopen it so the new sensor shows
            if (_monitor is not null && string.Equals(_services.Settings.CurrentTheme, doc.Name, StringComparison.OrdinalIgnoreCase)) ShowPanel();
        }

        picker.Picked += Save;
        reset.Click += (_, _) =>
        {
            if (picker.Value is null) return;
            picker.Value = null;
            Save(null);
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(picker);
        Grid.SetColumn(reset, 1);
        row.Children.Add(reset);
        CustomizePanel.Children.Add(Labeled(Loc.T("main.sensorOf", "Sensor of '{0}'", el.Name), row));
    }

    private void AddImageRow(ThemeDocument doc, ImageElement el)
    {
        var none = Loc.T("main.themeImage", "(theme's own image)");
        var current = _services.Settings.FindOverride(doc.Name, el.Name)?.Image;
        var path = new TextBlock { Text = current ?? none, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var pick = new Button { Content = Loc.T("main.choose", "Choose...") };
        var reset = new Button { Content = Loc.T("main.reset", "Reset") };

        pick.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Image for '{el.Name}'",
                FileTypeFilter = [FilePickerFileTypes.ImageAll],
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } local) return;
            SetOverride(doc.Name, el.Name, o => o.Image = local);
            path.Text = local;
        };
        reset.Click += (_, _) =>
        {
            SetOverride(doc.Name, el.Name, o => o.Image = null);
            path.Text = none;
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        row.Children.Add(path);
        Grid.SetColumn(pick, 1);
        Grid.SetColumn(reset, 2);
        row.Children.Add(pick);
        row.Children.Add(reset);
        CustomizePanel.Children.Add(Labeled($"Image of '{el.Name}'", row));
    }

    private static Control Labeled(string label, Control control) => new StackPanel
    {
        Spacing = 2,
        Children = { new TextBlock { Text = label, Opacity = 0.8 }, control },
    };

    private void SetOverride(string themeName, string elementName, Action<ElementOverride> change)
    {
        var all = _services.Settings.Overrides;
        if (!all.TryGetValue(themeName, out var perTheme)) all[themeName] = perTheme = new(StringComparer.Ordinal);
        if (!perTheme.TryGetValue(elementName, out var ov)) perTheme[elementName] = ov = new ElementOverride();
        change(ov);
        if (ov.IsEmpty) perTheme.Remove(elementName);
        if (perTheme.Count == 0) all.Remove(themeName);
        _services.SaveSettings();
    }

    // ------------------------------------------------------------------ status / panel

    private void OnHubUpdated() => Dispatcher.UIThread.Post(UpdateStatus, DispatcherPriority.Background);

    private void UpdateStatus()
    {
        var hub = _services.Hub;
        StatusText.Text = hub.State switch
        {
            ConnectionState.Connected => Loc.T("status.connected", "Connected to {0}  ·  {1} sensors  ·  updated {2}", hub.Url?.Host ?? "", hub.Snapshot.Sensors.Count, DateTime.Now.ToString("T")),
            ConnectionState.Error => Loc.T("status.error", "Connection problem: {0}", hub.LastError ?? ""),
            ConnectionState.Connecting => Loc.T("status.connecting", "Connecting..."),
            _ => Loc.T("status.stopped", "Not connected"),
        };
    }

    private void ShowPanel()
    {
        var entry = Selected;
        if (entry is null) return;

        _monitor?.Close();
        if (!_services.StartPolling(entry.Document, out var error))
        {
            TestResult.Text = Loc.T("main.badUrl", "{0} Open Settings to fix the sensor source address.", error!);
            TestResult.Foreground = Brushes.IndianRed;
            return;
        }
        TestResult.Text = "";

        _services.Settings.CurrentTheme = entry.Name;
        _services.SaveSettings();

        var monitor = new MonitorWindow(_services, entry);
        monitor.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_monitor, monitor)) return;
            _monitor = null;
            HideButton.IsEnabled = false;
            _services.Hub.Stop();
            StatusText.Text = Loc.T("status.panelClosed", "Panel closed");
        };
        _monitor = monitor;
        monitor.Show();
        HideButton.IsEnabled = true;
        UpdateStatus();
    }

    private void HidePanel() => _monitor?.Close();

    // ------------------------------------------------------------------ other windows

    private async Task OpenSettingsAsync()
    {
        var ok = await new SettingsWindow(_services).ShowDialog<bool>(this);
        if (!ok) return;

        // The running panel keeps the old address/weather until it is restarted.
        if (_monitor is not null && Selected is not null) ShowPanel();
        else UpdateStatus();
    }

    private void OpenEditor(ThemeEntry? entry)
    {
        if (_editor is not null)
        {
            _editor.Activate();
            return;
        }
        var editor = new ThemeEditorWindow(_services, entry);
        editor.ThemesChanged += () => Dispatcher.UIThread.Post(() => ReloadThemes());
        editor.Closed += (_, _) =>
        {
            _editor = null;
            ReloadThemes(Selected?.Name);
            if (_monitor is not null && Selected is not null) ShowPanel();     // pick up edits
        };
        _editor = editor;
        editor.Show();
    }

    private void OpenThemesFolder()
    {
        try
        {
            Directory.CreateDirectory(_services.ThemesDirectory);
            _ = Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_services.ThemesDirectory));
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.T("main.folderFailed", "Could not open the folder: {0}", ex.Message);
        }
    }
}
