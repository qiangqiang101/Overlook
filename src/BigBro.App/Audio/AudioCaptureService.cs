using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BigBro.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BigBro.App.Audio;

/// <summary>
/// Captures what the computer is playing (not the microphone) and feeds it to a <see cref="SpectrumAnalyzer"/>.
/// Windows: WASAPI loopback. Linux: <c>parec</c> on the default sink's monitor (PulseAudio / PipeWire-pulse).
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private readonly object _gate = new();
    private WasapiLoopbackCapture? _wasapi;
    private Process? _parec;
    private CancellationTokenSource? _cts;

    public SpectrumAnalyzer Analyzer { get; } = new();
    public bool IsRunning { get; private set; }
    /// <summary>Why capture isn't working (e.g. parec missing), or null.</summary>
    public string? Error { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning) return;
            Error = null;
            try
            {
                if (OperatingSystem.IsWindows()) StartWindows();
                else if (OperatingSystem.IsLinux()) StartLinux();
                else Error = "Audio capture isn't supported on this operating system.";
                IsRunning = Error is null;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                StopCore();
            }
        }
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    private void StopCore()
    {
        IsRunning = false;
        _cts?.Cancel();
        _cts = null;
        try { _wasapi?.StopRecording(); } catch (Exception) { /* device already gone */ }
        _wasapi?.Dispose();
        _wasapi = null;
        try { if (_parec is { HasExited: false }) _parec.Kill(); } catch (Exception) { /* already exited */ }
        _parec?.Dispose();
        _parec = null;
    }

    // ------------------------------------------------------------------ Windows

    [SupportedOSPlatform("windows")]
    private void StartWindows()
    {
        var capture = new WasapiLoopbackCapture();
        var format = capture.WaveFormat;
        Analyzer.SampleRate = format.SampleRate;

        capture.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded <= 0) return;
            if (format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible ext && ext.SubFormat == IeeeFloatSubFormat))
            {
                Analyzer.AddSamples(MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded - e.BytesRecorded % 4)), format.Channels);
            }
            else if (format.BitsPerSample == 16)
            {
                var pcm = MemoryMarshal.Cast<byte, short>(e.Buffer.AsSpan(0, e.BytesRecorded - e.BytesRecorded % 2));
                var floats = new float[pcm.Length];
                for (var i = 0; i < pcm.Length; i++) floats[i] = pcm[i] / 32768f;
                Analyzer.AddSamples(floats, format.Channels);
            }
        };
        capture.RecordingStopped += (_, _) => { };
        capture.StartRecording();
        _wasapi = capture;
    }

    // ------------------------------------------------------------------ Linux

    private void StartLinux()
    {
        const int rate = 48_000, channels = 2;
        var psi = new ProcessStartInfo("parec")
        {
            ArgumentList = { "-d", "@DEFAULT_MONITOR@", "--format=float32le", $"--rate={rate}", $"--channels={channels}", "--latency-msec=30" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("parec did not start.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Error = "The audio visualizer needs 'parec' (package pulseaudio-utils, or pipewire-pulse) to capture system audio.";
            return;
        }

        Analyzer.SampleRate = rate;
        _parec = process;
        var cts = _cts = new CancellationTokenSource();
        var stream = process.StandardOutput.BaseStream;

        _ = Task.Run(async () =>
        {
            var bytes = new byte[4096 * 4];
            var leftover = 0;
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(leftover), cts.Token).ConfigureAwait(false);
                    if (read <= 0) break;
                    var total = leftover + read;
                    var usable = total - total % (4 * channels);          // whole float frames only
                    Analyzer.AddSamples(MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, usable)), channels);
                    leftover = total - usable;
                    if (leftover > 0) Buffer.BlockCopy(bytes, usable, bytes, 0, leftover);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
        }, cts.Token);
    }

    public void Dispose() => Stop();
}
