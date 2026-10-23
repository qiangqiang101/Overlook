using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BigBro.App.Views;
using BigBro.Core.Settings;

namespace BigBro.App;

public sealed class App : Application
{
    /// <summary>Set by <see cref="Program"/> before the app starts.</summary>
    public static string? DataDirectoryOverride { get; set; }

    public AppServices Services { get; private set; } = null!;

    private MainWindow? _main;
    private TrayIcon? _tray;
    private NativeMenuItem? _openItem, _panelItem, _exitItem;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = AppServices.Create(DataDirectoryOverride);
        Loc.Load(Services.Settings.Language, SettingsWindow.LanguageFolders(Services));

        WindowPlacement.Install(Services.Settings);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _main = new MainWindow(Services);
            desktop.MainWindow = _main;
            desktop.Exit += (_, _) =>
            {
                _tray?.Dispose();
                Services.Dispose();
            };
            CreateTray(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _openItem = new NativeMenuItem();
        _openItem.Click += (_, _) => _main?.Restore();
        _panelItem = new NativeMenuItem();
        _panelItem.Click += (_, _) => _main?.TogglePanel();
        _exitItem = new NativeMenuItem();
        _exitItem.Click += (_, _) => desktop.Shutdown();

        _tray = new TrayIcon
        {
            Icon = AppIcon.Load(),
            ToolTipText = "Overlook",
            Menu = new NativeMenu { Items = { _openItem, _panelItem, new NativeMenuItemSeparator(), _exitItem } },
        };
        _tray.Clicked += (_, _) => _main?.Restore();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });

        Loc.Changed += ApplyTrayLanguage;
        ApplyTrayLanguage();
    }

    private void ApplyTrayLanguage()
    {
        if (_openItem is null) return;
        _openItem.Header = Loc.T("tray.open", "Open Overlook");
        _panelItem!.Header = Loc.T("tray.togglePanel", "Show / hide panel");
        _exitItem!.Header = Loc.T("tray.exit", "Exit");
        _tray!.ToolTipText = Loc.T("tray.message", "Overlook");
    }
}
