using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BigBro.App.Editor;
using BigBro.App.Views;
using BigBro.Core.Settings;

namespace BigBro.App.Cli;

/// <summary>
/// <c>Overlook --screenshot main|settings|editor --out shot.png [--theme "NVIDIA"] [--select ElementName] [--tab 1]</c><br/>
/// Opens a window, waits for it to settle and saves a picture of it. Used to check the UI without a human.
/// </summary>
internal static class ScreenshotCommand
{
    public static int Run(string[] args, Func<AppBuilder> buildApp)
    {
        var which = Program.ArgValue(args, "--screenshot") ?? "main";
        var outPath = Program.ArgValue(args, "--out") ?? $"{which}.png";
        var themeName = Program.ArgValue(args, "--theme");
        var select = Program.ArgValue(args, "--select");
        var tab = int.TryParse(Program.ArgValue(args, "--tab"), out var t) ? t : 0;

        buildApp().SetupWithoutStarting();
        var services = AppServices.Create(App.DataDirectoryOverride);
        Loc.Load(services.Settings.Language, SettingsWindow.LanguageFolders(services));
        var theme = services.FindTheme(themeName) ?? services.Themes.FirstOrDefault();
        if (theme is not null) services.Settings.CurrentTheme = theme.Name;

        Window window = which switch
        {
            "youtube" => YoutubeTestWindow(Program.ArgValue(args, "--id") ?? "dQw4w9WgXcQ"),
            "mapping" => MappingTestWindow(Program.ArgValue(args, "--xml"), services),
            "color" => new Window { Width = 420, Height = 520, Content = new ColorView { Color = Avalonia.Media.Colors.OrangeRed, IsAlphaVisible = true } },
            "settings" => new SettingsWindow(services),
            "monitor" => new MonitorWindow(services, theme!),
            "picker" => PickerTestWindow(),
            "editor" => new ThemeEditorWindow(services, theme),
            _ => new MainWindow(services),
        };

        if (window is ThemeEditorWindow editor)
        {
            editor.PrepareForScreenshot(select, tab);
        }
        else if (window is MainWindow main)
        {
            main.SelectForScreenshot(theme?.Name);
        }

        window.Show();
        if (window is ThemeEditorWindow floating && Program.ArgValue(args, "--undock") is { } side) floating.UndockForScreenshot(side);

        using var cts = new CancellationTokenSource();
        var delay = int.TryParse(Program.ArgValue(args, "--delay"), out var d) ? d : 2500;
        DispatcherTimer.RunOnce(() =>
        {
            var scale = window.RenderScaling;
            var size = PixelSize.FromSize(window.ClientSize, scale);
            using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            bitmap.Render(window);
            bitmap.Save(outPath, new PngBitmapEncoderOptions());
            Console.WriteLine($"{which} -> {Path.GetFullPath(outPath)} ({size.Width}x{size.Height})");
            cts.Cancel();
        }, TimeSpan.FromMilliseconds(delay));

        Dispatcher.UIThread.MainLoop(cts.Token);
        window.Close();
        services.Dispose();
        return 0;
    }

    /// <summary>A window with an open sensor picker, for capturing the desktop (popups are not part of the window's own render).</summary>
    private static Window PickerTestWindow()
    {
        using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Overlook/Assets/sample-lhm.json"));
        var snapshot = BigBro.Core.Sensors.LhmParser.Parse(stream);
        var picker = new SensorPicker(snapshot, includeSpecials: true) { Width = 520 };
        var window = new Window
        {
            Width = 760, Height = 640, Title = "picker-test",
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(40, 40),
            Content = new StackPanel { Margin = new Thickness(20), Children = { new TextBlock { Text = "Sensor" }, picker } },
        };
        window.Opened += (_, _) => DispatcherTimer.RunOnce(() => picker.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)), TimeSpan.FromMilliseconds(600));
        return window;
    }

    private static Window YoutubeTestWindow(string id)
    {
        var web = new NativeWebView();
        web.Source = BigBro.App.Rendering.YoutubeHost.Instance.PageFor(id);
        return new Window
        {
            Width = 800, Height = 480, Title = "youtube-test", Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(40, 40), Content = web,
        };
    }

    /// <summary>The import prompt for a few real 1.x themes, using the bundled sample PC as the sensor source.</summary>
    private static Window MappingTestWindow(string? xmlFolder, AppServices services)
    {
        var files = Directory.EnumerateFiles(xmlFolder ?? "", "*.xml").Take(6).ToList();
        var report = ImportReport.Prepare(files, Path.Combine(Path.GetTempPath(), "bigbro-mapping-test"), Path.Combine(Path.GetTempPath(), "bigbro-mapping-fonts"));
        using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Overlook/Assets/sample-lhm.json"));
        var snapshot = BigBro.Core.Sensors.LhmParser.Parse(stream);
        return new SensorMappingWindow(report.PendingGroups(), snapshot, "Suggestions come from ROGPC2, the PC you are connected to.", sampleData: false);
    }
}
