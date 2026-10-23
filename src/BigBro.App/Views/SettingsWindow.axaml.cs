using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BigBro.Core.Platform;
using BigBro.Core.Rgb;
using BigBro.Core.Theming;
using BigBro.Core.Updates;
using BigBro.Core.Sensors;
using BigBro.Core.Settings;

namespace BigBro.App.Views;

/// <summary>An enum value in a drop-down: <see cref="ToString"/> is the translated text, <see cref="Name"/> the value.</summary>
internal sealed record PropertyEditorChoice(string Name, string Text)
{
    public override string ToString() => Text;
}

public sealed partial class SettingsWindow : Window
{
    private readonly AppServices _services;

    public SettingsWindow() : this(null!) { }

    public SettingsWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        if (services is null) return;

        Icon = AppIcon.Load();
        var s = services.Settings;
        UrlBox.Text = s.LhmUrl;
        LocalSensorsBox.IsVisible = LinuxSensorReader.IsSupported;
        LocalSensorsBox.IsChecked = s.UseLocalSensors;
        LocalSensorsBox.IsCheckedChanged += (_, _) => UrlBox.IsEnabled = LocalSensorsBox.IsChecked != true;
        UrlBox.IsEnabled = !(LocalSensorsBox.IsVisible && LocalSensorsBox.IsChecked == true);
        LatBox.Value = ParseCoordinate(s.Latitude);
        LonBox.Value = ParseCoordinate(s.Longitude);
        TopMostBox.IsChecked = s.TopMost;
        PrimaryWindowsBox.IsChecked = s.OpenWindowsOnPrimary;
        UpdatesBox.IsChecked = s.CheckForUpdates;
        UpdateCheckButton.Click += async (_, _) => await CheckUpdatesNowAsync();
        AutoShowBox.IsChecked = s.AutoShowMonitor;
        MinimizeToTrayBox.IsChecked = s.MinimizeToTray;
        HqAudioBox.IsChecked = s.HighQualityAudio;
        StartWithSystemBox.IsChecked = AutoStart.IsSupported ? AutoStart.IsEnabled() : s.StartWithSystem;
        StartWithSystemBox.IsEnabled = AutoStart.IsSupported;

        var languages = Loc.Available(LanguageFolders(services));
        LanguageBox.ItemsSource = languages;
        LanguageBox.SelectedItem = languages.Contains(s.Language) ? s.Language : "English";

        LoadRgb();
        TestButton.Click += OnTestClick;
        LatLongButton.Click += (_, _) => OpenUrl("https://www.latlong.net");
        ResetPositionButton.Click += (_, _) =>
        {
            services.Settings.MonitorX = services.Settings.MonitorY = null;
            services.SaveSettings();
            ResetPositionButton.Content = Loc.T("settings.positionReset", "Position reset (applies next time the panel opens)");
        };
        OpenLogsButton.Click += async (_, _) => await OpenLogsFolderAsync();
        OkButton.Click += async (_, _) => await ApplyAsync();
        CancelButton.Click += (_, _) => Close(false);

        ApplyLanguage();
    }

    public static string[] LanguageFolders(AppServices services) =>
        [Path.Combine(AppContext.BaseDirectory, "Languages"), Path.Combine(services.DataDirectory, "Languages")];

    private void ApplyLanguage()
    {
        Title = Loc.T("settings.title", "Settings");
        SourceHeading.Text = Loc.T("settings.source", "Sensor source");
        SourceHint.Text = Loc.T("settings.sourceHint", SourceHint.Text!);
        TestButton.Content = Loc.T("settings.test", "Test connection");
        WeatherHeading.Text = Loc.T("settings.weather", "Weather");
        LatLabel.Text = Loc.T("settings.latitude", "Latitude");
        LonLabel.Text = Loc.T("settings.longitude", "Longitude");
        LatLongButton.Content = Loc.T("settings.getLatLong", "Find my latitude and longitude");
        GeneralHeading.Text = Loc.T("settings.general", "General");
        LanguageLabel.Text = Loc.T("settings.language", "Language");
        StartWithSystemBox.Content = Loc.T("settings.startWithSystem", "Start Overlook when I sign in");
        MinimizeToTrayBox.Content = Loc.T("settings.minimizeToTray", "Minimizing the main window hides it to the tray");
        UpdatesBox.Content = Loc.T("settings.checkUpdates", "Check for updates when Overlook starts");
        UpdateCheckButton.Content = Loc.T("settings.checkNow", "Check for updates now");
        UpdateDownloadButton.Content = Loc.T("settings.openRelease", "Open the release page");
        PrimaryWindowsBox.Content = Loc.T("settings.primaryWindows", "Open windows on the primary display when the panel is on another display");
        TopMostBox.Content = Loc.T("settings.topMost", "Keep the panel on top of other windows");
        AutoShowBox.Content = Loc.T("settings.autoShow", "Show the panel automatically when Overlook starts");
        HqAudioBox.Content = Loc.T("settings.hqAudio", "High quality audio visualizer (smoother, uses more CPU)");
        OpenLogsButton.Content = Loc.T("settings.openLogs", "Open logs folder");
        ResetPositionButton.Content = Loc.T("settings.resetPosition", "Reset the panel position");
        RgbHeading.Text = Loc.T("settings.rgb", "RGB background");
        LocalSensorsBox.Content = Loc.T("settings.localSensors", "Use this computer's own sensors instead (LibreHardwareMonitor only exists on Windows)");
        LatBox.PlaceholderText = Loc.T("settings.latExample", "e.g. 3.139");
        LonBox.PlaceholderText = Loc.T("settings.lonExample", "e.g. 101.687");
        RgbPortLabel.Text = Loc.T("settings.rgbPort", "UDP port");
        RgbEffectLabel.Text = Loc.T("settings.rgbEffect", "When nothing is sending");
        RgbShutdownLabel.Text = Loc.T("settings.rgbOfflineColor", "Offline colour");
        RgbMatrixTypeLabel.Text = Loc.T("settings.rgbMatrixShape", "Matrix shape");
        RgbMatrixTierLabel.Text = Loc.T("settings.rgbMatrixSize", "Matrix size");
        RgbShapeLabel.Text = Loc.T("settings.rgbLedShape", "LED shape");
        RgbRadiusLabel.Text = Loc.T("settings.rgbRadius", "Corner radius");
        RgbPaddingLabel.Text = Loc.T("settings.rgbPadding", "LED padding");
        RgbBlurLabel.Text = Loc.T("settings.rgbBlur", "Blur");
        RgbFpsLabel.Text = Loc.T("settings.rgbFps", "Frames per second");
        RgbBackgroundLabel.Text = Loc.T("settings.rgbBackground", "Background colour");
        RgbCoverLabel.Text = Loc.T("settings.rgbCover", "Cover image");
        RgbCoverBox.PlaceholderText = Loc.T("settings.rgbCoverHint", "path or http link (optional)");
        RgbCoverButton.Content = Loc.T("main.choose", "Choose...");
        RgbStretchLabel.Text = Loc.T("settings.rgbCoverFit", "Cover image fit");
        RgbHint.Text = Loc.T("settings.rgbHint", RgbHint.Text!);
        RgbListenBox.Content = Loc.T("settings.rgbListen", "Receive lighting data");
        RgbAppearance.Header = Loc.T("settings.rgbAppearance", "Appearance");
        OkButton.Content = Loc.T("common.ok", "OK");
        CancelButton.Content = Loc.T("common.cancel", "Cancel");
    }

    // ------------------------------------------------------------------ RGB background

    private static Color ToAvalonia(ThemeColor c) => Color.FromRgb(c.R, c.G, c.B);
    private static ThemeColor FromAvalonia(Color c) => new(255, c.R, c.G, c.B);

    private void LoadRgb()
    {
        var r = _services.Settings.Rgb;
        RgbListenBox.IsChecked = r.Listen;
        RgbPortBox.Value = r.Port;
        Fill<OfflineEffect>(RgbEffectBox, r.OfflineEffect);
        RgbShutdownColor.Color = ToAvalonia(r.ShutdownColor);
        Fill<MatrixSizeType>(RgbMatrixTypeBox, r.MatrixSizeType);
        Fill<MatrixSizeTier>(RgbMatrixTierBox, r.MatrixSizeTier);
        Fill<LedShape>(RgbShapeBox, r.LedShape);
        RgbRadiusBox.Value = r.RoundedRectangleCornerRadius;
        RgbPaddingBox.Value = (decimal)r.LedPadding;
        RgbBlurBox.Value = r.BlurIntensity;
        RgbFpsBox.Value = r.Fps > 0 ? r.Fps : 60;
        RgbBackgroundColor.Color = ToAvalonia(r.BackgroundColor);
        RgbCoverBox.Text = r.CoverImage;
        Fill<CoverStretch>(RgbStretchBox, r.CoverImageStretch);
        RgbStatus.Text = _services.Rgb.Status;

        RgbCoverButton.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = Loc.T("settings.rgbCover", "Cover image"),
                FileTypeFilter = [Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll],
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) RgbCoverBox.Text = path;
        };
    }

    private static void Fill<T>(ComboBox box, T current) where T : struct, Enum
    {
        var choices = Enum.GetNames<T>().Select(n => new PropertyEditorChoice(n, UiText.EnumValue(typeof(T), n))).ToList();
        box.ItemsSource = choices;
        box.SelectedItem = choices.First(c => c.Name == current.ToString());
    }

    private static T Pick<T>(ComboBox box, T fallback) where T : struct, Enum =>
        box.SelectedItem is PropertyEditorChoice choice && Enum.TryParse<T>(choice.Name, out var v) ? v : fallback;

    /// <returns>true if the port or the on/off state changed, so the receiver must be restarted.</returns>
    private bool SaveRgb()
    {
        var r = _services.Settings.Rgb;
        var listen = RgbListenBox.IsChecked == true;
        var port = (int)(RgbPortBox.Value ?? r.Port);
        var restart = listen != r.Listen || port != r.Port;
        r.Listen = listen;
        r.Port = port;

        r.OfflineEffect = Pick(RgbEffectBox, r.OfflineEffect);
        r.ShutdownColor = FromAvalonia(RgbShutdownColor.Color);
        r.MatrixSizeType = Pick(RgbMatrixTypeBox, r.MatrixSizeType);
        r.MatrixSizeTier = Pick(RgbMatrixTierBox, r.MatrixSizeTier);
        r.LedShape = Pick(RgbShapeBox, r.LedShape);
        r.RoundedRectangleCornerRadius = (int)(RgbRadiusBox.Value ?? r.RoundedRectangleCornerRadius);
        r.LedPadding = (double)(RgbPaddingBox.Value ?? (decimal)r.LedPadding);
        r.BlurIntensity = (int)(RgbBlurBox.Value ?? r.BlurIntensity);
        r.Fps = (int)(RgbFpsBox.Value ?? 60);
        r.BackgroundColor = FromAvalonia(RgbBackgroundColor.Color);
        r.CoverImage = Blank(RgbCoverBox.Text);
        r.CoverImageStretch = Pick(RgbStretchBox, r.CoverImageStretch);
        return restart;
    }

    private async Task CheckUpdatesNowAsync()
    {
        UpdateCheckButton.IsEnabled = false;
        UpdateDownloadButton.IsVisible = false;
        UpdateResult.Text = Loc.T("settings.updateChecking", "Checking...");
        var result = await UpdateChecker.CheckAsync();
        UpdateCheckButton.IsEnabled = true;

        if (result.Failed)
        {
            SetUpdateResult(Loc.T("settings.updateFailed", "Could not check for updates: {0}", result.Error!), false);
        }
        else if (result.Update is { } update)
        {
            SetUpdateResult(Loc.T("settings.updateAvailable", "Version {0} is available (you have {1}).", update.Tag, UpdateChecker.CurrentVersion.ToString(3)), null);
            UpdateDownloadButton.IsVisible = true;
            UpdateDownloadButton.Tag = update.Url;
            UpdateDownloadButton.Click -= OnDownloadClick;
            UpdateDownloadButton.Click += OnDownloadClick;
        }
        else
        {
            SetUpdateResult(Loc.T("settings.updateCurrent", "Overlook {0} is up to date.", UpdateChecker.CurrentVersion.ToString(3)), true);
        }
    }

    private void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (UpdateDownloadButton.Tag is string url) OpenUrl(url);
    }

    private void SetUpdateResult(string text, bool? ok)
    {
        UpdateResult.Text = text;
        UpdateResult.Foreground = ok switch { true => Brushes.SeaGreen, false => Brushes.IndianRed, _ => null };
    }

    private async Task OpenLogsFolderAsync()
    {
        try
        {
            Directory.CreateDirectory(CrashLog.Directory);          // nothing has been logged yet if the app never crashed
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(CrashLog.Directory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.ShowAsync(this, Title ?? "Settings", Loc.T("settings.logsFailed", "Could not open the logs folder: {0}", ex.Message));
        }
    }

    private string DescribeLocal()
    {
        var refresh = _services.Hub.Local.RefreshRate;
        var afterburner = !AfterburnerReader.IsSupported
            ? Loc.T("settings.fpsWindowsOnly", "FPS counters: Windows only.")
            : _services.Hub.Local.FramesPerSecond > 0 || _services.Hub.Local.FrameTimeMs > 0
                ? Loc.T("settings.afterburnerOk", "MSI Afterburner: connected.")
                : Loc.T("settings.afterburnerMissing", "MSI Afterburner: not detected (start Afterburner and RivaTuner, and enable the 'Framerate' counter).");
        var audio = OperatingSystem.IsLinux()
            ? Loc.T("settings.audioLinux", "Audio visualizer: needs 'parec' (pulseaudio-utils) to capture system audio.")
            : Loc.T("settings.audioWindows", "Audio visualizer: captures what this computer plays.");
        var rate = refresh > 0 ? $"{refresh:0} Hz" : Loc.T("settings.unknown", "unknown");
        return $"{Loc.T("settings.refreshRate", "Display refresh rate: {0}.", rate)}\n{afterburner}\n{audio}";
    }

    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        if (LocalSensorsBox.IsVisible && LocalSensorsBox.IsChecked == true)
        {
            var local = await Task.Run(SensorHub.ReadLocalOnce);
            SetTestResult(local.IsEmpty
                ? Loc.T("settings.localEmpty", "This computer exposes no readable sensors (no /sys/class/hwmon data and no nvidia-smi).")
                : Loc.T("settings.localFound", "Reading this computer: {0} sensors on {1} devices.", local.Sensors.Count, local.Hardware.Count), !local.IsEmpty);
            return;
        }

        if (!LhmClient.TryNormalizeUrl(UrlBox.Text, out var url))
        {
            SetTestResult(Loc.T("settings.badUrl", "That doesn't look like an address. Try something like 192.168.0.8:8085"), false);
            return;
        }

        TestButton.IsEnabled = false;
        SetTestResult(Loc.T("settings.connecting", "Connecting to {0}...", url), null);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var snapshot = await _services.Hub.TestAsync(url, cts.Token);
            if (snapshot.IsEmpty)
            {
                SetTestResult(Loc.T("settings.empty", "Connected, but the reply contains no sensors. Is this LibreHardwareMonitor's data.json?"), false);
                return;
            }
            var parts = new[] { snapshot.HardwareAt(HardwareKind.Cpu)?.Name, snapshot.HardwareAt(HardwareKind.Gpu)?.Name }
                .Where(p => !string.IsNullOrEmpty(p));
            SetTestResult(Loc.T("settings.connected", "Connected: {0} sensors on {1} devices ({2}).",
                snapshot.Sensors.Count, snapshot.Hardware.Count, string.Join(", ", parts)), true);
        }
        catch (Exception ex)
        {
            var reason = ex is TaskCanceledException or OperationCanceledException ? "no answer within 6 seconds" : ex.Message;
            SetTestResult(Loc.T("settings.failed", "Could not connect: {0}. Check the address and that the Remote Web Server is on and allowed through the firewall.", reason), false);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void SetTestResult(string text, bool? ok)
    {
        TestResult.Text = text;
        TestResult.Foreground = ok switch { true => Brushes.SeaGreen, false => Brushes.IndianRed, _ => null };
    }

    private async Task ApplyAsync()
    {
        var s = _services.Settings;
        s.LhmUrl = UrlBox.Text?.Trim() ?? "";
        if (LocalSensorsBox.IsVisible) s.UseLocalSensors = LocalSensorsBox.IsChecked == true;
        s.Latitude = FormatCoordinate(LatBox.Value);
        s.Longitude = FormatCoordinate(LonBox.Value);
        s.TopMost = TopMostBox.IsChecked == true;
        s.OpenWindowsOnPrimary = PrimaryWindowsBox.IsChecked == true;
        s.CheckForUpdates = UpdatesBox.IsChecked == true;
        s.AutoShowMonitor = AutoShowBox.IsChecked == true;
        s.MinimizeToTray = MinimizeToTrayBox.IsChecked == true;
        s.HighQualityAudio = HqAudioBox.IsChecked == true;
        s.StartWithSystem = StartWithSystemBox.IsChecked == true;

        if (AutoStart.IsSupported)
        {
            try
            {
                if (AutoStart.IsEnabled() != s.StartWithSystem) AutoStart.Set(s.StartWithSystem);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                await Dialogs.ShowAsync(this, Title ?? "Settings", Loc.T("settings.autostartFailed", "Could not change the start-up setting: {0}", ex.Message));
            }
        }

        if (LanguageBox.SelectedItem is string language && language != s.Language)
        {
            s.Language = language;
            Loc.Load(language, LanguageFolders(_services));
        }

        var restartRgb = SaveRgb();
        _services.SaveSettings();
        if (restartRgb) _services.Rgb.Restart();
        _services.Rgb.NotifyEdited();
        Close(true);
    }

    private static decimal? ParseCoordinate(string? s) =>
        decimal.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string? FormatCoordinate(decimal? v) => v?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
