using Avalonia;
using Avalonia.Media.Imaging;
using BigBro.App.Rendering;
using BigBro.Core.Audio;
using BigBro.Core.Sensors;
using BigBro.Core.Theming;

namespace BigBro.App.Cli;

/// <summary>
/// <c>Overlook --render &lt;theme folder|theme.json&gt; --out shot.png [--sensors data.json | --url http://host:8085/data.json] [--samples 60]</c><br/>
/// Draws a theme to a PNG without opening a window; handy for checking converted themes and for bug reports.
/// </summary>
internal static class RenderCommand
{
    public static int Run(string[] args, Func<AppBuilder> buildApp)
    {
        var themeArg = Program.ArgValue(args, "--render");
        var outPath = Program.ArgValue(args, "--out") ?? "theme.png";
        var sensorsFile = Program.ArgValue(args, "--sensors");
        var url = Program.ArgValue(args, "--url");
        var samples = int.TryParse(Program.ArgValue(args, "--samples"), out var n) ? Math.Max(1, n) : 1;

        if (string.IsNullOrWhiteSpace(themeArg))
        {
            Console.Error.WriteLine("Usage: Overlook --render <theme folder|theme.json> --out shot.png [--sensors data.json | --url <lhm url>] [--samples N]");
            return 1;
        }

        var jsonPath = Directory.Exists(themeArg) ? Path.Combine(themeArg, ThemeJson.ThemeFileName) : themeArg;
        if (!File.Exists(jsonPath))
        {
            Console.Error.WriteLine($"Theme not found: {jsonPath}");
            return 1;
        }

        buildApp().SetupWithoutStarting();

        var theme = ThemeJson.Load(jsonPath);
        var fonts = new FontRegistry();
        fonts.AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));
        fonts.AddDirectory(Path.Combine(theme.Directory!, "fonts"));
        fonts.Register();

        SensorSnapshot snapshot = SensorSnapshot.Empty;
        try
        {
            if (sensorsFile is not null)
            {
                using var stream = File.OpenRead(sensorsFile);
                snapshot = LhmParser.Parse(stream);
            }
            else if (url is not null && LhmClient.TryNormalizeUrl(url, out var uri))
            {
                using var client = new LhmClient();
                snapshot = client.FetchAsync(uri).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read sensor data: {ex.Message}");
            return 1;
        }

        var local = new LocalInfo { ScreenResolution = () => $"{theme.Width}x{theme.Height}" };
        var resolver = new SensorResolver(snapshot, local);

        // --audio-demo feeds a few tones so audio visualizers have something to show; --wait lets animations advance.
        SpectrumAnalyzer? analyzer = null;
        if (args.Contains("--audio-demo"))
        {
            analyzer = new SpectrumAnalyzer();
            var demo = new float[SpectrumAnalyzer.FftSize * 2];
            var rng = new Random(7);
            for (var i = 0; i < SpectrumAnalyzer.FftSize; i++)
            {
                double t = i / 48000.0, v = 0;
                foreach (var (hz, amp) in new[] { (60.0, 0.5), (220.0, 0.3), (880.0, 0.15), (3500.0, 0.08), (9000.0, 0.03) })
                    v += amp * Math.Sin(2 * Math.PI * hz * t);
                demo[2 * i] = demo[2 * i + 1] = (float)(v * 0.5 + (rng.NextDouble() - 0.5) * 0.01);
            }
            analyzer.AddSamples(demo, 2);
        }
        var waitMs = int.TryParse(Program.ArgValue(args, "--wait"), out var w) ? w : 0;

        using var view = new ThemeView(theme, fonts, null, null, analyzer);
        view.Width = theme.Width;
        view.Height = theme.Height;
        view.Measure(new Size(theme.Width, theme.Height));
        view.Arrange(new Rect(0, 0, theme.Width, theme.Height));

        for (var i = 0; i < samples; i++) view.Refresh(resolver);
        if (waitMs > 0) Thread.Sleep(waitMs);
        view.Tick();
        if (analyzer is not null) { view.Tick(); view.Tick(); }

        using var bitmap = new RenderTargetBitmap(new PixelSize(theme.Width, theme.Height), new Vector(96, 96));
        bitmap.Render(view);
        bitmap.Save(outPath, new PngBitmapEncoderOptions());

        Console.WriteLine($"{theme.Name}: {theme.Width}x{theme.Height} -> {Path.GetFullPath(outPath)}");
        return 0;
    }
}
