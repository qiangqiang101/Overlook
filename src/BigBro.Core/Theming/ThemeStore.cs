namespace BigBro.Core.Theming;

/// <summary>A theme folder on disk: <c>Themes/&lt;name&gt;/theme.json</c> (optionally with a <c>fonts/</c> folder).</summary>
public sealed record ThemeEntry(string Name, string Directory, string JsonPath, ThemeDocument Document)
{
    /// <summary>The preview image's bytes, or null if the theme has none.</summary>
    public byte[]? PreviewBytes => ImageData.TryGetBytes(Document.Preview, Document);

    public string SizeLabel => $"{Document.Width}x{Document.Height}";
}

public static class ThemeStore
{
    /// <summary>Loads every <c>*/theme.json</c> below <paramref name="themesDirectory"/>. Broken themes are reported, not fatal.</summary>
    public static IReadOnlyList<ThemeEntry> Scan(string themesDirectory, Action<string, Exception>? onError = null)
    {
        var result = new List<ThemeEntry>();
        if (!Directory.Exists(themesDirectory)) return result;

        foreach (var dir in Directory.EnumerateDirectories(themesDirectory).Order(StringComparer.OrdinalIgnoreCase))
        {
            var json = Path.Combine(dir, ThemeJson.ThemeFileName);
            if (!File.Exists(json)) continue;
            try
            {
                var doc = ThemeJson.Load(json);
                result.Add(new ThemeEntry(doc.Name, dir, json, doc));
            }
            catch (Exception ex)
            {
                onError?.Invoke(json, ex);
            }
        }
        return result;
    }

    /// <summary>Turns a relative image path (older theme files) into an absolute path, refusing to leave the theme folder.</summary>
    public static string? ResolveAsset(ThemeDocument doc, string? relative)
    {
        if (string.IsNullOrEmpty(relative) || doc.Directory is null) return null;
        var root = Path.GetFullPath(doc.Directory) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(doc.Directory, relative));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(full) ? full : null;
    }
}
