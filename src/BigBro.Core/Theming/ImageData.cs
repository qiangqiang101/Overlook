using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BigBro.Core.Legacy;

namespace BigBro.Core.Theming;

/// <summary>
/// Images live inside theme.json as <c>data:image/png;base64,...</c> strings. For older v2 files a relative path
/// ("assets/x.png") and a bare base64 string are still understood.
/// </summary>
public static class ImageData
{
    private const int BareBase64MinLength = 512;

    public static bool IsEmbedded(string? value) =>
        value is not null && (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || value.Length >= BareBase64MinLength);

    public static string ToDataUri(ReadOnlySpan<byte> bytes) => $"data:{Mime(bytes)};base64,{Convert.ToBase64String(bytes)}";

    public static string Mime(ReadOnlySpan<byte> bytes) => ImageSniffer.Extension(bytes) switch
    {
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    public static bool IsGif(string? value) =>
        value is not null && (value.StartsWith("data:image/gif", StringComparison.OrdinalIgnoreCase)
                              || (!IsEmbedded(value) && value.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The image file's bytes for any of the supported forms; null if missing or malformed.</summary>
    public static byte[]? TryGetBytes(string? value, ThemeDocument? doc)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!IsEmbedded(value))
        {
            var file = doc is null ? null : ThemeStore.ResolveAsset(doc, value);
            try { return file is null ? null : File.ReadAllBytes(file); }
            catch (IOException) { return null; }
        }

        var payload = value;
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = value.IndexOf(',');
            if (comma < 0) return null;
            payload = value[(comma + 1)..];
        }
        try { return Convert.FromBase64String(payload.Replace(" ", "+")); }
        catch (FormatException) { return null; }
    }
}

/// <summary>Maps big image strings to short ids and back, so editor undo snapshots stay tiny.</summary>
public sealed class ImageTable
{
    private readonly Dictionary<string, string> _byId = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<string, string> _idOf = new();

    public string IdFor(string value)
    {
        if (_idOf.TryGetValue(value, out var id)) return id;
        id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value)))[..20];
        _byId[id] = value;
        _idOf.Add(value, id);
        return id;
    }

    public string? Resolve(string id) => _byId.GetValueOrDefault(id);
}

/// <summary>
/// Normal mode: writes image strings as they are. While an <see cref="ImageTable"/> is active (editor snapshots) long image
/// strings are replaced by <c>@img:id</c> references.
/// </summary>
public sealed class ImageStringConverter : JsonConverter<string>
{
    [ThreadStatic] internal static ImageTable? Active;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (s is not null && Active is { } table && s.StartsWith("@img:", StringComparison.Ordinal))
            return table.Resolve(s[5..]);
        return s;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (Active is { } table && value.Length > 256) writer.WriteStringValue("@img:" + table.IdFor(value));
        else writer.WriteStringValue(value);
    }
}
