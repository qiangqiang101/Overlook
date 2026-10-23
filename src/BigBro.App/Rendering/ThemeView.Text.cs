using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BigBro.Core.Theming;

namespace BigBro.App.Rendering;

public sealed partial class ThemeView
{
    // ------------------------------------------------------------------ text label

    private static string TextFor(Node node, TextElement t)
    {
        if (t.Sensor is { Kind: not SensorKind.None })
            return (t.BeforeText ?? "") + node.Reading.Text;
        return t.AllowUserEdit && node.Override?.Text is { } edited ? edited : t.Text;
    }

    private void DrawText(DrawingContext ctx, Node node, TextElement t, Rect bounds)
    {
        if (_assets.Get(t.Image) is { } image)
            DrawAlignedImage(ctx, image, bounds, t.ImageAlign);

        var align = t.AutoSize ? ContentAlign.TopLeft : t.TextAlign;
        DrawAligned(ctx, TextFor(node, t), t.Font, t.ForeColor, bounds, align, t.RightToLeft, t.AutoEllipsis);
        DrawBorder(ctx, bounds, t.BorderStyle);
    }

    // ------------------------------------------------------------------ title / value / unit

    private void DrawCustomText(DrawingContext ctx, Node node, CustomTextElement c, Rect bounds)
    {
        var bound = c.Sensor is { Kind: not SensorKind.None };
        var title = c.Title ?? "";
        var value = bound ? node.Reading.Value : c.Value ?? "";
        var unit = c.Unit ?? "";

        // 1.x split the width evenly between the non-empty parts but kept each part in its own column.
        var parts = (title.Length > 0 ? 1 : 0) + (value.Length > 0 ? 1 : 0) + (unit.Length > 0 ? 1 : 0);
        var even = parts == 0 ? bounds.Width : bounds.Width / parts;

        double titleW, valueW, unitW, valueX, unitX;
        if (c.AutoWidth)
        {
            titleW = valueW = unitW = even;
            valueX = even + c.TitleOffset.X + c.ValueOffset.X;
            unitX = even * 2 + c.TitleOffset.X + c.ValueOffset.X + c.UnitOffset.X;
        }
        else
        {
            (titleW, valueW, unitW) = (c.TitleWidth, c.ValueWidth, c.UnitWidth);
            valueX = c.TitleWidth + c.TitleOffset.X + c.ValueOffset.X;
            unitX = c.TitleWidth + c.ValueWidth + c.TitleOffset.X + c.ValueOffset.X + c.UnitOffset.X;
        }

        DrawSegment(ctx, title, c.TitleFont, c.TitleColor, c.TitleOffset.X, c.TitleOffset.Y, titleW, c.TitleAlign);
        DrawSegment(ctx, value, c.ValueFont, c.ValueColor, valueX, c.ValueOffset.Y, valueW, c.ValueAlign);
        DrawSegment(ctx, unit, c.UnitFont, c.UnitColor, unitX, c.UnitOffset.Y, unitW, c.UnitAlign);
    }

    /// <summary>One column of a custom text: horizontally aligned inside its width, top of the text at <paramref name="y"/>.</summary>
    private void DrawSegment(DrawingContext ctx, string text, ThemeFont font, ThemeColor color,
        double x, double y, double width, ContentAlign align)
    {
        if (text.Length == 0 || width <= 0) return;
        var ft = Format(text, font, color, width, Split(align).Horizontal);
        ctx.DrawText(ft, new Point(x, y));
    }

    // ------------------------------------------------------------------ detail list

    private void DrawDetail(DrawingContext ctx, Node node, DetailElement d, Rect bounds)
    {
        var (names, values) = node.Detail;
        if (string.IsNullOrEmpty(names)) return;

        var left = Format(names, d.Font, d.ForeColor);
        var right = Format(values, d.Font, d.ForeColor);

        // GDI's text measuring pads each side a little; keep the two columns apart the same way.
        var leftRect = new Rect(0, 0, Math.Ceiling(left.WidthIncludingTrailingWhitespace) + 6, Math.Ceiling(left.Height));
        var rightRect = new Rect(leftRect.Width, 0, Math.Ceiling(right.WidthIncludingTrailingWhitespace) + 7, Math.Ceiling(right.Height));

        ctx.DrawText(left, leftRect.TopLeft);
        ctx.DrawText(right, rightRect.TopLeft);

        if (d.DrawBorder)
        {
            var pen = new Pen(Brush(d.ForeColor), 1);
            ctx.DrawRectangle(null, pen, leftRect);
            ctx.DrawRectangle(null, pen, rightRect);
        }
    }

    // ------------------------------------------------------------------ images

    private void DrawImageElement(DrawingContext ctx, Node node, ImageElement i, Rect bounds)
    {
        if (_assets.Get(i.BackgroundImage) is { } background)
            DrawImageLayout(ctx, background, bounds, i.BackgroundImageLayout);

        if (PickImage(node, i) is { } image)
        {
            var size = PixelSize(image);
            var src = new Rect(image.Size);
            switch (i.SizeMode)
            {
                case ImageSizeMode.StretchImage:
                    ctx.DrawImage(image, src, bounds);
                    break;
                case ImageSizeMode.CenterImage:
                    ctx.DrawImage(image, src, new Rect(bounds.Center - new Point(size.Width / 2, size.Height / 2), size));
                    break;
                case ImageSizeMode.Zoom:
                    ctx.DrawImage(image, src, Fit(size, bounds));
                    break;
                default:
                    ctx.DrawImage(image, src, new Rect(bounds.TopLeft, size));
                    break;
            }
        }

        DrawBorder(ctx, bounds, i.BorderStyle);
    }

    /// <summary>The image to show now: the user's replacement, the dynamic image for the sensor's value, or the static image.</summary>
    private Bitmap? PickImage(Node node, ImageElement i)
    {
        if (i.AllowUserEdit && node.Override?.Image is { Length: > 0 } custom)
            return _assets.GetFile(custom);

        if (i is { EnableDynamicImages: true, DynamicImages.Count: > 0 })
        {
            var index = node.Reading.ImageIndex
                        ?? (int)Math.Round((i.DynamicImages.Count - 1) * (node.Reading.Fraction ?? 0));
            var match = i.DynamicImages.FirstOrDefault(d => d.Index == index);
            return match is null ? null : _assets.Get(match.Image);
        }

        return _assets.Get(i.Image);
    }

    private static Size PixelSize(IImage image) =>
        image is Bitmap b ? b.PixelSize.ToSize(1) : image.Size;

    private static Rect Fit(Size image, Rect bounds)
    {
        if (image.Width <= 0 || image.Height <= 0) return bounds;
        var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        var w = image.Width * scale;
        var h = image.Height * scale;
        return new Rect(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
    }

    private void DrawAlignedImage(DrawingContext ctx, Bitmap image, Rect bounds, ContentAlign align)
    {
        var size = PixelSize(image);
        var (h, v) = Split(align);
        var x = h switch
        {
            TextAlignment.Left => bounds.Left,
            TextAlignment.Right => bounds.Right - size.Width,
            _ => bounds.Left + (bounds.Width - size.Width) / 2,
        };
        var y = v switch
        {
            0 => bounds.Top,
            2 => bounds.Bottom - size.Height,
            _ => bounds.Top + (bounds.Height - size.Height) / 2,
        };
        ctx.DrawImage(image, new Rect(image.Size), new Rect(new Point(x, y), size));
    }

    /// <summary>Draws a background image the way WinForms' BackgroundImageLayout does.</summary>
    private static void DrawImageLayout(DrawingContext ctx, Bitmap image, Rect bounds, ImageLayoutKind layout)
    {
        var size = PixelSize(image);
        var src = new Rect(image.Size);
        switch (layout)
        {
            case ImageLayoutKind.Stretch:
                ctx.DrawImage(image, src, bounds);
                break;
            case ImageLayoutKind.Zoom:
                ctx.DrawImage(image, src, Fit(size, bounds));
                break;
            case ImageLayoutKind.Center:
                ctx.DrawImage(image, src, new Rect(bounds.Center - new Point(size.Width / 2, size.Height / 2), size));
                break;
            case ImageLayoutKind.None:
                ctx.DrawImage(image, src, new Rect(bounds.TopLeft, size));
                break;
            default:
                var brush = new ImageBrush(image)
                {
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.Fill,
                    DestinationRect = new RelativeRect(0, 0, size.Width, size.Height, RelativeUnit.Absolute),
                };
                ctx.DrawRectangle(brush, null, bounds);
                break;
        }
    }
}
