using BigBro.App.Audio;
using BigBro.App.Rendering;
using BigBro.Core.Sensors;
using BigBro.Core.Settings;
using BigBro.Core.Theming;

namespace BigBro.App;

/// <summary>Everything the windows share: where data lives, settings, the sensor poller, fonts and the theme list.</summary>
public sealed class AppServices : IDisposable
{
    private AppServices(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        ThemesDirectory = Path.Combine(dataDirectory, "Themes");
        SettingsPath = Path.Combine(dataDirectory, "settings.json");
        Directory.CreateDirectory(ThemesDirectory);

        Settings = SettingsStore.Load(SettingsPath);
        Hub = new SensorHub();
        Fonts = new FontRegistry();
        Audio = new AudioCaptureService();
        Rgb = new RgbService(Settings.Rgb, SaveSettings);
        ResetScreenResolution();
    }

    public string DataDirectory { get; }
    public string ThemesDirectory { get; }
    public string SettingsPath { get; }
    public AppSettings Settings { get; }
    public SensorHub Hub { get; }
    public FontRegistry Fonts { get; }
    public AudioCaptureService Audio { get; }
    public RgbService Rgb { get; }
    public IReadOnlyList<ThemeEntry> Themes { get; private set; } = [];

    /// <summary>Theme problems found while scanning (path, message), for the UI to show.</summary>
    public List<(string Path, string Message)> ThemeErrors { get; } = [];

    /// <param name="dataDirectory">Defaults to the per-user data folder; pass one for portable use or tests.</param>
    public static AppServices Create(string? dataDirectory = null)
    {
        var services = new AppServices(dataDirectory ?? SettingsStore.DefaultDataDirectory);
        services.LoadFonts();
        services.RescanThemes();
        return services;
    }

    /// <summary>
    /// The themes that ship with the app. They are read from here and never copied into the user's folder, so deleting one is
    /// done where the app is installed and it does not come back.
    /// </summary>
    public string BundledThemesDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Themes");

    public bool IsBundled(string themeDirectory) =>
        Path.GetFullPath(themeDirectory).StartsWith(Path.GetFullPath(BundledThemesDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private void LoadFonts()
    {
        Fonts.AddDirectory(Path.Combine(AppContext.BaseDirectory, "Fonts"));
        Fonts.AddDirectory(Path.Combine(DataDirectory, "Fonts"));
        Fonts.Register();
    }

    public void RescanThemes()
    {
        ThemeErrors.Clear();
        void OnError(string path, Exception ex) => ThemeErrors.Add((path, ex.Message));
        var themes = ThemeStore.Scan(ThemesDirectory, OnError).ToList();
        // a theme of the same name in the user's folder (for example an edited copy) replaces the bundled one
        themes.AddRange(ThemeStore.Scan(BundledThemesDirectory, OnError)
            .Where(b => !themes.Any(t => t.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase))));
        Themes = themes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var theme in Themes)
            Fonts.AddDirectory(Path.Combine(theme.Directory, "fonts"));
    }

    public ThemeEntry? FindTheme(string? name) =>
        name is null ? null : Themes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Points the "screen resolution" sensor at the primary screen of the main window. A panel window overrides this while it is
    /// open and must call this again when it closes, so the sensor never refers to a disposed window.
    /// </summary>
    public void ResetScreenResolution() => Hub.Local.ScreenResolution = () =>
    {
        try
        {
            var main = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var screen = main?.Screens.Primary;
            return screen is null ? "" : $"{screen.Bounds.Width}x{screen.Bounds.Height}";
        }
        catch (ObjectDisposedException)
        {
            return "";
        }
    };

    private readonly object _saveLock = new();

    /// <summary>Safe to call from any thread (the lighting receiver saves plugin settings from its own thread).</summary>
    public void SaveSettings()
    {
        lock (_saveLock) SettingsStore.Save(Settings, SettingsPath);
    }

    /// <summary>Starts (or restarts) polling LHM for the given theme.</summary>
    public bool StartPolling(ThemeDocument theme, out string? error)
    {
        if (!LhmClient.TryNormalizeUrl(Settings.LhmUrl, out var url))
        {
            error = $"'{Settings.LhmUrl}' is not a valid address.";
            return false;
        }

        Hub.Latitude = Settings.Latitude;
        Hub.Longitude = Settings.Longitude;
        Hub.Start(url, theme.UpdateInterval);
        error = null;
        return true;
    }

    public static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    public void Dispose()
    {
        Rgb.Dispose();
        Audio.Dispose();
        Hub.Dispose();
    }
}
