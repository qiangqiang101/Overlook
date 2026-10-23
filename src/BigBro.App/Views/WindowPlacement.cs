using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BigBro.Core.Settings;

namespace BigBro.App.Views;

/// <summary>
/// Keeps the app's windows off the display the sensor panel is on (usually a small second screen): a window that opens there
/// is moved to the middle of the primary display instead. Does nothing while the panel is on the primary display.
/// </summary>
internal static class WindowPlacement
{
    public static void Install(AppSettings settings)
    {
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (settings.OpenWindowsOnPrimary && window is not MonitorWindow)
                Dispatcher.UIThread.Post(() => MoveToPrimary(window), DispatcherPriority.Loaded);
        });
    }

    private static void MoveToPrimary(Window window)
    {
        if (!window.IsVisible || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        var panel = desktop.Windows.OfType<MonitorWindow>().FirstOrDefault(w => w.IsVisible);
        var screens = window.Screens;
        if (panel is null || screens.Primary is not { } primary) return;

        var panelScreen = screens.ScreenFromWindow(panel);
        var here = screens.ScreenFromWindow(window);
        if (panelScreen is null || here is null) return;
        if (panelScreen.Bounds == primary.Bounds) return;           // the panel is on the primary display: leave windows alone
        if (here.Bounds != panelScreen.Bounds) return;              // not on the panel's display

        var area = primary.WorkingArea;
        var size = PixelSize.FromSize(window.FrameSize ?? window.ClientSize, window.RenderScaling);
        window.Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - size.Width) / 2),
            area.Y + Math.Max(0, (area.Height - size.Height) / 2));
    }
}
