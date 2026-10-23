namespace BigBro.Core.Audio;

/// <summary>How an FFT magnitude is mapped to a 0..1 bar height (same three choices as 1.x).</summary>
public enum ScalingStrategy { Decibel, Linear, Sqrt }

/// <summary>
/// Turns a stream of audio samples into bar heights: a Hann-windowed FFT over the newest samples, grouped into
/// logarithmically spaced bands between 20 Hz and 20 kHz. Thread-safe: capture threads call <see cref="AddSamples"/>,
/// the UI thread calls <see cref="Update"/> and <see cref="Bands"/>.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int FftSize = 8192;
    private const double MinFrequency = 20, MaxFrequency = 20_000;

    private readonly float[] _ring = new float[FftSize];
    private readonly object _lock = new();
    private int _write;
    private long _total;

    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];
    private readonly double[] _window = BuildWindow();
    private readonly float[] _magnitude = new float[FftSize / 2];

    public int SampleRate { get; set; } = 48_000;

    /// <summary>Add interleaved samples (-1..1); channels are averaged to mono.</summary>
    public void AddSamples(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels < 1) return;
        lock (_lock)
        {
            for (var i = 0; i + channels <= interleaved.Length; i += channels)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++) sum += interleaved[i + c];
                _ring[_write] = sum / channels;
                _write = (_write + 1) % FftSize;
                _total++;
            }
        }
    }

    /// <summary>True once enough audio has arrived to fill one FFT window.</summary>
    public bool HasData
    {
        get { lock (_lock) return _total >= FftSize; }
    }

    /// <summary>Recomputes the magnitude spectrum from the newest samples. Call once per frame before <see cref="Bands"/>.</summary>
    public void Update()
    {
        lock (_lock)
        {
            if (_total < FftSize)
            {
                Array.Clear(_magnitude);
                return;
            }
            for (var i = 0; i < FftSize; i++)
                _re[i] = _ring[(_write + i) % FftSize] * _window[i];
        }

        Array.Clear(_im);
        Fft(_re, _im);

        // |X| * 2 / sum(window) gives the amplitude of a sine at that bin (1.0 = full scale).
        const double norm = 2.0 / (FftSize * 0.5);
        for (var i = 0; i < _magnitude.Length; i++)
            _magnitude[i] = (float)(Math.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) * norm);
    }

    /// <summary>Bar heights (0..1) for <paramref name="barCount"/> log-spaced bands of the last <see cref="Update"/>.</summary>
    public float[] Bands(int barCount, bool useAverage, ScalingStrategy scaling)
    {
        barCount = Math.Clamp(barCount, 1, 1024);
        var result = new float[barCount];
        var binHz = (double)SampleRate / FftSize;
        var ratio = MaxFrequency / MinFrequency;

        for (var b = 0; b < barCount; b++)
        {
            var f0 = MinFrequency * Math.Pow(ratio, (double)b / barCount);
            var f1 = MinFrequency * Math.Pow(ratio, (double)(b + 1) / barCount);
            var first = Math.Clamp((int)Math.Floor(f0 / binHz), 1, _magnitude.Length - 1);
            var last = Math.Clamp((int)Math.Ceiling(f1 / binHz), first, _magnitude.Length - 1);

            double peak = 0, sum = 0;
            for (var i = first; i <= last; i++)
            {
                peak = Math.Max(peak, _magnitude[i]);
                sum += _magnitude[i];
            }
            var value = useAverage ? sum / (last - first + 1) : peak;
            result[b] = (float)Scale(value, scaling);
        }
        return result;
    }

    private float[] _rawSmooth = new float[0];

    /// <summary>
    /// The spectrum split into <paramref name="count"/> equal slices of frequency (not logarithmic), each the strongest bin of its slice,
    /// with a slow decay and scaled like an unnormalised 1024-point FFT. This is what RGB Wallpaper's audio effect expects.
    /// </summary>
    public float[] RawBins(int count)
    {
        if (_rawSmooth.Length != count) _rawSmooth = new float[count];
        var half = _magnitude.Length;
        var perBin = half / (double)count;
        for (var j = 0; j < count; j++)
        {
            var start = (int)(j * perBin);
            var end = Math.Min((int)((j + 1) * perBin), half - 1);
            float peak = 0;
            for (var k = start; k <= end; k++) peak = Math.Max(peak, _magnitude[k]);
            _rawSmooth[j] = Math.Max(peak * 256f, _rawSmooth[j] * 0.8f);
        }
        return _rawSmooth;
    }

    public static double Scale(double magnitude, ScalingStrategy scaling)
    {
        double v = scaling switch
        {
            // -90 dBFS..0 dBFS, like the 1.x Decibel mode
            ScalingStrategy.Decibel => magnitude <= 1e-9 ? 0 : (20 * Math.Log10(magnitude) + 90) / 90,
            ScalingStrategy.Sqrt => Math.Sqrt(magnitude) * 2,
            _ => magnitude * 20,
        };
        return Math.Clamp(v, 0, 1);
    }

    private static double[] BuildWindow()
    {
        var w = new double[FftSize];
        for (var i = 0; i < FftSize; i++) w[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        return w;
    }

    /// <summary>In-place radix-2 FFT.</summary>
    internal static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            double wr = Math.Cos(angle), wi = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var a = i + k;
                    var b = a + len / 2;
                    var tr = re[b] * cr - im[b] * ci;
                    var ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
    }
}
