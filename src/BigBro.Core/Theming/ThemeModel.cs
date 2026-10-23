using System.Text.Json.Serialization;

namespace BigBro.Core.Theming;

/// <summary>
/// A theme (a.k.a. "sensor panel"). Serialized as <c>theme.json</c>; images are embedded in it as base64 data URIs.
/// <see cref="Elements"/> is ordered back-to-front: later entries are drawn on top, and
/// <see cref="ThemeElement.Children"/> are drawn above their parent and clipped to it.
/// </summary>
public sealed class ThemeDocument
{
    public int FormatVersion { get; set; } = ThemeJson.CurrentFormatVersion;
    public string Name { get; set; } = "Untitled Theme";
    public string? Author { get; set; }
    public string Version { get; set; } = "1.0";
    public List<string> Tags { get; set; } = [];

    public int Width { get; set; } = 480;
    public int Height { get; set; } = 320;

    public ThemeColor BackgroundColor { get; set; } = ThemeColor.White;
    /// <summary>Default text colour for elements that don't set their own.</summary>
    public ThemeColor TextColor { get; set; } = ThemeColor.Red;
    [JsonConverter(typeof(ImageStringConverter))]
    public string? BackgroundImage { get; set; }
    /// <summary>Thumbnail shown in the theme picker.</summary>
    [JsonConverter(typeof(ImageStringConverter))]
    public string? Preview { get; set; }

    /// <summary>Milliseconds between sensor refreshes.</summary>
    public int UpdateInterval { get; set; } = 3000;
    public double Opacity { get; set; } = 1.0;
    public RgbBackground? Rgb { get; set; }

    public List<ThemeElement> Elements { get; set; } = [];

    /// <summary>Set by the loader: the folder the theme was read from. Not serialized.</summary>
    [JsonIgnore]
    public string? Directory { get; set; }

    /// <summary>Elements whose binding still names a removed 1.x sensor and so need the user to choose a sensor.</summary>
    [JsonIgnore]
    public bool NeedsSensors => Flatten().Any(e => e.Sensor?.LegacyKind is not null);

    /// <summary>All elements, parents before their children.</summary>
    public IEnumerable<ThemeElement> Flatten()
    {
        var stack = new Stack<ThemeElement>(Enumerable.Reverse(Elements));
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            yield return e;
            foreach (var c in Enumerable.Reverse(e.Children)) stack.Push(c);
        }
    }
}

/// <summary>
/// Shows the live RGB lighting (from SignalRGB / OpenRGB / RGBJunkie, the same stream RGB Wallpaper uses) behind the theme.
/// How it looks (LED matrix, shape, blur, offline effect...) is set in Overlook's settings, not per theme.
/// The theme's background image is drawn over it as a mask.
/// </summary>
public sealed class RgbBackground
{
    public bool Enabled { get; set; } = true;
}

[JsonConverter(typeof(SensorBindingConverter))]
public sealed class SensorBinding
{
    public SensorKind Kind { get; set; }
    /// <summary>
    /// Meaning depends on <see cref="Kind"/>: GPU/disk index, CPU core number, fan number, drive path,
    /// ping host, date/time or uptime format string, or an LHM SensorId for <see cref="SensorKind.Lhm"/>.
    /// </summary>
    public string? Param { get; set; }

    /// <summary>
    /// Set when the theme file still names a removed 1.x sensor (e.g. <c>cpuTemperatureC</c>). The element is unbound until the user
    /// chooses a sensor path in the editor, which clears this.
    /// </summary>
    public string? LegacyKind { get; set; }
}

public sealed class ThemeFont
{
    public string Family { get; set; } = "Segoe UI";
    /// <summary>Size in points (1/72 in), as in 1.x. Rendered at 96 dpi, i.e. px = pt * 4/3.</summary>
    public double Size { get; set; } = 9;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strikeout { get; set; }

    [JsonIgnore]
    public double SizePx => Size * 96.0 / 72.0;
}

public readonly record struct Offset(int X, int Y);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(CustomTextElement), "customText")]
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(BarElement), "bar")]
[JsonDerivedType(typeof(CircularBarElement), "circularBar")]
[JsonDerivedType(typeof(ChartElement), "chart")]
[JsonDerivedType(typeof(DetailElement), "detail")]
[JsonDerivedType(typeof(YoutubeElement), "youtube")]
[JsonDerivedType(typeof(AudioElement), "audio")]
public abstract class ThemeElement
{
    /// <summary>Unique within the theme. Used by user overrides to find the element.</summary>
    [JsonPropertyOrder(-100)]
    public string Name { get; set; } = "";
    /// <summary>Position relative to the parent element (or the panel for top-level elements).</summary>
    [JsonPropertyOrder(-99)]
    public int X { get; set; }
    [JsonPropertyOrder(-98)]
    public int Y { get; set; }
    [JsonPropertyOrder(-97)]
    public int Width { get; set; } = 100;
    [JsonPropertyOrder(-96)]
    public int Height { get; set; } = 30;
    [JsonPropertyOrder(-95)]
    public bool Visible { get; set; } = true;
    /// <summary>Fills the element's rectangle behind its content.</summary>
    [JsonPropertyOrder(-94)]
    public ThemeColor BackColor { get; set; } = ThemeColor.Transparent;

    /// <summary>Where the element gets its data from. Ignored by elements that have no sensor.</summary>
    [JsonPropertyOrder(-93)]
    public SensorBinding? Sensor { get; set; }

    /// <summary>Drawn above this element and clipped to it; positions are relative to this element.</summary>
    [JsonPropertyOrder(1000)]
    public List<ThemeElement> Children { get; set; } = [];
}

public sealed class TextElement : ThemeElement
{
    public ThemeFont Font { get; set; } = new();
    public ThemeColor ForeColor { get; set; } = ThemeColor.Black;
    /// <summary>Static text, used when no sensor is bound.</summary>
    public string Text { get; set; } = "";
    /// <summary>Prefix put in front of the sensor value.</summary>
    public string? BeforeText { get; set; }
    public ContentAlign TextAlign { get; set; } = ContentAlign.MiddleCenter;
    /// <summary>Size the label to its text (X/Y stay the top-left corner).</summary>
    public bool AutoSize { get; set; }
    public bool AutoEllipsis { get; set; }
    public bool RightToLeft { get; set; }
    public BorderKind BorderStyle { get; set; }
    [JsonConverter(typeof(ImageStringConverter))]
    public string? Image { get; set; }
    public ContentAlign ImageAlign { get; set; } = ContentAlign.MiddleCenter;
    /// <summary>The user may replace the text from the main window.</summary>
    public bool AllowUserEdit { get; set; }
}

/// <summary>Title, value and unit laid out in three columns.</summary>
public sealed class CustomTextElement : ThemeElement
{

    public string Title { get; set; } = "";
    public string Value { get; set; } = "";
    public string Unit { get; set; } = "";

    public ThemeColor TitleColor { get; set; } = ThemeColor.Black;
    public ThemeColor ValueColor { get; set; } = ThemeColor.Black;
    public ThemeColor UnitColor { get; set; } = ThemeColor.Black;

    public ThemeFont TitleFont { get; set; } = new();
    /// <summary>Font of the value column.</summary>
    public ThemeFont ValueFont { get; set; } = new();
    public ThemeFont UnitFont { get; set; } = new();

    public ContentAlign TitleAlign { get; set; } = ContentAlign.MiddleLeft;
    public ContentAlign ValueAlign { get; set; } = ContentAlign.MiddleCenter;
    public ContentAlign UnitAlign { get; set; } = ContentAlign.MiddleRight;

    /// <summary>Split the width evenly between the non-empty columns.</summary>
    public bool AutoWidth { get; set; } = true;
    public double TitleWidth { get; set; }
    public double ValueWidth { get; set; }
    public double UnitWidth { get; set; }

    public Offset TitleOffset { get; set; }
    public Offset ValueOffset { get; set; }
    public Offset UnitOffset { get; set; }
}

public sealed class ImageElement : ThemeElement
{
    [JsonConverter(typeof(ImageStringConverter))]
    public string? BackgroundImage { get; set; }
    public ImageLayoutKind BackgroundImageLayout { get; set; } = ImageLayoutKind.Tile;
    [JsonConverter(typeof(ImageStringConverter))]
    public string? Image { get; set; }
    public ImageSizeMode SizeMode { get; set; } = ImageSizeMode.Normal;
    public BorderKind BorderStyle { get; set; }

    /// <summary>Swap <see cref="Image"/> for one of <see cref="DynamicImages"/> depending on the sensor value.</summary>
    public bool EnableDynamicImages { get; set; }
    public List<DynamicImage> DynamicImages { get; set; } = [];
    public bool AllowUserEdit { get; set; }
}

public sealed class DynamicImage
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    [JsonConverter(typeof(ImageStringConverter))]
    public string? Image { get; set; }
}

/// <summary>Straight progress bar.</summary>
public sealed class BarElement : ThemeElement
{
    public string Text { get; set; } = "";
    public ThemeFont Font { get; set; } = new();
    public ThemeColor ForeColor { get; set; } = ThemeColor.Black;
    /// <summary>Colour of the filled part. The unfilled track is the element's <see cref="ThemeElement.BackColor"/>.</summary>
    public ThemeColor ForegroundColor { get; set; } = new(255, 41, 128, 185);

    [JsonConverter(typeof(ImageStringConverter))]
    public string? Texture { get; set; }
    public int TextureWidth { get; set; }
    public int TextureHeight { get; set; }
    public bool UseTexture { get; set; }

    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public FillDirection FillDirection { get; set; }
    public bool ShowValue { get; set; }
    public string Unit { get; set; } = "";
}

public sealed class CircularBarElement : ThemeElement
{
    public string Text { get; set; } = "";
    public string SubText { get; set; } = "";
    public ThemeFont Font { get; set; } = new();
    public ThemeColor ForeColor { get; set; } = ThemeColor.Black;

    /// <summary>Colour of the unfilled track.</summary>
    public ThemeColor TrackColor { get; set; } = ThemeColor.Gray;
    public ThemeColor ProgressColor { get; set; } = ThemeColor.Black;
    public ThemeColor ProgressColor2 { get; set; } = ThemeColor.Black;
    public GradientDirection GradientMode { get; set; } = GradientDirection.Horizontal;

    public CircularStyle Style { get; set; }
    public ProgressShape ProgressShape { get; set; }
    /// <summary>Sweep angle in degrees for <see cref="CircularStyle.Style3"/>.</summary>
    public int BarWidth { get; set; } = 270;
    public int Thickness { get; set; } = 10;
    /// <summary>Degrees, clockwise from 3 o'clock.</summary>
    public int StartAngle { get; set; }

    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public string Unit { get; set; } = "";
    public CircularTextMode TextMode { get; set; } = CircularTextMode.Value;
}

public sealed class ChartPen
{
    public ThemeColor Color { get; set; } = ThemeColor.Black;
    public LineDash DashStyle { get; set; } = LineDash.Solid;
    public double Width { get; set; } = 1;
}

public sealed class ChartStyle
{
    public bool ShowVerticalGridLines { get; set; } = true;
    public bool ShowHorizontalGridLines { get; set; } = true;
    public bool ShowCurMax { get; set; }
    public bool ShowAverageLine { get; set; }
    public ChartPen VerticalGridPen { get; set; } = new() { Color = new ThemeColor(255, 40, 40, 40) };
    public ChartPen HorizontalGridPen { get; set; } = new() { Color = new ThemeColor(255, 40, 40, 40) };
    public ChartPen AvgLinePen { get; set; } = new() { Color = ThemeColor.Red };
    public ChartPen ChartLinePen { get; set; } = new() { Color = ThemeColor.Lime };
    public bool AntiAliasing { get; set; } = true;
    public ThemeColor BackgroundColorTop { get; set; } = ThemeColor.Black;
    public ThemeColor BackgroundColorBottom { get; set; } = ThemeColor.Black;
}

public sealed class ChartElement : ThemeElement
{
    public string Text { get; set; } = "";
    public ThemeFont Font { get; set; } = new();
    public ThemeColor ForeColor { get; set; } = ThemeColor.White;
    public ChartScaleMode ScaleMode { get; set; } = ChartScaleMode.Absolute;
    public ChartBorder BorderStyle { get; set; } = ChartBorder.None;
    public ChartStyle Style { get; set; } = new();
}

/// <summary>Lists every sensor of a group: names on the left, values on the right.</summary>
public sealed class DetailElement : ThemeElement
{
    public ThemeFont Font { get; set; } = new();
    public ThemeColor ForeColor { get; set; } = ThemeColor.White;
    public DetailGroup Group { get; set; }
    public bool DrawBorder { get; set; }
}

/// <summary>An embedded YouTube player (a native web view floating over the panel).</summary>
public sealed class YoutubeElement : ThemeElement
{
    public string? YoutubeId { get; set; }
    public bool Play { get; set; }
}

/// <summary>Spectrum bars driven by whatever the computer is playing.</summary>
public sealed class AudioElement : ThemeElement
{
    [JsonConverter(typeof(ImageStringConverter))]
    public string? BackgroundImage { get; set; }
    public bool UseAverage { get; set; }
    public int BarCount { get; set; }
    public int BarSpacing { get; set; }
    public string? ScalingStrategy { get; set; }
    public string? Direction { get; set; }
    public string? LineCap { get; set; }
    public string? BarStyle { get; set; }
    public string? ColorStyle { get; set; }
    public double Speed { get; set; }
    public List<ThemeColor> Colors { get; set; } = [];
}
