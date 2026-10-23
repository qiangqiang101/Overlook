using System.Text.Json;

namespace BigBro.Core.Theming;

/// <summary>
/// What the theme list needs to show a theme: name, size, author, tags and the preview picture. Read without loading the
/// whole theme, whose embedded images can add up to tens of megabytes of text each.
/// </summary>
public sealed record ThemeHeader(
    string Name, string? Author, string Version, IReadOnlyList<string> Tags,
    int Width, int Height, int UpdateInterval, bool HasRgb, byte[]? PreviewBytes);

public static class ThemeHeaderReader
{
    private static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// Reads the top-level fields of a theme file up to its element list, streaming the file so that only one token (at most a
    /// single embedded image) is ever held in memory. The background image's text is skipped, not kept.
    /// </summary>
    public static ThemeHeader Read(string themeJsonPath)
    {
        using var stream = File.OpenRead(themeJsonPath);

        var name = "Untitled Theme";
        string? author = null, version = "1.0", previewUri = null;
        int width = 480, height = 320, interval = 3000, formatVersion = ThemeJson.CurrentFormatVersion;
        var tags = new List<string>();
        var hasRgb = false;
        var rgbEnabled = true;
        string? property = null, subProperty = null;
        var done = false;

        var buffer = new byte[256 * 1024];
        var used = 0;
        var final = false;
        var state = new JsonReaderState(ReaderOptions);

        while (!done && !final)
        {
            if (used == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);       // a single image token is larger than the buffer
            var read = stream.Read(buffer, used, buffer.Length - used);
            if (read == 0) final = true;
            else used += read;

            var reader = new Utf8JsonReader(buffer.AsSpan(0, used), final, state);
            while (reader.Read())
            {
                var depth = reader.CurrentDepth;
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName when depth == 1:
                        property = reader.GetString();
                        subProperty = null;
                        if (property == "elements") done = true;
                        break;
                    case JsonTokenType.PropertyName when depth == 2:
                        subProperty = reader.GetString();
                        break;
                    case JsonTokenType.String when depth == 1:
                        switch (property)
                        {
                            case "name": name = reader.GetString() ?? name; break;
                            case "author": author = reader.GetString(); break;
                            case "version": version = reader.GetString() ?? version; break;
                            case "preview": previewUri = reader.GetString(); break;
                        }
                        break;
                    case JsonTokenType.Number when depth == 1:
                        var number = (int)reader.GetDouble();
                        switch (property)
                        {
                            case "width": width = number; break;
                            case "height": height = number; break;
                            case "updateInterval": interval = number; break;
                            case "formatVersion": formatVersion = number; break;
                        }
                        break;
                    case JsonTokenType.String when depth == 2 && property == "tags":
                        tags.Add(reader.GetString() ?? "");
                        break;
                    case JsonTokenType.StartObject when depth == 1 && property == "rgb":
                        hasRgb = true;
                        break;
                    case JsonTokenType.True or JsonTokenType.False when depth == 2 && property == "rgb" && subProperty == "enabled":
                        rgbEnabled = reader.TokenType == JsonTokenType.True;
                        break;
                }
                if (done) break;
            }
            if (done) break;

            state = reader.CurrentState;
            var consumed = (int)reader.BytesConsumed;
            Buffer.BlockCopy(buffer, consumed, buffer, 0, used - consumed);
            used -= consumed;
        }

        if (formatVersion > ThemeJson.CurrentFormatVersion)
            throw new InvalidDataException($"{themeJsonPath} uses theme format {formatVersion}; this build understands up to {ThemeJson.CurrentFormatVersion}.");

        return new ThemeHeader(name, author, version ?? "1.0", tags, width, height, interval, hasRgb && rgbEnabled, ImageData.TryGetBytes(previewUri, null));
    }
}
