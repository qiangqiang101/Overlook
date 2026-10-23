using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using BigBro.Core.Theming;

namespace BigBro.App.Rendering;

/// <summary>
/// Decodes and caches the images a theme refers to (inline data URIs, older relative paths, animated GIFs).
/// Can be shared between views of the same theme.
/// </summary>
public sealed class ThemeAssets : IDisposable
{
    private sealed class Entry
    {
        public Bitmap? Bitmap;
        public AnimatedGif? Gif;
    }

    /// <summary>Theme strings can be megabytes long; compare them by identity so a lookup is O(1) every frame.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => ReferenceEquals(x, y);
        public int GetHashCode(string s) => RuntimeHelpers.GetHashCode(s);
    }

    private readonly ThemeDocument _theme;
    private readonly Dictionary<string, Entry> _byReference = new(new ReferenceComparer());
    private readonly Dictionary<string, Entry> _byContent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Entry> _all = [];

    public ThemeAssets(ThemeDocument theme) => _theme = theme;

    /// <summary>Time since the view started animating; picks the GIF frame to return.</summary>
    public long ClockMs { get; set; }

    /// <summary>True once an animated GIF has been loaded, so the view knows it must keep repainting.</summary>
    public bool HasAnimation => _all.Any(e => e.Gif is not null);

    /// <summary>Bitmap for an image value as stored in the theme.</summary>
    public Bitmap? Get(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (_byReference.TryGetValue(value, out var entry)) return Frame(entry);

        // Same picture repeated in several elements: decode it once.
        var key = ContentKey(value);
        if (!_byContent.TryGetValue(key, out entry))
        {
            entry = Load(ImageData.TryGetBytes(value, _theme));
            _byContent[key] = entry;
        }
        _byReference[value] = entry;
        return Frame(entry);
    }

    /// <summary>Bitmap for an absolute file path (user-chosen replacement images).</summary>
    public Bitmap? GetFile(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return null;
        if (!_byFile.TryGetValue(fullPath, out var entry))
        {
            byte[]? bytes = null;
            try { bytes = File.ReadAllBytes(fullPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            _byFile[fullPath] = entry = Load(bytes);
        }
        return Frame(entry);
    }

    private Bitmap? Frame(Entry e) => e.Gif is { } gif ? gif.At(ClockMs) : e.Bitmap;

    private Entry Load(byte[]? bytes)
    {
        var entry = new Entry();
        _all.Add(entry);
        if (bytes is null) return entry;

        try
        {
            if (bytes.Length > 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
            {
                using var gifStream = new MemoryStream(bytes);
                entry.Gif = AnimatedGif.TryLoad(gifStream);
                if (entry.Gif is not null) return entry;
            }
            using var stream = new MemoryStream(bytes);
            entry.Bitmap = new Bitmap(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not decode an image: {ex.Message}");
        }
        return entry;
    }

    // Short strings (paths) are their own key; long ones are identified by a hash so the dictionary stays small.
    private static string ContentKey(string value) =>
        value.Length < 256 ? value : "#" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    /// <summary>True if the theme contains any GIF, i.e. it may need a fast repaint timer.</summary>
    public static bool MayAnimate(ThemeDocument theme)
    {
        if (ImageData.IsGif(theme.BackgroundImage)) return true;
        foreach (var e in theme.Flatten())
        {
            switch (e)
            {
                case TextElement t when ImageData.IsGif(t.Image): return true;
                case ImageElement i when ImageData.IsGif(i.Image) || ImageData.IsGif(i.BackgroundImage) || i.DynamicImages.Any(d => ImageData.IsGif(d.Image)): return true;
                case BarElement b when ImageData.IsGif(b.Texture): return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        foreach (var e in _all)
        {
            e.Bitmap?.Dispose();
            e.Gif?.Dispose();
        }
        _all.Clear();
        _byReference.Clear();
        _byContent.Clear();
        _byFile.Clear();
    }
}
