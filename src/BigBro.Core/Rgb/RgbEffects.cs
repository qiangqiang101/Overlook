using System.Numerics;
using BigBro.Core.Audio;
using BigBro.Core.Theming;

namespace BigBro.Core.Rgb;

/// <summary>
/// The effects shown when no lighting software is sending, ported from RGB Wallpaper. Each call fills <c>frame</c> (w * h LEDs).
/// Effects driven by the clock (rainbow cycle, pinwheel, fire, breathing) line up with the wallpaper when both run;
/// the others animate from when they started.
/// </summary>
public sealed class RgbEffects
{
    private double _animStep;
    private double _xShift;
    private double _colorShift;
    private int _effectIdx;
    private DateTime _lastTrigger = DateTime.MinValue;

    private static readonly ThemeColor[] Neon = [C(255, 0, 255), C(255, 0, 129), C(0, 117, 255), C(0, 255, 255)];
    private static readonly ThemeColor[] Sunset = [C(255, 80, 0), C(255, 0, 100), C(80, 0, 200), C(0, 0, 50)];
    private static readonly ThemeColor[] Rainbow =
        [C(255, 0, 0), C(255, 127, 0), C(255, 255, 0), C(0, 255, 0), C(0, 255, 255), C(0, 0, 255), C(255, 0, 255)];

    private static ThemeColor C(byte r, byte g, byte b) => new(255, r, g, b);

    public void Render(OfflineEffect effect, ThemeColor[] frame, int w, int h, ThemeColor shutdownColor, SpectrumAnalyzer? audio = null)
    {
        switch (effect)
        {
            case OfflineEffect.Aurora: Aurora(frame, w, h); break;
            case OfflineEffect.Breathing: Solid(frame, Breathe(shutdownColor)); break;
            case OfflineEffect.RainbowLeft: Sweep(frame, w, Rainbow, fromLeft: true); break;
            case OfflineEffect.RainbowRight: Sweep(frame, w, Rainbow, fromLeft: false); break;
            case OfflineEffect.NeonLeft: Sweep(frame, w, Neon, fromLeft: true); break;
            case OfflineEffect.NeonRight: Sweep(frame, w, Neon, fromLeft: false); break;
            case OfflineEffect.SunsetLeft: Sweep(frame, w, Sunset, fromLeft: true); break;
            case OfflineEffect.SunsetRight: Sweep(frame, w, Sunset, fromLeft: false); break;
            case OfflineEffect.AudioParty: AudioParty(frame, w, h, audio); break;
            case OfflineEffect.RainbowCycle: RainbowCycle(frame); break;
            case OfflineEffect.RainbowPinWheel: Pinwheel(frame, w, h); break;
            case OfflineEffect.Fire: Fire(frame, w, h); break;
            default: Solid(frame, shutdownColor); break;
        }
    }

    // ------------------------------------------------------------------ simple effects

    private static void Solid(ThemeColor[] frame, ThemeColor color) => Array.Fill(frame, color);

    private static ThemeColor Breathe(ThemeColor c)
    {
        var pulse = (Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds * 2.0) + 1.0) / 2.0;
        return new ThemeColor(255, (byte)(c.R * pulse), (byte)(c.G * pulse), (byte)(c.B * pulse));
    }

    private void Aurora(ThemeColor[] frame, int w, int h)
    {
        _animStep += 0.05;
        for (var i = 0; i < frame.Length; i++)
        {
            int x = i % w, y = i / w;
            var noiseH = Noise(x * 0.1, y * 0.1, _animStep);
            var noiseB = Noise(x * 0.05, y * 0.2, _animStep * 0.5);
            var hue = (220 + noiseH * 40) / 360.0;
            var intensity = Math.Clamp((noiseB + 1.0) / 2.0, 0.0, 1.0);
            frame[i] = FromHsl(hue, 0.8, intensity * 0.5);
        }
    }

    private static double Noise(double x, double y, double z) =>
        (Math.Sin(x * 0.5 + z) + Math.Cos(y * 0.3 - z) + Math.Sin((x + y) * 0.2 + z * 0.5)) / 3.0;

    private void Sweep(ThemeColor[] frame, int w, ThemeColor[] scheme, bool fromLeft)
    {
        _animStep += 0.01;
        for (var i = 0; i < frame.Length; i++)
        {
            var x = i % w;
            var progress = (fromLeft ? (double)x / w : 1.0 - (double)x / w) + _animStep;
            progress %= 1.0;
            if (progress < 0) progress += 1.0;
            frame[i] = Gradient(scheme, progress);
        }
    }

    private static void RainbowCycle(ThemeColor[] frame)
    {
        var offset = DateTime.Now.TimeOfDay.TotalSeconds * 60;
        var pos = (offset / 320.0) % 1.0;
        if (pos < 0) pos += 1.0;
        var colorPos = pos * 7;
        var i1 = (int)Math.Floor(colorPos) % 7;
        Array.Fill(frame, Lerp(Rainbow[i1], Rainbow[(i1 + 1) % 7], colorPos - Math.Floor(colorPos)));
    }

    private static void Pinwheel(ThemeColor[] frame, int w, int h)
    {
        double cx = w / 2.0, cy = h / 2.0;
        var offset = DateTime.Now.TimeOfDay.TotalSeconds * 60.0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var angle = Math.Atan2(y - cy, x - cx);
                if (angle < 0) angle += 2 * Math.PI;
                var pos = (angle / (2 * Math.PI) + offset / 360.0) % 1.0;
                var colorPos = pos * 7;
                var i1 = (int)Math.Floor(colorPos) % 7;
                frame[y * w + x] = Lerp(Rainbow[i1], Rainbow[(i1 + 1) % 7], colorPos - Math.Floor(colorPos));
            }
        }
    }

    /// <summary>Position t (0..1) on a colour ring (the last colour blends back into the first).</summary>
    internal static ThemeColor Gradient(ThemeColor[] colors, double t)
    {
        var n = colors.Length;
        var scaled = t * n;
        var i = Math.Min((int)scaled, n - 1);
        return Lerp(colors[i], colors[(i + 1) % n], scaled - i);
    }

    internal static ThemeColor Lerp(ThemeColor a, ThemeColor b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        byte Ch(byte x, byte y) => (byte)Math.Round(Math.Clamp(x + (y - x) * t, 0, 255));
        return new ThemeColor(255, Ch(a.R, b.R), Ch(a.G, b.G), Ch(a.B, b.B));
    }

    // ------------------------------------------------------------------ fire

    private static void Fire(ThemeColor[] frame, int w, int h)
    {
        ThemeColor color4 = C(64, 5, 0), color1 = C(255, 77, 0), color2 = C(255, 154, 0), color3 = C(255, 213, 128);
        double speed1 = 120, speed2 = 10, distance = 353, amplitude = 47;
        var itime = DateTime.Now.TimeOfDay.TotalSeconds * 2.0;
        var speed = new Vector2((float)(speed1 / 100.0), (float)(speed2 / 100.0));
        var distBase = distance / 100.0 - Math.Sin(itime * 0.4) / 1.89;
        var shift = 1.327 + Math.Sin(itime * 2.0) / 2.4;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var p = new Vector2((float)(x * distBase / w), (float)(y * distBase / w));
                p.X -= (float)(itime / 1.1);

                var q = Fbm(p - new Vector2((float)(itime * 0.01 + Math.Sin(itime) / 10.0), 0), amplitude);
                var qb = Fbm(p - new Vector2((float)(itime * 0.002 + Math.Cos(itime) / 5.0), 0), amplitude);
                var q2 = Fbm(p - new Vector2((float)(itime * 0.44 + 5.0 * Math.Cos(itime) / 7.0), 0), amplitude) - 6.0;
                var q3 = Fbm(p - new Vector2((float)(itime * 0.9 + 10.0 * Math.Cos(itime) / 30.0), 0), amplitude) - 4.0;
                var q4 = Fbm(p - new Vector2((float)(itime * 2.0 + 20.0 * Math.Sin(itime) / 20.0), 0), amplitude) + 2.0;
                var finalQ = (q + qb - 0.4 * q2 - 2.0 * q3 + 0.6 * q4) / 3.8;

                var r = new Vector2(
                    (float)Fbm(p + new Vector2((float)(finalQ / 2.0 + itime * speed.X - p.X - p.Y), 0), amplitude),
                    (float)Fbm(p + new Vector2((float)(finalQ - itime * speed.Y), 0), amplitude));
                var fbmPr = Fbm(p + r, amplitude);

                double Channel(byte c1, byte c2, byte c3x, byte c4x) =>
                    Mix(c1, c2, fbmPr) + Mix(c3x, c4x, r.X) - Mix(25, 230, r.Y);

                var intensity = Math.Cos(shift * y / h);
                frame[y * w + x] = new ThemeColor(255,
                    (byte)Math.Clamp(Channel(color1.R, color2.R, color3.R, color4.R) * intensity, 0, 255),
                    (byte)Math.Clamp(Channel(color1.G, color2.G, color3.G, color4.G) * intensity, 0, 255),
                    (byte)Math.Clamp(Channel(color1.B, color2.B, color3.B, color4.B) * intensity, 0, 255));
            }
        }
    }

    private static double Mix(double a, double b, double t) => a + (b - a) * t;

    private static double Rand(Vector2 n) =>
        Math.Sin(Math.Cos(Vector2.Dot(n, new Vector2(12.9898f, 12.1414f)))) * 83758.5453 % 1.0;

    private static double ValueNoise(Vector2 n)
    {
        var b = new Vector2(MathF.Floor(n.X), MathF.Floor(n.Y));
        var f = n - b;
        f = new Vector2(f.X * f.X * (3f - 2f * f.X), f.Y * f.Y * (3f - 2f * f.Y));
        return Mix(
            Mix(Rand(b), Rand(b + new Vector2(1f, 0f)), f.X),
            Mix(Rand(b + new Vector2(0f, 1f)), Rand(b + new Vector2(1f, 1f)), f.X),
            f.Y);
    }

    private static double Fbm(Vector2 n, double amplitudeSetting)
    {
        double total = 0, amp = 1;
        var p = n;
        for (var i = 0; i < 5; i++)
        {
            total += ValueNoise(p) * amp;
            p *= 1.7f;
            amp *= amplitudeSetting / 100.0;
        }
        return total;
    }

    // ------------------------------------------------------------------ audio party

    private void AudioParty(ThemeColor[] frame, int w, int h, SpectrumAnalyzer? audio)
    {
        const int divisions = 4;
        const double gain = 65, speed = 50, colorSpeed = 25, threshold = 20;

        var spectrum = Spectrum(audio?.RawBins(200) ?? new float[200], gain);

        float Peak(int from, int to)
        {
            float peak = 0;
            for (var i = from; i < to; i++) peak = Math.Max(peak, spectrum[i]);
            return peak;
        }

        _xShift += Peak(0, 50) * (speed / 1000.0);

        if (_animStep >= 1.0 && (DateTime.Now - _lastTrigger).TotalMilliseconds > 1500 && Peak(150, 200) * (gain / 10.0) > threshold / 100.0)
        {
            _effectIdx = Random.Shared.Next(0, 7);
            _animStep = 0;
            _lastTrigger = DateTime.Now;
        }

        _colorShift += Peak(50, 150) * (colorSpeed / 2000.0);
        if (_animStep < 1.0) _animStep += 0.01 * (speed / 15.0);

        for (var y = 0; y < h; y++)
        {
            var posY = (double)y / h;
            for (var x = 0; x < w; x++)
            {
                var s = 0.5 * (1.0 + Math.Sin((double)x / w * divisions * Math.PI + _xShift));
                var hue = (180.0 + Math.Sin(posY + _colorShift) * 180.0) % 360.0;
                var baseColor = FromHsv(hue, 1.0, 1.0);
                var overlay = Overlay(x, y, w, h);
                frame[y * w + x] = new ThemeColor(255,
                    Screen((byte)(baseColor.R * s), overlay.R),
                    Screen((byte)(baseColor.G * s), overlay.G),
                    Screen((byte)(baseColor.B * s), overlay.B));
            }
        }
    }

    private static float[] Spectrum(float[] raw, double gain)
    {
        var result = new float[200];
        float max = 1f, sum = 1f;
        var linear = Math.Pow(10, (3.0 + gain - 50.0) * 0.1);
        for (var i = 0; i < 200; i++)
        {
            sum += raw[i];
            if (raw[i] > max) max = raw[i];
        }
        var fac = 2f / (max + sum);
        for (var i = 0; i < 200; i++) result[i] = (float)(raw[i] * fac * linear);
        return result;
    }

    private static byte Screen(byte a, byte b) => (byte)Math.Min(255f, a + b - a * b / 255f);

    private ThemeColor Overlay(int x, int y, int w, int h)
    {
        var black = ThemeColor.Black;
        var white = ThemeColor.White;
        if (_animStep >= 1.0) return black;
        switch (_effectIdx)
        {
            case 0: return Math.Abs(x - _animStep * w) <= 1 ? white : black;
            case 1: return Math.Abs(x - (w - _animStep * w)) <= 1 ? white : black;
            case 2: return Math.Abs(y - _animStep * h) <= 1 ? white : black;
            case 3: return Math.Abs(y - (h - _animStep * h)) <= 1 ? white : black;
            case 4: return new ThemeColor(255, (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256));
            case 5:
            {
                var v = (byte)Random.Shared.Next(256);
                return new ThemeColor(255, v, v, v);
            }
            case 6:
            {
                var v = (byte)(255 * Math.Max(0.0, 1.0 - _animStep));
                return new ThemeColor(255, v, v, v);
            }
            default: return black;
        }
    }

    // ------------------------------------------------------------------ colour conversion

    internal static ThemeColor FromHsl(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0) r = g = b = l;
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3.0);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3.0);
        }
        return new ThemeColor(255, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    internal static ThemeColor FromHsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var c = v * s;
        var x = c * (1.0 - Math.Abs(h / 60.0 % 2.0 - 1.0));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new ThemeColor(255, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
