using Avalonia;
using Avalonia.Media;
using BigBro.Core.Audio;
using BigBro.Core.Theming;

namespace BigBro.App.Rendering;

public sealed partial class ThemeView
{
    private void DrawAudio(DrawingContext ctx, Node node, AudioElement a, Rect bounds)
    {
        if (_assets.Get(a.BackgroundImage) is { } background)
            DrawImageLayout(ctx, background, bounds, ImageLayoutKind.Stretch);

        var count = Math.Clamp(a.BarCount, 1, 512);
        var targets = _audio?.Bands(count, a.UseAverage, ParseEnum(a.ScalingStrategy, ScalingStrategy.Decibel))
                      ?? new float[count];

        // Let bars fall back smoothly instead of flickering with every FFT frame.
        if (node.Levels is null || node.Levels.Length != count) node.Levels = new float[count];
        var levels = node.Levels;
        for (var i = 0; i < count; i++)
            levels[i] = Math.Max(targets[i], levels[i] - 0.05f);

        var spacing = Math.Max(0, a.BarSpacing);
        var barWidth = Math.Max((bounds.Width - spacing * (count + 1)) / count, 0.00001);
        var h = bounds.Height;

        var colors = a.Colors.Count >= 7 ? a.Colors : DefaultColors;
        var brush = AudioBrush(a, colors, bounds, node.Angle);
        var cap = ParseEnum(a.LineCap, "Round") switch
        {
            "Flat" => PenLineCap.Flat,
            "Square" => PenLineCap.Square,
            "Round" or "RoundAnchor" => PenLineCap.Round,
            _ => PenLineCap.Flat,
        };
        var pen = new Pen(brush, barWidth, lineCap: cap);
        var direction = ParseEnum(a.Direction, "Bottom");
        var line = ParseEnum(a.BarStyle, "Bar") == "Line";

        using var quality = ctx.PushRenderOptions(new RenderOptions
        {
            EdgeMode = HighQualityAudio ? EdgeMode.Antialias : EdgeMode.Aliased,
        });

        Point? previous = null, previousMirror = null;
        for (var i = 0; i < count; i++)
        {
            var x = spacing * (i + 1) + barWidth * i + barWidth / 2;
            var value = levels[i] * h;

            switch (direction)
            {
                case "Top":
                {
                    var tip = new Point(x, Math.Max(value - 1, 0));
                    if (line) { ctx.DrawLine(pen, previous ?? new Point(0, 0), tip); previous = tip; }
                    else ctx.DrawLine(pen, new Point(x, 0), tip);
                    break;
                }
                case "Center":
                {
                    // full height is shared between the two halves
                    var up = new Point(x, h / 2 - value / 2);
                    var down = new Point(x, h / 2 + value / 2);
                    if (line)
                    {
                        ctx.DrawLine(pen, previous ?? new Point(0, h / 2), up);
                        ctx.DrawLine(pen, previousMirror ?? new Point(0, h / 2), down);
                        previous = up;
                        previousMirror = down;
                    }
                    else
                    {
                        ctx.DrawLine(pen, new Point(x, h / 2), up);
                        ctx.DrawLine(pen, new Point(x, h / 2), down);
                    }
                    break;
                }
                default:
                {
                    var tip = new Point(x, h - value - 1);
                    if (line) { ctx.DrawLine(pen, previous ?? new Point(0, h), tip); previous = tip; }
                    else ctx.DrawLine(pen, new Point(x, h), tip);
                    break;
                }
            }
        }
    }

    private static readonly List<ThemeColor> DefaultColors =
    [
        new(255, 128, 0, 128), new(255, 255, 0, 0), new(255, 255, 255, 0), new(255, 0, 255, 0),
        new(255, 0, 255, 255), new(255, 0, 0, 255), new(255, 128, 0, 128),
    ];

    private IBrush AudioBrush(AudioElement a, List<ThemeColor> colors, Rect bounds, double angle)
    {
        switch (ParseEnum(a.ColorStyle, "Rainbow"))
        {
            case "Solid":
                return Brush(colors[0]);
            case "Gradient":
                return new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(0, bounds.Height, RelativeUnit.Absolute),
                    GradientStops = { new GradientStop(ToColor(colors[1]), 0), new GradientStop(ToColor(colors[0]), 1) },
                };
            default:
            {
                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(bounds.Width, bounds.Height, RelativeUnit.Absolute),
                    SpreadMethod = GradientSpreadMethod.Repeat,
                    Transform = new RotateTransform(angle),
                };
                double[] positions = [0, 0.1666, 0.3333, 0.5, 0.6666, 0.8333, 1];
                for (var i = 0; i < 7; i++) brush.GradientStops.Add(new GradientStop(ToColor(colors[i]), positions[i]));
                return brush;
            }
        }
    }

    private static string ParseEnum(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
