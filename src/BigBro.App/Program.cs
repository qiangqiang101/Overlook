using Avalonia;
using BigBro.App.Cli;

namespace BigBro.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var data = ArgValue(args, "--data");
        if (data is not null) App.DataDirectoryOverride = Path.GetFullPath(data);
        else if (File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt")))
            App.DataDirectoryOverride = Path.Combine(AppContext.BaseDirectory, "Data");     // portable build: keep settings next to the program

        if (args.Contains("--render"))
            return RenderCommand.Run(args, BuildAvaloniaApp);
        if (args.Contains("--screenshot"))
            return ScreenshotCommand.Run(args, BuildAvaloniaApp);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();

    internal static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
