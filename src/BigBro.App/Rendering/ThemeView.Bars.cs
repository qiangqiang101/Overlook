using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BigBro.Core.Theming;

namespace BigBro.App.Rendering;

public sealed partial class ThemeView
{
    // ------------------------------------------------------------------ straight bar

    private void DrawBar(DrawingContext ctx, Node node, BarElement b, Rect bounds)
    {
        var bound = b.Sensor is { Kind: not SensorKind.None };
        var r = node.Reading;

        var min = bound ? r.Min : b.Minimum;
        var max = bound ? r.Max ?? Math.Max(Math.Max(b.Maximum, 1), node.Peak) : b.Maximum;
        var value = bound ? (int)Math.Round(r.Number) : 0;

        // Same arithmetic as 1.x: the scale never drops below 1.
        var scale = max < 1 ? 1 : max;
        var percent = scale - min == 0 ? 0 : Math.Clamp((value - min) / (scale - min), 0, 1);

        // Note: the 1.x direction names are swapped for the vertical modes; themes were made against that behaviour.
        var fill = bounds;
        var vertical = false;
        switch (b.FillDirection)
        {
            case FillDirection.LeftToRight:
                fill = new Rect(0, 0, bounds.Width * percent, bounds.Height);
                break;
            case FillDirection.RightToLeft:
                fill = new Rect(bounds.Width - bounds.Width * percent, 0, bounds.Width * percent, bounds.Height);
                break;
            case FillDirection.DownToUp:
                fill = new Rect(0, 0, bounds.Width, bounds.Height * percent);
                vertical = true;
                break;
            case FillDirection.UpToDown:
                fill = new Rect(0, bounds.Height - bounds.Height * percent, bounds.Width, bounds.Height * percent);
                vertical = true;
                break;
        }

        if (value != 0)
        {
            if (b.UseTexture && _assets.Get(b.Texture) is { } texture)
            {
                var size = PixelSize(texture);
                if (b.TextureWidth > 0 && b.TextureHeight > 0)
                    size = new Size(Math.Min(size.Width, b.TextureWidth), Math.Min(size.Height, b.TextureHeight));
                var brush = new ImageBrush(texture)
                {
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.Fill,
                    DestinationRect = new RelativeRect(0, 0, size.Width, size.Height, RelativeUnit.Absolute),
                };
                ctx.DrawRectangle(brush, null, fill);
            }
            else
            {
                ctx.DrawRectangle(Brush(b.ForegroundColor), null, fill);
            }
        }

        var text = b.ShowValue ? $"{b.Text}{value}{b.Unit}" : b.Text;
        if (text.Length == 0) return;

        var ft = Format(text, b.Font, b.ForeColor);
        if (vertical)
        {
            // GDI's DirectionVertical: text runs top-to-bottom, rotated a quarter turn clockwise.
            using var rotate = ctx.PushTransform(
                Matrix.CreateTranslation(-ft.Width / 2, -ft.Height / 2) *
                Matrix.CreateRotation(Math.PI / 2) *
                Matrix.CreateTranslation(bounds.Center.X, bounds.Center.Y));
            ctx.DrawText(ft, new Point(0, 0));
        }
        else
        {
            ctx.DrawText(ft, new Point(bounds.Center.X - ft.Width / 2, bounds.Center.Y - ft.Height / 2));
        }
    }

    // ------------------------------------------------------------------ circular bar

    private void DrawCircularBar(DrawingContext ctx, Node node, CircularBarElement c, Rect bounds)
    {
        var bound = c.Sensor is { Kind: not SensorKind.None };
        var r = node.Reading;
        var value = bound ? (int)Math.Round(r.Number) : 0;
        var max = bound ? r.Max ?? Math.Max(Math.Max(c.Maximum, 1), node.Peak) : c.Maximum;
        var scale = max < 1 ? 1 : max;

        // 1.x measured the sweep against the maximum alone, ignoring the minimum.
        var full = c.Style switch
        {
            CircularStyle.Style2 => 270.0,
            CircularStyle.Style3 => c.BarWidth,
            _ => 360.0,
        };
        var start = c.Style == CircularStyle.Style2 ? 135.0 : -c.StartAngle;
        var progress = full / scale * value;

        var rect = new Rect(c.Thickness, c.Thickness, bounds.Width - c.Thickness * 2, bounds.Height - c.Thickness * 2);
        if (rect.Width > 0 && rect.Height > 0)
        {
            // Avalonia has no triangle/arrow caps, so those shapes fall back to flat ends.
            var cap = c.ProgressShape is ProgressShape.Round or ProgressShape.RoundAnchor ? PenLineCap.Round : PenLineCap.Flat;

            var trackPen = new Pen(Brush(c.TrackColor), c.Thickness, lineCap: cap);
            var progressPen = new Pen(GradientBrush(c, bounds), c.Thickness, lineCap: cap);

            if (Arc(rect, start, full) is { } track) ctx.DrawGeometry(null, trackPen, track);
            if (Arc(rect, start, progress) is { } bar) ctx.DrawGeometry(null, progressPen, bar);
        }

        var main = c.TextMode switch
        {
            CircularTextMode.Value => $"{value}{c.Unit}",
            CircularTextMode.Text => c.Text,
            _ => "",
        };
        if (main.Length > 0)
        {
            var ft = Format(main, c.Font, c.ForeColor);
            ctx.DrawText(ft, new Point(rect.Center.X - ft.Width / 2, rect.Center.Y - ft.Height / 2));
        }

        if (c.Style != CircularStyle.Style1 && c.SubText.Length > 0)
        {
            var small = new ThemeFont
            {
                Family = c.Font.Family, Size = c.Font.Size / 2, Bold = c.Font.Bold, Italic = c.Font.Italic,
                Underline = c.Font.Underline, Strikeout = c.Font.Strikeout,
            };
            var ft = Format(c.SubText, small, c.ForeColor);
            ctx.DrawText(ft, new Point(rect.Center.X - ft.Width / 2, rect.Bottom - ft.Height));
        }
    }

    private static IBrush GradientBrush(CircularBarElement c, Rect bounds)
    {
        var (from, to) = c.GradientMode switch
        {
            GradientDirection.Vertical => (new Point(0, 0), new Point(0, bounds.Height)),
            GradientDirection.ForwardDiagonal => (new Point(0, 0), new Point(bounds.Width, bounds.Height)),
            GradientDirection.BackwardDiagonal => (new Point(bounds.Width, 0), new Point(0, bounds.Height)),
            _ => (new Point(0, 0), new Point(bounds.Width, 0)),
        };
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(from, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(to, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(ToColor(c.ProgressColor), 0),
                new GradientStop(ToColor(c.ProgressColor2), 1),
            },
        };
    }

    /// <summary>
    /// An elliptical arc like GDI+'s DrawArc: angles in degrees, measured clockwise from 3 o'clock (y points down).
    /// Split into pieces of at most 180° so full circles and large sweeps are unambiguous.
    /// </summary>
    private static StreamGeometry? Arc(Rect rect, double startDegrees, double sweepDegrees)
    {
        if (Math.Abs(sweepDegrees) < 0.01 || rect.Width <= 0 || rect.Height <= 0) return null;
        sweepDegrees = Math.Clamp(sweepDegrees, -360, 360);

        var rx = rect.Width / 2;
        var ry = rect.Height / 2;
        var center = rect.Center;
        Point At(double degrees)
        {
            var rad = degrees * Math.PI / 180;
            return new Point(center.X + rx * Math.Cos(rad), center.Y + ry * Math.Sin(rad));
        }

        var pieces = (int)Math.Ceiling(Math.Abs(sweepDegrees) / 180);
        var step = sweepDegrees / pieces;
        var direction = sweepDegrees > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(startDegrees), false);
            for (var i = 1; i <= pieces; i++)
                g.ArcTo(At(startDegrees + step * i), new Size(rx, ry), 0, false, direction);
            g.EndFigure(false);
        }
        return geometry;
    }

    // ------------------------------------------------------------------ scrolling line chart

    private const int ChartSamples = 512;
    private const int GridSpacing = 20;

    private sealed class ChartState
    {
        /// <summary>Newest first.</summary>
        public List<double> Values { get; } = new(ChartSamples);
        public int GridScroll { get; private set; }
        public double Current { get; private set; }

        public void Add(double value)
        {
            Values.Insert(0, value);
            if (Values.Count > ChartSamples) Values.RemoveAt(ChartSamples);
            GridScroll = (GridScroll + 1) % GridSpacing;
            Current = value;
        }
    }

    private void DrawChart(DrawingContext ctx, Node node, ChartElement c, Rect bounds)
    {
        var state = node.Chart!;
        var style = c.Style;
        var w = bounds.Width;
        var h = bounds.Height;

        // background gradient and grid
        ctx.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(0, h, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(ToColor(style.BackgroundColorTop), 0),
                new GradientStop(ToColor(style.BackgroundColorBottom), 1),
            },
        }, null, bounds);

        if (style.ShowVerticalGridLines)
        {
            var pen = MakePen(style.VerticalGridPen);
            for (var x = w - state.GridScroll; x >= 0; x -= GridSpacing)
                ctx.DrawLine(pen, new Point(x, 0), new Point(x, h));
        }
        if (style.ShowHorizontalGridLines)
        {
            var pen = MakePen(style.HorizontalGridPen);
            for (var y = 0.0; y < h; y += GridSpacing)
                ctx.DrawLine(pen, new Point(0, y), new Point(w, y));
        }

        // data: one pixel per sample, newest at the right edge
        var visible = Math.Min((int)w, state.Values.Count);
        var relative = c.ScaleMode == ChartScaleMode.Relative;
        var currentMax = relative ? state.Values.Take(visible).DefaultIfEmpty(0).Max() : 100;

        double Y(double value)
        {
            var scaled = relative ? (currentMax > 0 ? value * h / currentMax : 0) : value * h / 100;
            return h - Math.Round(scaled);
        }

        if (visible > 0)
        {
            if (style.ShowAverageLine)
            {
                var y = Y(state.Values.Take(visible).Average());
                ctx.DrawLine(MakePen(style.AvgLinePen), new Point(0, y), new Point(w, y));
            }

            var line = new StreamGeometry();
            var area = new StreamGeometry();
            using (var lg = line.Open())
            using (var ag = area.Open())
            {
                lg.BeginFigure(new Point(w + 1, h), false);
                ag.BeginFigure(new Point(w, h), true);
                for (var i = 0; i < visible; i++)
                {
                    var p = new Point(w - i, Y(state.Values[i]));
                    lg.LineTo(p);
                    ag.LineTo(p);
                }
                ag.LineTo(new Point(w - (visible - 1), h));
                lg.EndFigure(false);
                ag.EndFigure(true);
            }

            var lineColor = style.ChartLinePen.Color;
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(100, lineColor.R, lineColor.G, lineColor.B)), null, area);
            ctx.DrawGeometry(null, MakePen(style.ChartLinePen), line);
        }

        if (relative)
        {
            var unit = node.Reading.ChartUnit;
            var label = style.ShowCurMax
                ? $"{c.Text} ({Math.Round(state.Current)}{unit}/{Math.Round(currentMax)}{unit})"
                : c.Text;
            if (label.Length > 0)
                ctx.DrawText(Format(label, c.Font, c.ForeColor), new Point(4, 2));
        }

        DrawChartBorder(ctx, bounds, c.BorderStyle);
    }

    private Pen MakePen(ChartPen p)
    {
        IDashStyle? dash = p.DashStyle switch
        {
            LineDash.Dash => new DashStyle([3, 1], 0),
            LineDash.Dot => new DashStyle([1, 1], 0),
            LineDash.DashDot => new DashStyle([3, 1, 1, 1], 0),
            LineDash.DashDotDot => new DashStyle([3, 1, 1, 1, 1, 1], 0),
            _ => null,
        };
        return new Pen(Brush(p.Color), Math.Max(p.Width, 0.5), dash);
    }

    private static void DrawChartBorder(DrawingContext ctx, Rect bounds, ChartBorder border)
    {
        switch (border)
        {
            case ChartBorder.Raised or ChartBorder.RaisedInner or ChartBorder.RaisedOuter or ChartBorder.Bump:
                DrawBevel(ctx, bounds, raised: true, thick: border is ChartBorder.Raised or ChartBorder.Bump);
                break;
            case ChartBorder.Sunken or ChartBorder.SunkenInner or ChartBorder.SunkenOuter or ChartBorder.Etched:
                DrawBevel(ctx, bounds, raised: false, thick: border is ChartBorder.Sunken or ChartBorder.Etched);
                break;
            case ChartBorder.Flat:
                ctx.DrawRectangle(null, new Pen(BevelDark, 1), bounds.Deflate(0.5));
                break;
            // None and Adjust draw nothing (Adjust is only a layout hint in WinForms).
        }
    }
}
