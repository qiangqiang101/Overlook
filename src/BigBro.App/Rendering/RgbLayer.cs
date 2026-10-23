using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BigBro.Core.Audio;
using BigBro.Core.Rgb;
using BigBro.Core.Theming;

namespace BigBro.App.Rendering;

/// <summary>
/// The LED matrix, drawn from the live lighting stream (or the offline effect when nothing is sending). Port of RGB Wallpaper's
/// <c>LedCanvas</c>.
/// </summary>
public sealed class LedCanvas : Control
{
    private readonly RgbSource _source;
    private readonly RgbSettings _settings;
    private readonly SpectrumAnalyzer? _audio;
    private readonly RgbEffects _effects = new();
    private readonly Dictionary<ThemeColor, IBrush> _brushes = new();
    private readonly DispatcherTimer _timer;
    private ThemeColor[] _frame = [];

    public LedCanvas(RgbSource source, RgbSettings settings, SpectrumAnalyzer? audio)
    {
        _source = source;
        _settings = settings;
        _audio = audio;
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = Interval() };
        _timer.Tick += (_, _) => InvalidateVisual();
    }

    private TimeSpan Interval() => TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(_settings.Fps > 0 ? _settings.Fps : 60, 5, 120));

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    /// <summary>Re-reads the frame rate after the settings changed.</summary>
    public void SettingsChanged() => _timer.Interval = Interval();

    public override void Render(DrawingContext dc)
    {
        var (w, h) = RgbMatrix.Size(_settings.MatrixSizeType, _settings.MatrixSizeTier);
        if (w < 1 || h < 1 || Bounds.Width < 1 || Bounds.Height < 1) return;

        if (_frame.Length != w * h) _frame = new ThemeColor[w * h];

        if (_source.HasLiveFrames)
        {
            var live = _source.Frame;
            Array.Copy(live, _frame, Math.Min(live.Length, _frame.Length));
        }
        else
        {
            if (_settings.OfflineEffect == OfflineEffect.AudioParty) _audio?.Update();
            _effects.Render(_settings.OfflineEffect, _frame, w, h, _settings.ShutdownColor, _audio);
        }

        double cellW = Bounds.Width / w, cellH = Bounds.Height / h;
        var padding = _settings.LedPadding;
        var drawW = Math.Max(0, cellW - padding);
        var drawH = Math.Max(0, cellH - padding);

        for (var j = 0; j < h; j++)
        {
            for (var i = 0; i < w; i++)
            {
                var color = _frame[j * w + i];
                if (color.R == 0 && color.G == 0 && color.B == 0) continue;     // black LEDs show the background

                var brush = Brush(color);
                var rect = new Rect(cellW * i + padding / 2, cellH * j + padding / 2, drawW, drawH);
                switch (_settings.LedShape)
                {
                    case LedShape.RoundedRectangle:
                        dc.DrawRectangle(brush, null, new RoundedRect(rect, _settings.RoundedRectangleCornerRadius));
                        break;
                    case LedShape.Sphere:
                        dc.DrawEllipse(brush, null, rect.Center, drawW / 2, drawH / 2);
                        break;
                    default:
                        dc.DrawRectangle(brush, null, rect);
                        break;
                }
            }
        }
    }

    private IBrush Brush(ThemeColor c)
    {
        if (_brushes.TryGetValue(c, out var brush)) return brush;
        if (_brushes.Count > 4000) _brushes.Clear();
        return _brushes[c] = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)).ToImmutable();
    }
}

/// <summary>
/// The whole RGB background: background colour, the blurred LED matrix and the cover (diffuser) image on top.
/// Sits underneath the theme view, which draws the theme's background image over it as a mask.
/// </summary>
public sealed class RgbLayer : Grid, IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly RgbService _service;
    private readonly RgbSettings _settings;
    private readonly LedCanvas _leds;
    private readonly Image _cover = new() { IsHitTestVisible = false };
    private string? _loadedCover;
    private Bitmap? _coverBitmap;

    public RgbLayer(RgbService service, SpectrumAnalyzer? audio)
    {
        _service = service;
        _settings = service.Settings;
        IsHitTestVisible = false;
        _leds = new LedCanvas(service.Source, service.Settings, audio) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch };
        Children.Add(_leds);
        Children.Add(_cover);
        service.Source.SettingsChanged += OnSourceSettings;
        service.Edited += OnSourceSettings;
        Apply();
    }

    private void OnSourceSettings() => Dispatcher.UIThread.Post(Apply);

    /// <summary>Applies colour, blur, frame rate and cover image from the settings (call again after they change).</summary>
    public void Apply()
    {
        Background = new SolidColorBrush(Color.FromRgb(_settings.BackgroundColor.R, _settings.BackgroundColor.G, _settings.BackgroundColor.B));
        _leds.Effect = _settings.BlurIntensity > 0 ? new BlurEffect { Radius = _settings.BlurIntensity } : null;
        _leds.SettingsChanged();
        _cover.Stretch = _settings.CoverImageStretch switch
        {
            CoverStretch.None => Stretch.None,
            CoverStretch.Fill => Stretch.Fill,
            CoverStretch.UniformToFill => Stretch.UniformToFill,
            _ => Stretch.Uniform,
        };
        LoadCover(_settings.CoverImage);
        _leds.InvalidateVisual();
    }

    private void LoadCover(string? source)
    {
        if (source == _loadedCover) return;
        _loadedCover = source;
        if (string.IsNullOrWhiteSpace(source))
        {
            _cover.Source = null;
            return;
        }

        _ = Task.Run(async () =>
        {
            Bitmap? bitmap = null;
            try
            {
                var text = source.Trim().Trim('"');
                if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = await Http.GetByteArrayAsync(text).ConfigureAwait(false);
                    bitmap = new Bitmap(new MemoryStream(bytes));
                }
                else
                {
                    var path = Path.IsPathRooted(text) ? text : Path.Combine(AppContext.BaseDirectory, text);
                    if (File.Exists(path)) bitmap = new Bitmap(path);
                }
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or ArgumentException or NotSupportedException)
            {
                // an unreadable cover image is simply not shown
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (_loadedCover != source) { bitmap?.Dispose(); return; }
                _coverBitmap?.Dispose();
                _coverBitmap = bitmap;
                _cover.Source = bitmap;
            });
        });
    }

    public void Dispose()
    {
        _service.Source.SettingsChanged -= OnSourceSettings;
        _service.Edited -= OnSourceSettings;
        _cover.Source = null;
        _coverBitmap?.Dispose();
    }
}
