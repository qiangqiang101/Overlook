using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigBro.Core.Theming;

public static class ThemeJson
{
    public const int CurrentFormatVersion = 2;
    public const string ThemeFileName = "theme.json";

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Hand-edited files shouldn't have to put "type" first.
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static ThemeDocument Load(string themeJsonPath)
    {
        using var stream = File.OpenRead(themeJsonPath);
        var doc = JsonSerializer.Deserialize<ThemeDocument>(stream, Options)
                  ?? throw new InvalidDataException($"{themeJsonPath} is empty.");
        if (doc.FormatVersion > CurrentFormatVersion)
            throw new InvalidDataException(
                $"{themeJsonPath} uses theme format {doc.FormatVersion}; this build understands up to {CurrentFormatVersion}.");
        doc.Directory = Path.GetDirectoryName(Path.GetFullPath(themeJsonPath));
        return doc;
    }

    public static void Save(ThemeDocument doc, string themeJsonPath)
    {
        using var stream = File.Create(themeJsonPath);
        JsonSerializer.Serialize(stream, doc, Options);
    }

    /// <summary>Compact snapshot for undo: images are replaced by short references held in <paramref name="images"/>.</summary>
    public static string Snapshot(ThemeDocument doc, ImageTable images)
    {
        ImageStringConverter.Active = images;
        try { return JsonSerializer.Serialize(doc, Options); }
        finally { ImageStringConverter.Active = null; }
    }

    public static ThemeDocument FromSnapshot(string json, ImageTable images)
    {
        ImageStringConverter.Active = images;
        try { return Deserialize(json); }
        finally { ImageStringConverter.Active = null; }
    }

    public static string Serialize(ThemeDocument doc) => JsonSerializer.Serialize(doc, Options);

    public static ThemeDocument Deserialize(string json)
        => JsonSerializer.Deserialize<ThemeDocument>(json, Options) ?? throw new InvalidDataException("Empty theme.");
}
