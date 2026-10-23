using Avalonia.Media;
using Avalonia.Media.Fonts;
using BigBro.Core.Theming;

namespace BigBro.App.Rendering;

/// <summary>
/// Loads the .ttf/.otf files that ship with themes (the 1.x "Fonts" folder) so a theme can ask for
/// "GeForce" or "Blender Pro Bold" by name on any OS, without installing anything.
/// </summary>
public sealed class FontRegistry
{
    private static readonly Uri CollectionKey = new("fonts:bigbro", UriKind.Absolute);

    private readonly FileFontCollection _collection = new(CollectionKey);
    private readonly Dictionary<string, FontFamily> _families = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, bool, bool), Typeface> _typefaces = new();

    public IReadOnlyCollection<string> Families => _families.Keys;

    /// <summary>Registers every font file in <paramref name="directory"/>. Returns how many were loaded.</summary>
    public int AddDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return 0;

        var loaded = 0;
        foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var name = _collection.AddFile(file);
                if (name is null) continue;
                loaded++;
                if (!_families.ContainsKey(name))
                    _families[name] = new FontFamily($"{CollectionKey}#{name}");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                System.Diagnostics.Debug.WriteLine($"Could not load font {file}: {ex.Message}");
            }
        }
        return loaded;
    }

    /// <summary>
    /// Call once, before the first window is created. Fonts added later (themes imported while running) still work,
    /// because they go into this same collection.
    /// </summary>
    public void Register() => FontManager.Current.AddFontCollection(_collection);

    /// <summary>The Avalonia typeface for a theme font; falls back to the system font if the family isn't loaded or installed.</summary>
    public Typeface TypefaceFor(ThemeFont font)
    {
        var key = (font.Family, font.Bold, font.Italic);
        if (_typefaces.TryGetValue(key, out var cached)) return cached;

        var family = _families.TryGetValue(font.Family, out var f) ? f : new FontFamily(font.Family);
        var typeface = new Typeface(family,
            font.Italic ? FontStyle.Italic : FontStyle.Normal,
            font.Bold ? FontWeight.Bold : FontWeight.Normal);
        _typefaces[key] = typeface;
        return typeface;
    }

    private sealed class FileFontCollection(Uri key) : FontCollectionBase
    {
        private readonly List<MemoryStream> _keepAlive = [];

        public override Uri Key => key;

        /// <returns>The font's family name, or null if the file isn't a usable font.</returns>
        public string? AddFile(string path)
        {
            // Skia may read lazily, so hand it a buffer that lives as long as the collection.
            var stream = new MemoryStream(File.ReadAllBytes(path));
            if (!TryAddGlyphTypeface(stream, out var glyphTypeface) || glyphTypeface is null)
            {
                stream.Dispose();
                return null;
            }
            _keepAlive.Add(stream);
            return glyphTypeface.FamilyName;
        }
    }
}
