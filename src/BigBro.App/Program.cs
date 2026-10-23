using Avalonia;
using BigBro.App.Cli;
using BigBro.Core.Settings;

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

        CrashLog.Install(App.DataDirectoryOverride ?? SettingsStore.DefaultDataDirectory);

        if (args.Contains("--audio-test"))
            return AudioTest();

        if (args.Contains("--render"))
            return RenderCommand.Run(args, BuildAvaloniaApp);
        if (args.Contains("--screenshot"))
            return ScreenshotCommand.Run(args, BuildAvaloniaApp);

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Crash while running", ex);
            throw;
        }
    }

    /// <summary>Starts the real audio capture for a few seconds and prints what it hears (play some audio meanwhile).</summary>
    private static int AudioTest()
    {
        using var audio = new Audio.AudioCaptureService();
        audio.Start();
        Console.WriteLine($"running={audio.IsRunning} error={audio.Error ?? "none"} sampleRate={audio.Analyzer.SampleRate}");
        for (var i = 0; i < 12; i++)
        {
            Thread.Sleep(500);
            audio.Analyzer.Update();
            var bands = audio.Analyzer.Bands(16, false, Core.Audio.ScalingStrategy.Decibel);
            Console.WriteLine($"{i * 0.5 + 0.5,4:0.0}s hasData={audio.Analyzer.HasData} peak={bands.Max():0.00} bands={string.Join(' ', bands.Select(b => (int)(b * 9)))}");
        }
        return 0;
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
