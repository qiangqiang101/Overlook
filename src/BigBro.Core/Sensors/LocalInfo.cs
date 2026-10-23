using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using BigBro.Core.Platform;

namespace BigBro.Core.Sensors;

/// <summary>
/// Values LibreHardwareMonitor can't provide, read on the machine the panel runs on:
/// ping, uptime, drive sizes and screen resolution.
/// </summary>
public sealed class LocalInfo
{
    private readonly ConcurrentDictionary<string, PingState> _pings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set by the UI layer; reports e.g. "1920x480".</summary>
    public Func<string> ScreenResolution { get; set; } = () => "";

    /// <summary>Latest FPS/frame time from MSI Afterburner (Windows, same PC); 0 when unavailable.</summary>
    public double FramesPerSecond { get; set; }
    public double FrameTimeMs { get; set; }

    private double _refresh;
    private long _refreshAt = -1;

    /// <summary>Refresh rate of this computer's display in Hz, cached for 10 s; 0 when unknown.</summary>
    public double RefreshRate
    {
        get
        {
            var now = Environment.TickCount64;
            if (_refreshAt < 0 || now - _refreshAt > 10_000)
            {
                _refreshAt = now;
                _refresh = DisplayInfo.RefreshRate();
            }
            return _refresh;
        }
    }

    public TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private sealed class PingState
    {
        public long Ms = -1;
        public long LastStartTicks;
        public int Running;
    }

    /// <summary>Last known round-trip time to <paramref name="host"/> in ms, or -1 if it failed / hasn't completed yet. Never blocks.</summary>
    public long Ping(string host)
    {
        var state = _pings.GetOrAdd(host, _ => new PingState());
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref state.LastStartTicks) > 2000 && Interlocked.CompareExchange(ref state.Running, 1, 0) == 0)
        {
            Interlocked.Exchange(ref state.LastStartTicks, now);
            _ = Task.Run(async () =>
            {
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(host, 1000).ConfigureAwait(false);
                    Interlocked.Exchange(ref state.Ms, reply.Status == IPStatus.Success ? reply.RoundtripTime : -1);
                }
                catch
                {
                    Interlocked.Exchange(ref state.Ms, -1);
                }
                finally
                {
                    Interlocked.Exchange(ref state.Running, 0);
                }
            });
        }
        return Interlocked.Read(ref state.Ms);
    }

    /// <summary>
    /// Finds a drive by the path a theme asks for ("C:\" on Windows, "/" or "/home" on Linux). If that drive doesn't exist
    /// here (a Windows theme on Linux, say) the first fixed drive is used.
    /// </summary>
    public DriveInfo? Drive(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var wanted = path.Trim();
                var match = DriveInfo.GetDrives().FirstOrDefault(d =>
                    d.IsReady && string.Equals(d.Name, wanted, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match;
            }
            return DriveInfo.GetDrives().FirstOrDefault(d => d.DriveType == DriveType.Fixed && d.IsReady);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
