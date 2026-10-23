using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BigBro.App.Rendering;
using BigBro.App.Views;
using System.Net.Http;
using BigBro.Core.Legacy;
using BigBro.Core.Sensors;
using BigBro.Core.Settings;
using BigBro.Core.Theming;

namespace BigBro.App.Editor;

public sealed partial class ThemeEditorWindow : Window
{
    private readonly AppServices _services;
    private readonly SelectionLayer _layer;
    private readonly DispatcherTimer _dataTimer;
    private readonly DispatcherTimer _animTimer;
    private readonly EditorContext _ctx;
    private readonly SensorSnapshot _sample;

    private EditorSession _session;
    private ThemeAssets _assets;
    private ThemeView? _view;
    private RgbLayer? _rgbLayer;
    private ObservableCollection<ElementNode> _nodes = [];
    private List<ThemeEntry> _entries = [];
    private ThemeElement? _selected;
    private string _dragBefore = "";
    private bool _suppress;
    private bool _forceClose;
    private bool _freshTheme;

    /// <summary>Raised when themes were saved or imported, so the main window can refresh its list.</summary>
    public event Action? ThemesChanged;

    public ThemeEditorWindow() : this(null!, null) { }

    /// <param name="start">Theme to edit; null starts a new one.</param>
    public ThemeEditorWindow(AppServices services, ThemeEntry? start)
    {
        _services = services;
        InitializeComponent();
        if (services is null) { _session = null!; _assets = null!; _layer = null!; _dataTimer = null!; _animTimer = null!; _ctx = null!; _sample = null!; return; }

        Icon = AppIcon.Load();
        _sample = LoadSample();

        if (start is not null)
        {
            _session = new EditorSession(ThemeJson.Load(start.JsonPath), start.Directory);
        }
        else
        {
            _session = EditorSession.CreateNew(services.ThemesDirectory);
            _freshTheme = true;
        }
        _assets = new ThemeAssets(_session.Document);

        _ctx = new EditorContext
        {
            Owner = this,
            Apply = change =>
            {
                if (_session.Edit(change)) _freshTheme = false;
                AfterEdit();
            },
            ImportImage = EditorSession.ImportImage,
            FontFamilies = FontFamilyNames,
            Sensors = CurrentSensors,
        };

        _layer = new SelectionLayer(() => _view);
        _layer.SelectionRequested += OnLayerSelection;
        _layer.DragStarted += () => _dragBefore = _session.Snapshot();
        _layer.DragChanged += () => { _view?.InvalidateVisual(); UpdateStatus(); };
        _layer.SnapToGrid = services.Settings.EditorSnapToGrid;
        _layer.ShowGrid = services.Settings.EditorShowGrid;
        _layer.GridSize = Math.Clamp(services.Settings.EditorGridSize, 2, 200);
        SnapItem.IsChecked = _layer.SnapToGrid;
        ShowGridItem.IsChecked = _layer.ShowGrid;
        _layer.DragCompleted += moved =>
        {
            if (moved && _session.RecordUndo(_dragBefore)) _freshTheme = false;
            RebuildElementPanel();
            UpdateTitle();
        };

        InitDocking();
        WireToolbar();
        Tree.SelectionChanged += OnTreeSelection;

        _dataTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshData());
        _animTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Render, (_, _) => _view?.Tick());
        _dataTimer.Start();

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        CanvasScroll.PointerWheelChanged += OnCanvasWheel;
        Closed += (_, _) => Cleanup();

        BuildGridSizeMenu();
        RefreshOpenMenu();
        Structural();
        Opened += async (_, _) =>
        {
            Fit();
            await PromptLegacySensorsAsync();
        };
        ApplyLanguage();
    }

    /// <summary>Used by the --screenshot command: select an element and a tab before the window is shown.</summary>
    internal void PrepareForScreenshot(string? elementName, int tabIndex)
    {
        if (elementName is not null && _session.Document.Flatten().FirstOrDefault(e => e.Name == elementName) is { } el)
        {
            _selected = el;
            _layer.Selected = el;
            SelectInTree(el);
            RebuildElementPanel();
            UpdateStatus();
        }
        if (this.FindControl<TabControl>("SideTabs") is { } tabs) tabs.SelectedIndex = tabIndex;
    }

    // ------------------------------------------------------------------ setup

    private static SensorSnapshot LoadSample()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Overlook/Assets/sample-lhm.json"));
            return LhmParser.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or System.Text.Json.JsonException)
        {
            return SensorSnapshot.Empty;
        }
    }

    /// <summary>Live data when the panel is connected to LibreHardwareMonitor, otherwise the bundled sample machine.</summary>
    private SensorSnapshot CurrentSensors() => _services.Hub.Snapshot.IsEmpty ? _sample : _services.Hub.Snapshot;

    private IReadOnlyList<string> FontFamilyNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in _services.Fonts.Families) names.Add(n);
        foreach (var f in FontManager.Current.SystemFonts) names.Add(f.Name);
        return [.. names];
    }

    private void WireToolbar()
    {
        NewItem.Click += async (_, _) => await NewThemeAsync();
        SaveItem.Click += (_, _) => Save();
        SaveAsItem.Click += async (_, _) => await SaveAsAsync();
        CloseItem.Click += (_, _) => Close();
        UndoItem.Click += (_, _) => UndoRedo(undo: true);
        RedoItem.Click += (_, _) => UndoRedo(undo: false);
        DuplicateItem.Click += (_, _) => Duplicate();
        DeleteItem.Click += (_, _) => DeleteSelected();
        ForwardItem.Click += (_, _) => Reorder(forward: true);
        BackwardItem.Click += (_, _) => Reorder(forward: false);
        ImportItem.Click += OnImportFilesClick;
        ImportFolderItem.Click += OnImportFolderClick;
        PreviewItem.Click += (_, _) => CapturePreview();
        FitItem.Click += (_, _) => Fit();
        ZoomInItem.Click += (_, _) => ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + 0.1, 0.25, 3);
        ZoomOutItem.Click += (_, _) => ZoomSlider.Value = Math.Clamp(ZoomSlider.Value - 0.1, 0.25, 3);
        Zoom100Item.Click += (_, _) => ZoomSlider.Value = 1;
        ZoomSlider.ValueChanged += (_, _) => ApplyZoom();
        SnapItem.Click += (_, _) => GridChanged();
        ShowGridItem.Click += (_, _) => GridChanged();

        foreach (var (type, label, _) in ElementFactory.Types)
        {
            var item = new MenuItem { Header = ElementFactory.LocalLabel(type, label) };
            var t = type;
            item.Click += (_, _) => AddElement(t);
            AddMenu.Items.Add(item);
        }
    }

    private static readonly int[] GridSizes = [5, 8, 10, 16, 20, 25, 32, 50];

    private void BuildGridSizeMenu()
    {
        var current = GridSizes.MinBy(g => Math.Abs(g - _layer.GridSize));
        foreach (var size in GridSizes)
        {
            var item = new MenuItem { Header = $"{size} px", ToggleType = MenuItemToggleType.Radio, GroupName = "gridSize", IsChecked = size == current };
            var chosen = size;
            item.Click += (_, _) =>
            {
                _layer.GridSize = chosen;
                GridChanged();
            };
            GridSizeMenu.Items.Add(item);
        }
        _layer.GridSize = current;
    }

    private void ApplyLanguage()
    {
        ApplyDockLanguage();
        FileMenu.Header = Loc.T("editor.menuFile", "_File");
        NewItem.Header = Loc.T("editor.new", "_New theme");
        OpenMenu.Header = Loc.T("editor.open", "_Open theme");
        SaveItem.Header = Loc.T("common.save", "_Save");
        SaveAsItem.Header = Loc.T("editor.saveAs", "Save _as...");
        ImportItem.Header = Loc.T("editor.import", "_Import 1.x themes (.xml)...");
        ImportFolderItem.Header = Loc.T("editor.importFolder", "Import a 1.x _Themes folder...");
        CloseItem.Header = Loc.T("editor.close", "_Close editor");
        EditMenu.Header = Loc.T("editor.menuEdit", "_Edit");
        UndoItem.Header = Loc.T("editor.undo", "_Undo");
        RedoItem.Header = Loc.T("editor.redo", "_Redo");
        DuplicateItem.Header = Loc.T("editor.duplicate", "_Duplicate");
        DeleteItem.Header = Loc.T("common.delete", "De_lete");
        ForwardItem.Header = Loc.T("editor.forward", "Bring _forward");
        BackwardItem.Header = Loc.T("editor.backward", "Send _back");
        AddMenu.Header = Loc.T("editor.add", "_Add");
        ViewMenu.Header = Loc.T("editor.menuView", "_View");
        SnapItem.Header = Loc.T("editor.snap", "_Snap to grid");
        ShowGridItem.Header = Loc.T("editor.showGrid", "Show _grid");
        GridSizeMenu.Header = Loc.T("editor.gridSize", "Grid _size");
        ZoomInItem.Header = Loc.T("editor.zoomIn", "Zoom _in");
        ZoomOutItem.Header = Loc.T("editor.zoomOut", "Zoom _out");
        Zoom100Item.Header = Loc.T("editor.zoom100", "_Actual size (100%)");
        FitItem.Header = Loc.T("editor.fit", "_Fit to window");
        ToolsMenu.Header = Loc.T("editor.menuTools", "_Tools");
        PreviewItem.Header = Loc.T("editor.preview", "Update _preview image");
        ElementTab.Header = Loc.T("editor.tabElement", "Element");
        ThemeTab.Header = Loc.T("editor.tabTheme", "Theme");
    }

    // ------------------------------------------------------------------ view / data

    /// <summary>Rebuilds everything that depends on the shape of the element tree.</summary>
    private void Structural(string? reselect = null)
    {
        var name = reselect ?? _selected?.Name;
        var doc = _session.Document;

        _view?.Dispose();
        _view = new ThemeView(doc, _services.Fonts, null, _assets)
        {
            PixelPerfect = false,
            HighQualityAudio = _services.Settings.HighQualityAudio,
        };
        CanvasHost.Children.Clear();
        UpdateRgbLayer();
        CanvasHost.Children.Add(_view);
        CanvasHost.Children.Add(_layer);
        ApplyCanvasSize();

        _nodes = ElementNode.Build(doc);
        _suppress = true;
        Tree.ItemsSource = _nodes;
        _suppress = false;

        _selected = name is null ? null : doc.Flatten().FirstOrDefault(e => e.Name == name);
        _layer.Selected = _selected;
        SelectInTree(_selected);

        RebuildElementPanel();
        RebuildThemePanel();
        RefreshData();
        UpdateAnimation();
        UpdateTitle();
        UpdateStatus();
    }

    /// <summary>Shows the lighting beneath the preview when the theme has an RGB background.</summary>
    private void UpdateRgbLayer()
    {
        var want = _session.Document.Rgb is { Enabled: true };
        if (want && _rgbLayer is null)
        {
            _services.Rgb.Acquire();
            _rgbLayer = new RgbLayer(_services.Rgb, null);
        }
        else if (!want && _rgbLayer is not null)
        {
            DisposeRgbLayer();
        }

        if (_view is not null) _view.RgbLayerPresent = _rgbLayer is not null;
        if (_rgbLayer is not null && !CanvasHost.Children.Contains(_rgbLayer)) CanvasHost.Children.Insert(0, _rgbLayer);
    }

    private void DisposeRgbLayer()
    {
        if (_rgbLayer is null) return;
        CanvasHost.Children.Remove(_rgbLayer);
        _rgbLayer.Dispose();
        _rgbLayer = null;
        _services.Rgb.Release();
    }

    private void ApplyCanvasSize()
    {
        var doc = _session.Document;
        CanvasHost.Width = doc.Width;
        CanvasHost.Height = doc.Height;
        if (_view is not null) { _view.Width = doc.Width; _view.Height = doc.Height; }
        _layer.Width = doc.Width;
        _layer.Height = doc.Height;
    }

    private void UpdateAnimation()
    {
        if (_view is { NeedsTick: true })
        {
            _animTimer.Interval = TimeSpan.FromMilliseconds(_view.TickIntervalMs);
            _animTimer.Start();
        }
        else
        {
            _animTimer.Stop();
        }
    }

    /// <summary>Live data when the panel is connected to LibreHardwareMonitor, otherwise a bundled sample machine.</summary>
    private void RefreshData()
    {
        if (_view is null) return;
        var hub = _services.Hub;
        var resolver = hub.Snapshot.IsEmpty
            ? new SensorResolver(_sample, hub.Local)
            : hub.CreateResolver();
        _view.Refresh(resolver);
    }

    private void AfterEdit()
    {
        UpdateRgbLayer();
        ApplyCanvasSize();
        _view?.InvalidateVisual();
        RefreshData();
        UpdateAnimation();
        if (_selected is not null) FindNode(_selected)?.Refresh();
        UpdateTitle();
        UpdateStatus();
    }

    // ------------------------------------------------------------------ selection / panels

    private ElementNode? FindNode(ThemeElement? element) =>
        element is null ? null : _nodes.SelectMany(n => n.Descendants()).FirstOrDefault(n => n.Element == element);

    private void SelectInTree(ThemeElement? element)
    {
        _suppress = true;
        Tree.SelectedItem = FindNode(element);
        _suppress = false;
    }

    private void OnTreeSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        _selected = (Tree.SelectedItem as ElementNode)?.Element;
        _layer.Selected = _selected;
        _layer.InvalidateVisual();
        RebuildElementPanel();
        UpdateStatus();
    }

    private void OnLayerSelection(ThemeElement? element)
    {
        _selected = element;
        SelectInTree(element);
        RebuildElementPanel();
        UpdateStatus();
    }

    private void RebuildElementPanel()
    {
        ElementPanel.Children.Clear();
        var doc = _session.Document;
        if (_selected is null)
        {
            ElementPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("editor.noSelection", "Click an element on the canvas or in the list to edit it."),
                TextWrapping = TextWrapping.Wrap, Opacity = 0.7,
            });
            return;
        }

        var element = _selected;
        ElementPanel.Children.Add(new TextBlock { Text = ElementFactory.LabelOf(element), FontSize = 16, FontWeight = FontWeight.SemiBold });

        // parent chooser: nesting makes children move with, and get clipped to, their parent
        var none = Loc.T("editor.topLevel", "(top level)");
        var candidates = doc.Flatten().Where(e => e != element && !ElementFactory.IsDescendant(element, e)).Select(e => e.Name).ToList();
        candidates.Insert(0, none);
        var currentParent = ElementFactory.Locate(doc, element)?.Parent?.Name ?? none;
        var parent = new ComboBox { ItemsSource = candidates, SelectedItem = currentParent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        parent.SelectionChanged += (_, _) =>
        {
            if (parent.SelectedItem is not string chosen || chosen == currentParent) return;
            var newParent = chosen == none ? null : doc.Flatten().FirstOrDefault(e => e.Name == chosen);
            _session.Edit(() => ElementFactory.Reparent(doc, element, newParent));
            Structural(element.Name);
        };
        ElementPanel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("128,*"),
            ColumnSpacing = 8,
            Children =
            {
                new TextBlock { Text = Loc.T("editor.parent", "Parent"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Opacity = 0.85 },
                Place(parent, 1),
            },
        });

        ElementPanel.Children.Add(PropertyEditor.Build(element, _ctx, RebuildElementPanel));
    }

    private static Control Place(Control c, int column)
    {
        Grid.SetColumn(c, column);
        return c;
    }

    private void RebuildThemePanel()
    {
        ThemePanel.Children.Clear();
        ThemePanel.Children.Add(PropertyEditor.Build(_session.Document, _ctx, RebuildThemePanel));
    }

    // ------------------------------------------------------------------ element operations

    private void AddElement(string type)
    {
        var doc = _session.Document;
        ThemeElement? created = null;
        _session.Edit(() =>
        {
            created = ElementFactory.Create(type, doc);
            doc.Elements.Add(created);
        });
        _freshTheme = false;
        Structural(created!.Name);
    }

    private void DeleteSelected()
    {
        if (_selected is not { } element) return;
        _session.Edit(() => ElementFactory.Delete(_session.Document, element));
        _selected = null;
        Structural();
    }

    private void Duplicate()
    {
        if (_selected is not { } element) return;
        var doc = _session.Document;
        ThemeElement? copy = null;
        _session.Edit(() =>
        {
            copy = ElementFactory.Clone(doc, element);
            var loc = ElementFactory.Locate(doc, element)!.Value;
            loc.Siblings.Insert(loc.Siblings.IndexOf(element) + 1, copy);
        });
        Structural(copy!.Name);
    }

    private void Reorder(bool forward)
    {
        if (_selected is not { } element) return;
        _session.Edit(() => ElementFactory.Reorder(_session.Document, element, forward));
        Structural(element.Name);
    }

    private void Nudge(int dx, int dy)
    {
        if (_selected is not { } element) return;
        _session.Edit(() => { element.X += dx; element.Y += dy; });
        AfterEdit();
        RebuildElementPanel();
    }

    private void UndoRedo(bool undo)
    {
        var name = _selected?.Name;
        if (undo) _session.Undo(); else _session.Redo();
        Structural(name);
    }

    // ------------------------------------------------------------------ files

    private void Save()
    {
        try
        {
            // the bundled themes are read-only defaults: saving one keeps an edited copy of the same name in the user's folder
            if (_services.IsBundled(_session.Directory)) _session.SaveAs(_services.ThemesDirectory, _session.Document.Name);
            else _session.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = Dialogs.ShowAsync(this, Title ?? "Theme editor", ex.Message);
            return;
        }
        _freshTheme = false;
        ThemesChangedRefresh();
        UpdateTitle();
        StatusText.Text = Loc.T("editor.saved", "Saved to {0}", _session.JsonPath);
    }

    private async Task SaveAsAsync()
    {
        var name = await Dialogs.AskAsync(this, Loc.T("editor.saveAs", "Save as..."),
            Loc.T("editor.themeName", "Name of the new theme"), _session.Document.Name + " copy");
        if (name is null) return;
        try
        {
            _session.SaveAs(_services.ThemesDirectory, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.ShowAsync(this, Title ?? "Theme editor", ex.Message);
            return;
        }
        _freshTheme = false;
        _assets.Dispose();
        _assets = new ThemeAssets(_session.Document);
        ThemesChangedRefresh();
        Structural();
    }

    private async Task NewThemeAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        DeleteFreshFolder();
        LoadSession(EditorSession.CreateNew(_services.ThemesDirectory), fresh: true);
        ThemesChangedRefresh();
    }

    private async Task OpenEntryAsync(ThemeEntry entry)
    {
        if (entry.Directory == _session.Directory) return;
        if (!await ConfirmDiscardAsync()) return;

        DeleteFreshFolder();
        try
        {
            LoadSession(new EditorSession(ThemeJson.Load(entry.JsonPath), entry.Directory), fresh: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            await Dialogs.ShowAsync(this, entry.Name, ex.Message);
        }
    }

    private void LoadSession(EditorSession session, bool fresh)
    {
        _session = session;
        _freshTheme = fresh;
        _assets.Dispose();
        _assets = new ThemeAssets(session.Document);
        _selected = null;
        RefreshOpenMenu();
        Structural();
        Fit();
        _ = PromptLegacySensorsAsync();
    }

    /// <summary>Lists the themes in File > Open theme.</summary>
    private void RefreshOpenMenu()
    {
        _entries = [.. _services.Themes];
        OpenMenu.Items.Clear();
        if (_entries.Count == 0)
        {
            OpenMenu.Items.Add(new MenuItem { Header = Loc.T("editor.noThemes", "(no themes yet)"), IsEnabled = false });
            return;
        }
        foreach (var entry in _entries)
        {
            var item = new MenuItem { Header = entry.Name.Replace("_", "__"), IsChecked = entry.Directory == _session.Directory, ToggleType = MenuItemToggleType.Radio, GroupName = "openTheme" };
            var e = entry;
            item.Click += async (_, _) => await OpenEntryAsync(e);
            OpenMenu.Items.Add(item);
        }
    }

    private void ThemesChangedRefresh()
    {
        _services.RescanThemes();
        RefreshOpenMenu();
        ThemesChanged?.Invoke();
    }

    /// <summary>A theme created with "New" that was never touched shouldn't litter the themes folder.</summary>
    private void DeleteFreshFolder()
    {
        if (!_freshTheme || _session.Document.Elements.Count > 0) return;
        try { Directory.Delete(_session.Directory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _freshTheme = false;
        ThemesChangedRefresh();
    }

    /// <returns>false if the user cancelled.</returns>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_session.IsDirty) return true;
        var choice = await Dialogs.ChoiceAsync(this, Loc.T("editor.unsaved", "Unsaved changes"),
            Loc.T("editor.unsavedText", "Save the changes to \"{0}\" first?", _session.Document.Name),
            Loc.T("common.save", "Save"), Loc.T("editor.discard", "Discard"), Loc.T("common.cancel", "Cancel"));
        if (choice == 0) Save();
        return choice is 0 or 1;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_forceClose || e.Cancel || _session is null || !_session.IsDirty) return;
        e.Cancel = true;
        _ = CloseAfterConfirmAsync();
    }

    private async Task CloseAfterConfirmAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        _forceClose = true;
        Close();
    }

    private void Cleanup()
    {
        SaveDockLayout();
        _dataTimer.Stop();
        _animTimer.Stop();
        _view?.Dispose();
        DisposeRgbLayer();
        DeleteFreshFolder();
        _assets.Dispose();
    }

    // ------------------------------------------------------------------ import

    private async void OnImportFilesClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("editor.importTitle", "Choose 1.x theme files"),
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("BigBro Monitor 1.x themes") { Patterns = ["*.xml"] }],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) await ImportAsync(paths);
    }

    private async void OnImportFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.T("editor.importFolderTitle", "Choose the 1.x Themes folder"),
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir) return;
        var xml = Directory.EnumerateFiles(dir, "*.xml").ToList();
        if (xml.Count == 0)
        {
            await Dialogs.ShowAsync(this, Loc.T("editor.nothing", "Nothing to import"), Loc.T("editor.noXml", "That folder has no .xml theme files."));
            return;
        }
        await ImportAsync(xml);
    }

    private void SetImporting(bool busy) => ImportItem.IsEnabled = ImportFolderItem.IsEnabled = !busy;

    private async Task ImportAsync(List<string> xmlFiles)
    {
        SetImporting(true);
        try
        {
            StatusText.Text = Loc.T("editor.converting", "Converting {0} theme(s)...", xmlFiles.Count);
            var themes = _services.ThemesDirectory;
            var fonts = Path.Combine(_services.DataDirectory, "Fonts");
            var report = await Task.Run(() => ImportReport.Prepare(xmlFiles, themes, fonts));

            // 1.x themes name hardware sensors ("CPU load"); the user decides which LibreHardwareMonitor sensor each one becomes.
            Dictionary<string, string?>? chosen = null;
            var groups = report.PendingGroups();
            if (groups.Count > 0)
            {
                var (snapshot, text, sample) = await SensorSourceForImportAsync();
                chosen = await SensorMappingWindow.AskAsync(this, groups, snapshot, text, sample);
                if (chosen is null)
                {
                    StatusText.Text = Loc.T("editor.importCancelled", "Import cancelled.");
                    return;
                }
            }

            await Task.Run(() => report.Finish(chosen));

            _services.Fonts.AddDirectory(fonts);
            ThemesChangedRefresh();
            StatusText.Text = Loc.T("editor.imported", "Imported {0} theme(s)", report.Converted);
            await Dialogs.ShowAsync(this, Loc.T("editor.importDone", "Import finished"), report.ToText());

            if (report.LastConverted is { } last && _services.FindTheme(last) is { } entry
                && await ConfirmDiscardAsync())
            {
                DeleteFreshFolder();
                LoadSession(new EditorSession(ThemeJson.Load(entry.JsonPath), entry.Directory), fresh: false);
            }
        }
        finally
        {
            SetImporting(false);
        }
    }

    /// <summary>
    /// A theme saved by an earlier build can still name 1.x sensors. They load unbound; this asks which LibreHardwareMonitor sensor
    /// each one should become (the same window the import uses). Cancelling leaves them for next time.
    /// </summary>
    private async Task PromptLegacySensorsAsync()
    {
        var doc = _session.Document;
        var pending = doc.Flatten()
            .Where(e => e.Sensor?.LegacyKind is not null)
            .Select(e => Enum.TryParse<LegacySensorKind>(e.Sensor!.LegacyKind, ignoreCase: true, out var kind)
                ? new PendingSensor(e, kind, e.Sensor.Param)
                : null)
            .OfType<PendingSensor>()
            .ToList();
        if (pending.Count == 0) return;

        var groups = pending.GroupBy(p => p.Key)
            .Select(g => new PendingGroup(g.Key, g.First().Kind, g.First().Param, g.Count(), 1))
            .ToList();
        var (snapshot, text, sample) = await SensorSourceForImportAsync();
        var chosen = await SensorMappingWindow.AskAsync(this, groups, snapshot, text, sample);
        if (chosen is null)
        {
            StatusText.Text = Loc.T("editor.sensorsPending", "{0} element(s) still use 1.x sensors and are unbound.", pending.Count);
            return;
        }

        _session.Edit(() => LegacyThemeConverter.ApplySensors(pending, (kind, param) =>
            chosen.TryGetValue(new PendingSensor(null!, kind, param).Key, out var path) ? path : null));
        _freshTheme = false;
        Structural();
    }

    /// <summary>The sensors to suggest from: the connected PC, else the one in Settings (fetched now), else the bundled sample.</summary>
    private async Task<(SensorSnapshot Snapshot, string Text, bool Sample)> SensorSourceForImportAsync()
    {
        var hub = _services.Hub;
        if (!hub.Snapshot.IsEmpty)
            return (hub.Snapshot, Loc.T("mapping.live", "Suggestions come from {0}, the PC you are connected to.", hub.Snapshot.ComputerName), false);

        if (_services.UsesLocalSensors)
        {
            var local = await Task.Run(SensorHub.ReadLocalOnce);
            if (!local.IsEmpty)
                return (local, Loc.T("mapping.local", "Suggestions come from {0}, this computer.", local.ComputerName), false);
        }
        else if (LhmClient.TryNormalizeUrl(_services.Settings.LhmUrl, out var url))
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var snapshot = await hub.TestAsync(url, cts.Token);
                if (!snapshot.IsEmpty)
                    return (snapshot, Loc.T("mapping.fetched", "Suggestions come from {0} ({1}).", snapshot.ComputerName, url.Host), false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or System.Text.Json.JsonException) { }
        }

        return (_sample, Loc.T("mapping.sample",
            "LibreHardwareMonitor on your PC could not be reached, so these suggestions come from a sample PC and its sensor paths may not match your hardware. " +
            "Cancel, start LibreHardwareMonitor's remote web server and check the address in Settings, then import again, or choose the sensors by hand."), true);
    }

    // ------------------------------------------------------------------ preview, zoom, keys

    private void CapturePreview()
    {
        if (_view is null) return;
        var doc = _session.Document;
        string preview;
        try
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(doc.Width, doc.Height), new Vector(96, 96));
            bitmap.Render(_view);
            using var png = new MemoryStream();
            bitmap.Save(png, new PngBitmapEncoderOptions());
            preview = ImageData.ToDataUri(png.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = ex.Message;
            return;
        }
        _session.Edit(() => doc.Preview = preview);
        AfterEdit();
        RebuildThemePanel();
        StatusText.Text = Loc.T("editor.previewDone", "Preview image updated.");
    }

    private void GridChanged()
    {
        if (_layer is null) return;
        _layer.SnapToGrid = SnapItem.IsChecked;
        _layer.ShowGrid = ShowGridItem.IsChecked;
        _layer.InvalidateVisual();
        var s = _services.Settings;
        (s.EditorSnapToGrid, s.EditorShowGrid, s.EditorGridSize) = (_layer.SnapToGrid, _layer.ShowGrid, _layer.GridSize);
        _services.SaveSettings();
    }

    private void ApplyZoom()
    {
        var z = ZoomSlider.Value;
        ZoomHost.LayoutTransform = new ScaleTransform(z, z);
        _layer.Zoom = z;
        _layer.InvalidateVisual();
        ZoomLabel.Text = Loc.T("editor.zoom", "Zoom {0}%", (int)Math.Round(z * 100));
    }

    private void Fit()
    {
        var doc = _session.Document;
        var viewport = CanvasScroll.Viewport;
        if (viewport.Width < 100 || viewport.Height < 100 || doc.Width <= 0 || doc.Height <= 0) return;
        ZoomSlider.Value = Math.Clamp(Math.Min((viewport.Width - 60) / doc.Width, (viewport.Height - 60) / doc.Height), 0.25, 3);
    }

    private void OnCanvasWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + Math.Sign(e.Delta.Y) * 0.1, 0.25, 3);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && e.Key == Key.S) { Save(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.N) { _ = NewThemeAsync(); e.Handled = true; return; }

        var typing = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or NumericUpDown or AutoCompleteBox;
        if (typing) return;

        if (ctrl && e.Key == Key.Z) UndoRedo(undo: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        else if (ctrl && e.Key == Key.Y) UndoRedo(undo: false);
        else if (ctrl && e.Key == Key.D) Duplicate();
        else if (e.Key == Key.Delete) DeleteSelected();
        else
        {
            var step = _layer.SnapToGrid ? _layer.GridSize : e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            switch (e.Key)
            {
                case Key.Left: Nudge(-step, 0); break;
                case Key.Right: Nudge(step, 0); break;
                case Key.Up: Nudge(0, -step); break;
                case Key.Down: Nudge(0, step); break;
                default: return;
            }
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ chrome

    private void UpdateTitle()
    {
        var doc = _session.Document;
        Title = $"{Loc.T("editor.title", "Theme editor")} - {doc.Name}{(_session.IsDirty ? " *" : "")}";
        UndoItem.IsEnabled = _session.CanUndo;
        RedoItem.IsEnabled = _session.CanRedo;
        var has = _selected is not null;
        DuplicateItem.IsEnabled = DeleteItem.IsEnabled = ForwardItem.IsEnabled = BackwardItem.IsEnabled = has;
    }

    private void UpdateStatus()
    {
        var doc = _session.Document;
        var theme = $"{doc.Width} x {doc.Height}";
        if (_selected is not { } e)
        {
            StatusText.Text = theme;
            return;
        }
        var (ax, ay) = ElementFactory.AbsolutePosition(doc, e);
        StatusText.Text = Loc.T("editor.statusElement", "{0}  ·  {1}  ·  x {2}, y {3}  ·  {4} x {5}  ·  on panel ({6}, {7})    |    theme {8}",
            e.Name, ElementFactory.LabelOf(e), e.X, e.Y, e.Width, e.Height, ax, ay, theme);
    }
}
