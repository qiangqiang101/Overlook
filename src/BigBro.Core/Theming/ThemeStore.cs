namespace BigBro.Core.Theming;

/// <summary>
/// A theme folder on disk: <c>Themes/&lt;name&gt;/theme.json</c> (optionally with a <c>fonts/</c> folder). Only the light
/// <see cref="Header"/> is kept for every theme; the full <see cref="Document"/> (with all its embedded images) is loaded on first
/// use and dropped again with <see cref="Release"/>, so a library of dozens of large themes costs no memory while it is not open.
/// </summary>
public sealed class ThemeEntry
{
    private ThemeDocument? _document;

    public ThemeEntry(string directory, string jsonPath, ThemeHeader header)
    {
        Directory = directory;
        JsonPath = jsonPath;
        Header = header;
    }

    public string Directory { get; }
    public string JsonPath { get; }
    public ThemeHeader Header { get; }

    public string Name => Header.Name;
    public string SizeLabel => $"{Header.Width}x{Header.Height}";

    /// <summary>The preview image's bytes, or null if the theme has none.</summary>
    public byte[]? PreviewBytes => Header.PreviewBytes;

    public bool IsLoaded => _document is not null;

    /// <summary>The whole theme, loaded from disk the first time it is asked for. Throws if the file can't be read.</summary>
    public ThemeDocument Document => _document ??= ThemeJson.Load(JsonPath);

    /// <summary>Drops the loaded theme (its images are by far the biggest part) and returns the memory.</summary>
    public void Release()
    {
        if (_document is null) return;
        _document = null;
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}

public static class ThemeStore
{
    // headers are cheap but reading dozens of big files is not: remember them until the file changes
    private static readonly Dictionary<string, (long Length, DateTime Written, ThemeHeader Header)> HeaderCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lists every <c>*/theme.json</c> below <paramref name="themesDirectory"/> by header only. Broken themes are reported, not fatal.</summary>
    public static IReadOnlyList<ThemeEntry> Scan(string themesDirectory, Action<string, Exception>? onError = null)
    {
        var result = new List<ThemeEntry>();
        if (!System.IO.Directory.Exists(themesDirectory)) return result;

        foreach (var dir in System.IO.Directory.EnumerateDirectories(themesDirectory).Order(StringComparer.OrdinalIgnoreCase))
        {
            var json = Path.Combine(dir, ThemeJson.ThemeFileName);
            if (!File.Exists(json)) continue;
            try
            {
                result.Add(new ThemeEntry(dir, json, HeaderOf(json)));
            }
            catch (Exception ex)
            {
                onError?.Invoke(json, ex);
            }
        }
        return result;
    }

    private static ThemeHeader HeaderOf(string json)
    {
        var info = new FileInfo(json);
        lock (HeaderCache)
        {
            if (HeaderCache.TryGetValue(json, out var cached) && cached.Length == info.Length && cached.Written == info.LastWriteTimeUtc)
                return cached.Header;
        }
        var header = ThemeHeaderReader.Read(json);
        lock (HeaderCache) HeaderCache[json] = (info.Length, info.LastWriteTimeUtc, header);
        return header;
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
