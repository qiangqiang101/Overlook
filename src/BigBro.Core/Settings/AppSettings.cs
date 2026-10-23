using System.Text.Json;
using System.Text.Json.Serialization;
using BigBro.Core.Sensors;

namespace BigBro.Core.Settings;

public sealed class AppSettings
{
    /// <summary>LibreHardwareMonitor remote web server, e.g. http://192.168.0.8:8085/data.json</summary>
    public string LhmUrl { get; set; } = LhmClient.DefaultUrl;
    public string? CurrentTheme { get; set; }
    /// <summary>UI language: the file name (without extension) of a Languages/*.json file.</summary>
    public string Language { get; set; } = "English";
    /// <summary>Start Overlook when the user signs in.</summary>
    public bool StartWithSystem { get; set; }
    /// <summary>Minimizing the main window hides it to the tray/notification area. Off by default on Linux, where not every desktop shows tray icons.</summary>
    public bool MinimizeToTray { get; set; } = OperatingSystem.IsWindows();
    /// <summary>Anti-aliased audio visualizer (costs a little more CPU).</summary>
    public bool HighQualityAudio { get; set; } = true;
    /// <summary>Theme editor: snap moved/resized elements to the grid.</summary>
    public bool EditorSnapToGrid { get; set; }
    public bool EditorShowGrid { get; set; }
    public int EditorGridSize { get; set; } = 10;
    public double EditorLeftWidth { get; set; } = 270;
    public double EditorRightWidth { get; set; } = 380;
    /// <summary>The RGB background: where the lighting comes from and how it is drawn.</summary>
    public BigBro.Core.Rgb.RgbSettings Rgb { get; set; } = new();
    public bool TopMost { get; set; } = true;
    /// <summary>Windows that would open on the sensor panel's display open on the primary display instead (unless the panel is on it).</summary>
    public bool OpenWindowsOnPrimary { get; set; } = true;
    /// <summary>Show the panel automatically when the app starts.</summary>
    public bool AutoShowMonitor { get; set; }
    /// <summary>Ask GitHub for a newer release when the app starts.</summary>
    public bool CheckForUpdates { get; set; } = true;
    public int? MonitorX { get; set; }
    public int? MonitorY { get; set; }
    /// <summary>Open-Meteo coordinates for the weather sensors.</summary>
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }

    /// <summary>User edits per theme (theme name → element name → override). The 1.x "presets".</summary>
    public Dictionary<string, Dictionary<string, ElementOverride>> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ElementOverride? FindOverride(string themeName, string elementName) =>
        Overrides.TryGetValue(themeName, out var perTheme) && perTheme.TryGetValue(elementName, out var o) ? o : null;
}

/// <summary>What the user changed on one element of a theme without editing the theme itself.</summary>
public sealed class ElementOverride
{
    /// <summary>Replacement text (text elements with allowUserEdit).</summary>
    public string? Text { get; set; }
    /// <summary>Replacement sensor parameter: GPU or disk index, fan number, drive...</summary>
    public string? Param { get; set; }
    /// <summary>Absolute path of a replacement image (image elements with allowUserEdit).</summary>
    public string? Image { get; set; }
    public string? YoutubeId { get; set; }
    /// <summary>Replacement sensor for an element that the theme binds to one ("Custom this theme"). Kept here, never in the theme file.</summary>
    public Theming.SensorBinding? Sensor { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Text is null && Param is null && Image is null && YoutubeId is null && Sensor is null;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Per-user data folder: %AppData%\Overlook on Windows, ~/.config/Overlook on Linux.</summary>
    public static string DefaultDataDirectory { get; } = ResolveDataDirectory();

    private static string ResolveDataDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var current = Path.Combine(root, "Overlook");
        var previous = Path.Combine(root, "BigBroMonitor2");    // the folder used before the app was renamed
        try
        {
            if (!Directory.Exists(current) && Directory.Exists(previous)) Directory.Move(previous, current);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return current;
    }

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize<AppSettings>(stream, Options) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A damaged settings file shouldn't stop the app from starting; keep it aside for inspection.
            try { File.Move(path, path + ".bad", overwrite: true); } catch (IOException) { }
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
            JsonSerializer.Serialize(stream, settings, Options);
        File.Move(tmp, path, overwrite: true);
    }
}
