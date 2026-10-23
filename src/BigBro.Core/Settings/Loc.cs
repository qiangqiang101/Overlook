using System.Text.Json;

namespace BigBro.Core.Settings;

/// <summary>
/// UI text lookup. Code always supplies the English text, so a missing language file or key just shows English;
/// <c>Languages/&lt;name&gt;.json</c> files are flat {"key": "text"} maps.
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _current = new(StringComparer.Ordinal);

    public static string Current { get; private set; } = "English";

    /// <summary>Raised after <see cref="Load"/> so open windows can re-apply their texts.</summary>
    public static event Action? Changed;

    public static string T(string key, string english) =>
        _current.TryGetValue(key, out var text) && !string.IsNullOrEmpty(text) ? text : english;

    public static string T(string key, string english, params object[] args) =>
        string.Format(T(key, english), args);

    /// <summary>Names (file names without extension) of the languages found in the given folders; English is always offered.</summary>
    public static IReadOnlyList<string> Available(params string[] directories)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "English" };
        foreach (var dir in directories.Where(Directory.Exists))
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                names.Add(Path.GetFileNameWithoutExtension(file));
        return [.. names];
    }

    public static void Load(string language, params string[] directories)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!language.Equals("English", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var dir in directories.Where(Directory.Exists))
            {
                var file = Path.Combine(dir, language + ".json");
                if (!File.Exists(file)) continue;
                try
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                    if (loaded is null) continue;
                    foreach (var (k, v) in loaded) map[k] = v;
                }
                catch (JsonException)
                {
                    // a broken language file falls back to English
                }
            }
        }
        _current = map;
        Current = map.Count > 0 || language.Equals("English", StringComparison.OrdinalIgnoreCase) ? language : "English";
        Changed?.Invoke();
    }
}
