using BigBro.Core.Theming;

namespace BigBro.Core.Legacy;

/// <summary>Receives the images embedded (as base64) in a 1.x theme and returns the value to store for them in the new JSON.</summary>
public interface IAssetSink
{
    /// <returns>A <c>data:</c> URI, or null if the data is not a usable image.</returns>
    string? Add(byte[] data);
}

/// <summary>Stores each image inline in the theme file as a base64 data URI.</summary>
public sealed class DataUriAssetSink : IAssetSink
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    /// <summary>Number of distinct images embedded.</summary>
    public int Count => _seen.Count;

    public string? Add(byte[] data)
    {
        var uri = ImageData.ToDataUri(data);
        _seen.Add(uri);
        return uri;
    }
}

public static class ImageSniffer
{
    /// <summary>File extension (with dot) for the image in <paramref name="d"/>, judged by its magic bytes.</summary>
    public static string Extension(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return ".png";
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return ".jpg";
        if (d.Length >= 4 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F' && d[3] == '8') return ".gif";
        if (d.Length >= 2 && d[0] == 'B' && d[1] == 'M') return ".bmp";
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E') return ".webp";
        if (d.Length >= 4 && ((d[0] == 'I' && d[1] == 'I' && d[2] == 42 && d[3] == 0) || (d[0] == 'M' && d[1] == 'M' && d[2] == 0 && d[3] == 42))) return ".tif";
        return ".bin";
    }

    public static bool IsAnimatedGif(ReadOnlySpan<byte> d) =>
        d.Length > 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F' && CountGifFrames(d) > 1;

    // Counts graphic control extensions; enough to tell 1 frame from many.
    private static int CountGifFrames(ReadOnlySpan<byte> d)
    {
        var frames = 0;
        for (var i = 0; i + 3 < d.Length; i++)
        {
            if (d[i] == 0x21 && d[i + 1] == 0xF9 && d[i + 2] == 0x04) frames++;
            if (frames > 1) break;
        }
        return frames;
    }
}
