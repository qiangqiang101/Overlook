using BigBro.Core.Theming;

namespace BigBro.Core.Rgb;

public enum LedShape { Rectangle, RoundedRectangle, Sphere }

/// <summary>Matrix shapes the RGB plugins can send. Numbering matches the wire format.</summary>
public enum MatrixSizeType
{
    Landscape4_3, Portrait4_3, Landscape5_4, Portrait5_4, Landscape16_9, Portrait16_9, Landscape16_10, Portrait16_10,
    Landscape21_9, Portrait21_9, Landscape32_9, Portrait32_9, Landscape4_1, Portrait4_1,
}

public enum MatrixSizeTier { Small, Normal, Large, XLarge }

/// <summary>What is shown when no lighting software is sending (called "shutdown effect" in the plugins).</summary>
public enum OfflineEffect
{
    SolidColor, Aurora, Breathing, RainbowLeft, RainbowRight, NeonLeft, NeonRight, SunsetLeft, SunsetRight,
    AudioParty, RainbowCycle, RainbowPinWheel, Fire,
}

/// <summary>How the cover (diffuser) image is fitted; numbering matches the wire format (Avalonia's Stretch).</summary>
public enum CoverStretch { None, Fill, Uniform, UniformToFill }

/// <summary>
/// Everything about how the RGB background looks. The lighting plugin sends these over UDP and they overwrite what is here,
/// exactly as RGB Wallpaper does; they can also be set by hand in Overlook's settings.
/// </summary>
public sealed class RgbSettings
{
    /// <summary>Listen for lighting data. Off: only the offline effect is shown.</summary>
    public bool Listen { get; set; } = true;
    /// <summary>UDP port the SignalRGB / OpenRGB / RGBJunkie plugin broadcasts to (RGB Wallpaper's config.json "Port").</summary>
    public int Port { get; set; } = 8133;

    public MatrixSizeType MatrixSizeType { get; set; } = MatrixSizeType.Landscape16_9;
    public MatrixSizeTier MatrixSizeTier { get; set; } = MatrixSizeTier.Normal;
    public OfflineEffect OfflineEffect { get; set; } = OfflineEffect.RainbowCycle;
    public ThemeColor ShutdownColor { get; set; } = new(255, 255, 0, 0);

    public LedShape LedShape { get; set; } = LedShape.Rectangle;
    public int RoundedRectangleCornerRadius { get; set; } = 4;
    public double LedPadding { get; set; }
    /// <summary>Gaussian blur radius that smooths the LEDs into a glow (0 = crisp LEDs).</summary>
    public int BlurIntensity { get; set; } = 20;
    /// <summary>Target frames per second; 0 = 60.</summary>
    public int Fps { get; set; } = 60;

    public ThemeColor BackgroundColor { get; set; } = ThemeColor.Black;
    /// <summary>Picture (path or http URL) drawn over the LEDs as a diffuser.</summary>
    public string? CoverImage { get; set; }
    public CoverStretch CoverImageStretch { get; set; } = CoverStretch.Uniform;

    public RgbSettings Clone() => (RgbSettings)MemberwiseClone();
}

public static class RgbMatrix
{
    // width, height of the landscape layouts for Small, Normal, Large, XLarge
    private static readonly Dictionary<string, (int W, int H)[]> Landscape = new()
    {
        ["4_1"] = [(8, 2), (16, 4), (32, 8), (64, 16)],
        ["4_3"] = [(8, 6), (16, 12), (32, 24), (64, 48)],
        ["5_4"] = [(10, 8), (20, 16), (40, 32), (80, 64)],
        ["16_9"] = [(32, 18), (48, 27), (64, 36), (128, 72)],
        ["16_10"] = [(32, 20), (48, 30), (64, 40), (128, 80)],
        ["21_9"] = [(42, 18), (63, 27), (84, 36), (168, 72)],
        ["32_9"] = [(64, 18), (96, 27), (128, 36), (256, 72)],
    };

    public static bool IsPortrait(MatrixSizeType type) =>
        type is MatrixSizeType.Portrait4_1 or MatrixSizeType.Portrait4_3 or MatrixSizeType.Portrait5_4 or MatrixSizeType.Portrait16_9
            or MatrixSizeType.Portrait16_10 or MatrixSizeType.Portrait21_9 or MatrixSizeType.Portrait32_9;

    /// <summary>LED grid size in columns and rows.</summary>
    public static (int Width, int Height) Size(MatrixSizeType type, MatrixSizeTier tier)
    {
        var key = type.ToString().Replace("Landscape", "").Replace("Portrait", "");
        var (w, h) = Landscape[key][(int)tier];
        if (!IsPortrait(type)) return (w, h);

        // The plugin's own table has 36x48 (not 36x84) for portrait 21:9 / Large; frames are sized to that, so keep it.
        if (type == MatrixSizeType.Portrait21_9 && tier == MatrixSizeTier.Large) return (36, 48);
        return (h, w);
    }
}
