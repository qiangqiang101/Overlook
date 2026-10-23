using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using BigBro.App.Rendering;
using BigBro.Core.Settings;
using BigBro.Core.Theming;

namespace BigBro.App.Views;

/// <summary>The borderless window that shows a theme. Drag it to move it; right-click for options.</summary>
public sealed class MonitorWindow : Window
{
    private readonly AppServices _services;
    private readonly ThemeView _view;
    private readonly Canvas _overlay = new();
    private readonly DispatcherTimer? _tickTimer;
    private readonly bool _usesAudio;
    private readonly RgbLayer? _rgb;
    private bool _refreshQueued;

    public MonitorWindow(AppServices services, ThemeEntry entry)
    {
        _services = services;
        var theme = entry.Document;

        var rgbOn = theme.Rgb is { Enabled: true };
        _usesAudio = theme.Flatten().Any(e => e is AudioElement)
                     || (rgbOn && services.Settings.Rgb.OfflineEffect == BigBro.Core.Rgb.OfflineEffect.AudioParty);
        if (_usesAudio) services.Audio.Start();

        _view = new ThemeView(theme, services.Fonts, name => services.Settings.FindOverride(theme.Name, name), null, services.Audio.Analyzer)
        {
            HighQualityAudio = services.Settings.HighQualityAudio,
        };
        var stack = new Grid();
        if (rgbOn)
        {
            // The lighting runs underneath the theme, which draws its background image over it as a mask.
            services.Rgb.Acquire();
            _rgb = new RgbLayer(services.Rgb, services.Audio.Analyzer);
            _view.RgbLayerPresent = true;
            stack.Children.Add(_rgb);
        }
        stack.Children.Add(_view);
        stack.Children.Add(_overlay);
        Content = stack;

        Title = $"Overlook - {theme.Name}";
        Icon = AppIcon.Load();
        CanResize = false;
        ShowInTaskbar = true;
        Topmost = services.Settings.TopMost;
        Background = new SolidColorBrush(Color.FromArgb(theme.BackgroundColor.A, theme.BackgroundColor.R, theme.BackgroundColor.G, theme.BackgroundColor.B));
        Opacity = Math.Clamp(theme.Opacity, 0.05, 1.0);
        WindowDecorations = WindowDecorations.None;

        ApplyPixelSize();
        RestorePosition();

        ContextMenu = BuildMenu();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += (_, e) => EndDrag(e.Pointer);
        PointerCaptureLost += (_, _) => _dragging = false;
        PositionChanged += (_, _) => RememberPosition();
        ScalingChanged += (_, _) => ApplyPixelSize();

        services.Hub.Updated += OnHubUpdated;
        services.Hub.Local.ScreenResolution = ScreenResolution;

        if (_view.NeedsTick)
        {
            _tickTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(_view.TickIntervalMs), DispatcherPriority.Render, (_, _) => _view.Tick());
            _tickTimer.Start();
        }

        Opened += (_, _) => { ApplyPixelSize(); Position = KeepOnScreen(Position, Position); _view.Refresh(_services.Hub.CreateResolver()); };
        Closed += (_, _) => Cleanup();
    }

    /// <summary>One theme pixel is one screen pixel, so the window is sized in device pixels converted to DIPs.</summary>
    private void ApplyPixelSize()
    {
        var scale = RenderScaling > 0 ? RenderScaling : 1.0;
        var theme = _view.Document;
        Width = _view.Width = theme.Width / scale;
        Height = _view.Height = theme.Height / scale;
        _view.InvalidateVisual();
        PlaceWebViews(scale);
    }

    /// <summary>YouTube elements are real web views floating above the drawn panel (they can't be drawn into it).</summary>
    private void PlaceWebViews(double scale)
    {
        var slots = _view.YoutubeSlots().ToList();
        if (_overlay.Children.Count != slots.Count)
        {
            _overlay.Children.Clear();
            foreach (var (_, _, id) in slots)
            {
                var web = new NativeWebView();
                if (YoutubeEmbed.ExtractId(id) is { } videoId)
                    web.Source = YoutubeHost.Instance.PageFor(videoId);
                _overlay.Children.Add(web);
            }
        }

        for (var i = 0; i < slots.Count; i++)
        {
            var b = slots[i].Bounds;
            var web = _overlay.Children[i];
            Canvas.SetLeft(web, b.X / scale);
            Canvas.SetTop(web, b.Y / scale);
            web.Width = b.Width / scale;
            web.Height = b.Height / scale;
        }
    }

    private void RestorePosition()
    {
        var s = _services.Settings;
        if (s.MonitorX is { } x && s.MonitorY is { } y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void RememberPosition()
    {
        if (!IsVisible) return;
        _services.Settings.MonitorX = Position.X;
        _services.Settings.MonitorY = Position.Y;
    }

    private ContextMenu BuildMenu()
    {
        var onTop = new MenuItem
        {
            Header = Loc.T("monitor.alwaysOnTop", "Always on top"),
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = Topmost,
        };
        onTop.Click += (_, _) =>
        {
            Topmost = !Topmost;
            onTop.IsChecked = Topmost;
            _services.Settings.TopMost = Topmost;
            _services.SaveSettings();
        };

        var close = new MenuItem { Header = Loc.T("monitor.close", "Close panel") };
        close.Click += (_, _) => Close();

        return new ContextMenu { Items = { onTop, new Separator(), close } };
    }

    private bool _dragging;
    private PixelPoint _dragWindowStart, _dragPointerStart;

    /// <summary>How close (device pixels) to a screen edge the panel is when it snaps flush to it.</summary>
    private const int EdgeSnap = 24;

    // The window is moved by hand (not BeginMoveDrag) so it can be kept on a screen and snapped to its edges while dragging.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _dragPointerStart = this.PointToScreen(e.GetPosition(this));
        _dragWindowStart = Position;
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        var pointer = this.PointToScreen(e.GetPosition(this));
        var wanted = new PixelPoint(_dragWindowStart.X + pointer.X - _dragPointerStart.X, _dragWindowStart.Y + pointer.Y - _dragPointerStart.Y);
        Position = KeepOnScreen(wanted, pointer);
    }

    private void EndDrag(IPointer pointer)
    {
        if (!_dragging) return;
        _dragging = false;
        pointer.Capture(null);
    }

    /// <summary>
    /// Clamps the panel inside the screen under <paramref name="reference"/> (so it can still be dragged onto another
    /// monitor) and snaps it flush to an edge when it comes within <see cref="EdgeSnap"/> pixels.
    /// </summary>
    private PixelPoint KeepOnScreen(PixelPoint wanted, PixelPoint reference)
    {
        var screen = Screens.ScreenFromPoint(reference) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return wanted;

        var area = screen.Bounds;
        var size = PixelSize.FromSize(ClientSize, RenderScaling);
        return new PixelPoint(Fit(wanted.X, size.Width, area.X, area.Right), Fit(wanted.Y, size.Height, area.Y, area.Bottom));

        static int Fit(int position, int length, int low, int high)
        {
            // A panel larger than the screen can slide between "left edges aligned" and "right edges aligned".
            var min = Math.Min(low, high - length);
            var max = Math.Max(low, high - length);
            var p = Math.Clamp(position, min, max);
            if (Math.Abs(p - low) < EdgeSnap) return low;
            if (Math.Abs(p + length - high) < EdgeSnap) return high - length;
            return p;
        }
    }

    private void OnHubUpdated()
    {
        // Poll results arrive on a worker thread; coalesce so a slow UI never queues up redraws.
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (IsVisible) _view.Refresh(_services.Hub.CreateResolver());
        }, DispatcherPriority.Background);
    }

    private string ScreenResolution()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            return screen is null ? "" : $"{screen.Bounds.Width}x{screen.Bounds.Height}";
        }
        catch (ObjectDisposedException)
        {
            return "";   // window already closed
        }
    }

    private void Cleanup()
    {
        _services.Hub.Updated -= OnHubUpdated;
        _services.ResetScreenResolution();
        _tickTimer?.Stop();
        if (_usesAudio) _services.Audio.Stop();
        if (_rgb is not null)
        {
            _rgb.Dispose();
            _services.Rgb.Release();
        }
        _services.SaveSettings();
        _view.Dispose();
    }
}
