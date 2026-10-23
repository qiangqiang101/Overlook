using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace BigBro.App.Rendering;

/// <summary>All frames of an animated GIF, decoded up front, with each frame's display time.</summary>
public sealed class AnimatedGif : IDisposable
{
    /// <summary>Animations whose decoded frames would exceed this many bytes are shown as a still image instead.</summary>
    private const long MaxBytes = 384L * 1024 * 1024;

    private readonly Bitmap[] _frames;
    private readonly int[] _durations;
    private readonly int _total;

    private AnimatedGif(Bitmap[] frames, int[] durations)
    {
        _frames = frames;
        _durations = durations;
        _total = Math.Max(1, durations.Sum());
    }

    public int FrameCount => _frames.Length;

    /// <summary>The frame showing at <paramref name="timeMs"/> since the animation started (loops forever).</summary>
    public Bitmap At(long timeMs)
    {
        var t = (int)(timeMs % _total);
        for (var i = 0; i < _frames.Length; i++)
        {
            if (t < _durations[i]) return _frames[i];
            t -= _durations[i];
        }
        return _frames[^1];
    }

    /// <returns>null if the stream isn't a multi-frame animation (use a normal bitmap then) or is too large to keep in memory.</returns>
    public static AnimatedGif? TryLoad(Stream stream)
    {
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.FrameCount < 2) return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if ((long)info.BytesSize * codec.FrameCount > MaxBytes) return null;

        var infos = codec.FrameInfo;
        var bitmaps = new SKBitmap[codec.FrameCount];
        var frames = new Bitmap[codec.FrameCount];
        var durations = new int[codec.FrameCount];

        try
        {
            for (var i = 0; i < bitmaps.Length; i++)
            {
                var bmp = new SKBitmap(info);
                var prior = infos[i].RequiredFrame;
                if (prior >= 0) bitmaps[prior].CopyTo(bmp);

                var options = prior >= 0 ? new SKCodecOptions(i, prior) : new SKCodecOptions(i);
                var result = codec.GetPixels(info, bmp.GetPixels(), options);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    bmp.Dispose();
                    return null;
                }
                bitmaps[i] = bmp;
                // Browsers treat 0/10 ms delays as 100 ms; do the same so fast GIFs don't spin.
                durations[i] = infos[i].Duration <= 10 ? 100 : infos[i].Duration;
                frames[i] = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, bmp.GetPixels(),
                    new PixelSize(info.Width, info.Height), new Vector(96, 96), info.RowBytes);
            }
            return new AnimatedGif(frames, durations);
        }
        finally
        {
            foreach (var b in bitmaps) b?.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var f in _frames) f.Dispose();
    }
}
