using System.Globalization;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BigBro.Core.Audio;
using BigBro.Core.Sensors;
using BigBro.Core.Settings;
using BigBro.Core.Theming;

namespace BigBro.App.Rendering;

/// <summary>
/// Draws one theme. Everything is laid out in device pixels, 1 theme pixel = 1 screen pixel, regardless of the
/// display scale factor, because themes are authored for fixed-size panels.
/// </summary>
public sealed partial class ThemeView : Control, IDisposable
{
    private readonly ThemeDocument _theme;
    private readonly ThemeAssets _assets;
    private readonly bool _ownsAssets;
    private readonly FontRegistry _fonts;
    private readonly SpectrumAnalyzer? _audio;
    private readonly List<Node> _roots = [];
    private readonly List<Node> _all = [];
    private readonly Dictionary<ThemeColor, IBrush> _brushes = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool _hasAudio;
    private readonly bool _mayAnimate;

    private long _lastTickMs;

    /// <param name="assets">Pass one to share decoded images between rebuilds (the editor does); otherwise the view owns its own.</param>
    /// <param name="audio">Source of spectrum data for audio visualizer elements; they stay flat without it.</param>
    public ThemeView(ThemeDocument theme, FontRegistry fonts, Func<string, ElementOverride?>? overrides = null,
        ThemeAssets? assets = null, SpectrumAnalyzer? audio = null)
    {
        _theme = theme;
        _fonts = fonts;
        _audio = audio;
        _ownsAssets = assets is null;
        _assets = assets ?? new ThemeAssets(theme);
        IsHitTestVisible = false;

        foreach (var element in theme.Elements)
            _roots.Add(BuildNode(element, overrides));

        _hasAudio = _all.Any(n => n.Element is AudioElement);
        _mayAnimate = ThemeAssets.MayAnimate(theme);
    }

    public ThemeDocument Document => _theme;

    /// <summary>True (default): one theme pixel is one device pixel whatever the display scale. The editor turns this off so zoom and pointer coordinates stay simple.</summary>
    public bool PixelPerfect { get; set; } = true;

    /// <summary>Set by the host when an <see cref="RgbLayer"/> sits beneath this view (the theme has an RGB background).</summary>
    public bool RgbLayerPresent { get; set; }

    /// <summary>Anti-alias the audio visualizer (Settings: high quality audio effect).</summary>
    public bool HighQualityAudio { get; set; } = true;

    /// <summary>True when the theme has something that moves on its own (RGB background, audio, animated GIF) and wants <see cref="Tick"/> called every 30-60 ms.</summary>
    public bool NeedsTick => _hasAudio || _mayAnimate || _assets.HasAnimation;

    /// <summary>The fastest rate <see cref="Tick"/> is useful at, in milliseconds.</summary>
    public int TickIntervalMs => _hasAudio || _mayAnimate ? 30 : 60;

    private Node BuildNode(ThemeElement element, Func<string, ElementOverride?>? overrides)
    {
        var node = new Node(element) { Override = overrides?.Invoke(element.Name) };
        if (element is ChartElement) node.Chart = new ChartState();
        _all.Add(node);
        foreach (var child in element.Children)
            node.Children.Add(BuildNode(child, overrides));
        return node;
    }

    private sealed class Node(ThemeElement element)
    {
        public ThemeElement Element { get; } = element;
        public List<Node> Children { get; } = [];
        public ElementOverride? Override { get; init; }
        public SensorReading Reading { get; set; } = SensorReading.None;
        /// <summary>Largest value seen; the scale of bars bound to sensors without a natural maximum.</summary>
        public double Peak { get; set; }
        public ChartState? Chart { get; set; }
        public (string Names, string Values) Detail { get; set; }
        public float[]? Levels { get; set; }
        public double Angle { get; set; }
    }

    // ------------------------------------------------------------------ data

    /// <summary>Pulls fresh values from <paramref name="resolver"/> and repaints. Call on the UI thread.</summary>
    public void Refresh(SensorResolver resolver)
    {
        foreach (var node in _all)
        {
            switch (node.Element)
            {
                case DetailElement d:
                    node.Detail = resolver.Detail(d.Group);
                    break;
                default:
                    var binding = EffectiveBinding(node);
                    node.Reading = binding is null ? SensorReading.None : resolver.Resolve(binding);
                    break;
            }

            if (node.Reading.Max is null)
                node.Peak = Math.Max(node.Peak, node.Reading.Number);

            if (node is { Chart: { } chart, Element: ChartElement c })
                chart.Add(c.ScaleMode == ChartScaleMode.Absolute ? Math.Clamp(node.Reading.Number, 0, 100) : Math.Max(node.Reading.Number, 0));
        }
        InvalidateVisual();
    }

    /// <summary>Advances every animation by the time since the last call and repaints.</summary>
    public void Tick()
    {
        var now = _clock.ElapsedMilliseconds;
        var dt = Math.Max(0, now - _lastTickMs);
        _lastTickMs = now;

        _assets.ClockMs = now;

        if (_hasAudio)
        {
            _audio?.Update();
            foreach (var node in _all.Where(n => n.Element is AudioElement a))
                node.Angle = (node.Angle + ((AudioElement)node.Element).Speed * dt / 30.0) % 360;
        }
        InvalidateVisual();
    }

    private static SensorBinding? EffectiveBinding(Node node)
    {
        var binding = node.Element.Sensor;
        if (binding is null || binding.Kind == SensorKind.None) return null;
        if (node.Override?.Sensor is { Kind: not SensorKind.None } custom) return custom;
        return node.Override?.Param is { Length: > 0 } p ? new SensorBinding { Kind = binding.Kind, Param = p } : binding;
    }

    // ------------------------------------------------------------------ rendering

    public override void Render(DrawingContext ctx)
    {
        var scale = PixelPerfect ? TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0 : 1.0;
        using var _ = ctx.PushTransform(Matrix.CreateScale(1 / scale, 1 / scale));

        var panel = new Rect(0, 0, _theme.Width, _theme.Height);
        using var __ = ctx.PushClip(panel);

        // With an RgbLayer underneath, the lighting is the background: nothing opaque may be drawn here.
        if (!RgbLayerPresent) ctx.DrawRectangle(Brush(_theme.BackgroundColor), null, panel);
        DrawBackdrop(ctx, panel);

        foreach (var node in _roots)
            DrawNode(ctx, node);
    }

    private void DrawBackdrop(DrawingContext ctx, Rect panel)
    {
        var background = _assets.Get(_theme.BackgroundImage);

        if (RgbLayerPresent)
        {
            // The theme's background image is a mask over the lighting, stretched to the panel.
            if (background is not null)
                ctx.DrawImage(background, new Rect(background.PixelSize.ToSize(1)), panel);
        }
        else if (background is not null)
        {
            DrawImageLayout(ctx, background, panel, ImageLayoutKind.Tile);
        }
    }

    private void DrawNode(DrawingContext ctx, Node node)
    {
        var el = node.Element;
        if (!el.Visible) return;

        var size = ContentSize(node);
        using var translate = ctx.PushTransform(Matrix.CreateTranslation(el.X, el.Y));
        var bounds = new Rect(0, 0, size.Width, size.Height);
        using var clip = ctx.PushClip(bounds);

        if (!el.BackColor.IsTransparent)
            ctx.DrawRectangle(Brush(el.BackColor), null, bounds);

        switch (el)
        {
            case TextElement t: DrawText(ctx, node, t, bounds); break;
            case CustomTextElement c: DrawCustomText(ctx, node, c, bounds); break;
            case ImageElement i: DrawImageElement(ctx, node, i, bounds); break;
            case BarElement b: DrawBar(ctx, node, b, bounds); break;
            case CircularBarElement cb: DrawCircularBar(ctx, node, cb, bounds); break;
            case ChartElement ch: DrawChart(ctx, node, ch, bounds); break;
            case DetailElement d: DrawDetail(ctx, node, d, bounds); break;
            case AudioElement au: DrawAudio(ctx, node, au, bounds); break;
            case YoutubeElement y: DrawYoutubePlaceholder(ctx, y, bounds); break;   // the player itself is a native web view, see MonitorWindow
        }

        foreach (var child in node.Children)
            DrawNode(ctx, child);
    }

    /// <summary>In the editor there is no web view, so mark where the video goes.</summary>
    private void DrawYoutubePlaceholder(DrawingContext ctx, YoutubeElement y, Rect bounds)
    {
        if (PixelPerfect) return;
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), new Pen(Brushes.Gray, 1, new DashStyle([4, 3], 0)), bounds);
        var font = new ThemeFont { Family = "Segoe UI", Size = 12 };
        var ft = Format($"YouTube {y.YoutubeId}", font, ThemeColor.White);
        ctx.DrawText(ft, new Point(bounds.Center.X - ft.Width / 2, bounds.Center.Y - ft.Height / 2));
    }

    /// <summary>Elements normally have the size the theme gives them; auto-sized labels shrink-wrap their text.</summary>
    private Size ContentSize(Node node)
    {
        var el = node.Element;
        if (el is TextElement { AutoSize: true } t)
        {
            var ft = Format(TextFor(node, t), t.Font, t.ForeColor);
            return new Size(Math.Ceiling(ft.WidthIncludingTrailingWhitespace), Math.Ceiling(ft.Height));
        }
        return new Size(el.Width, el.Height);
    }

    // ------------------------------------------------------------------ shared helpers

    private IBrush Brush(ThemeColor color)
    {
        if (_brushes.TryGetValue(color, out var brush)) return brush;
        brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B)).ToImmutable();
        _brushes[color] = brush;
        return brush;
    }

    private static Color ToColor(ThemeColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private FormattedText Format(string text, ThemeFont font, ThemeColor color,
        double maxWidth = double.PositiveInfinity, TextAlignment align = TextAlignment.Left,
        FlowDirection flow = FlowDirection.LeftToRight)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, flow, _fonts.TypefaceFor(font), font.SizePx, Brush(color))
        {
            TextAlignment = align,
        };
        if (!double.IsInfinity(maxWidth)) ft.MaxTextWidth = Math.Max(1, maxWidth);

        if (font.Underline || font.Strikeout)
        {
            var decorations = new TextDecorationCollection();
            if (font.Underline) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
            if (font.Strikeout) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
            ft.SetTextDecorations(decorations);
        }
        return ft;
    }

    private static (TextAlignment Horizontal, int Vertical) Split(ContentAlign a) => a switch
    {
        ContentAlign.TopLeft => (TextAlignment.Left, 0),
        ContentAlign.TopCenter => (TextAlignment.Center, 0),
        ContentAlign.TopRight => (TextAlignment.Right, 0),
        ContentAlign.MiddleLeft => (TextAlignment.Left, 1),
        ContentAlign.MiddleCenter => (TextAlignment.Center, 1),
        ContentAlign.MiddleRight => (TextAlignment.Right, 1),
        ContentAlign.BottomLeft => (TextAlignment.Left, 2),
        ContentAlign.BottomCenter => (TextAlignment.Center, 2),
        _ => (TextAlignment.Right, 2),
    };

    /// <summary>Draws <paramref name="text"/> inside <paramref name="rect"/> like a WinForms label with the given alignment.</summary>
    private void DrawAligned(DrawingContext ctx, string text, ThemeFont font, ThemeColor color, Rect rect,
        ContentAlign align, bool rightToLeft = false, bool ellipsis = false)
    {
        if (text.Length == 0 || rect.Width <= 0 || rect.Height <= 0) return;

        var (h, v) = Split(align);
        if (rightToLeft)
            h = h switch { TextAlignment.Left => TextAlignment.Right, TextAlignment.Right => TextAlignment.Left, _ => h };

        var ft = Format(text, font, color, rect.Width, h, rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
        if (ellipsis)
        {
            ft.Trimming = TextTrimming.CharacterEllipsis;
            ft.MaxTextHeight = Math.Max(1, rect.Height);
        }

        var y = v switch
        {
            0 => rect.Top,
            1 => rect.Top + (rect.Height - ft.Height) / 2,
            _ => rect.Bottom - ft.Height,
        };
        using var clip = ctx.PushClip(rect);
        ctx.DrawText(ft, new Point(rect.Left, y));
    }

    /// <summary>1.x border styles: a plain 1 px frame or a sunken 3-D edge.</summary>
    private void DrawBorder(DrawingContext ctx, Rect bounds, BorderKind kind)
    {
        switch (kind)
        {
            case BorderKind.FixedSingle:
                ctx.DrawRectangle(null, new Pen(Brushes.Black, 1), bounds.Deflate(0.5));
                break;
            case BorderKind.Fixed3D:
                DrawBevel(ctx, bounds, raised: false, thick: true);
                break;
        }
    }

    private static readonly IBrush BevelLight = new SolidColorBrush(Color.FromRgb(255, 255, 255)).ToImmutable();
    private static readonly IBrush BevelShadow = new SolidColorBrush(Color.FromRgb(160, 160, 160)).ToImmutable();
    private static readonly IBrush BevelDark = new SolidColorBrush(Color.FromRgb(105, 105, 105)).ToImmutable();

    private static void DrawBevel(DrawingContext ctx, Rect b, bool raised, bool thick)
    {
        var tl = raised ? BevelLight : BevelDark;
        var br = raised ? BevelDark : BevelLight;
        var inset = 0.5;
        for (var pass = 0; pass < (thick ? 2 : 1); pass++, inset += 1)
        {
            var r = b.Deflate(inset);
            var penTl = new Pen(pass == 0 ? tl : (raised ? BevelLight : BevelShadow), 1);
            var penBr = new Pen(pass == 0 ? br : (raised ? BevelShadow : BevelLight), 1);
            ctx.DrawLine(penTl, r.BottomLeft, r.TopLeft);
            ctx.DrawLine(penTl, r.TopLeft, r.TopRight);
            ctx.DrawLine(penBr, r.TopRight, r.BottomRight);
            ctx.DrawLine(penBr, r.BottomRight, r.BottomLeft);
        }
    }

    public void Dispose()
    {
        if (_ownsAssets) _assets.Dispose();
        _brushes.Clear();
    }
}
