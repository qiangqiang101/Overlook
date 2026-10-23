namespace BigBro.Core.Sensors;

public enum ConnectionState
{
    Stopped,
    Connecting,
    Connected,
    /// <summary>The last poll failed. The previous snapshot is kept so the panel doesn't blank out on a hiccup.</summary>
    Error,
}

/// <summary>
/// Polls LibreHardwareMonitor in the background and keeps the newest snapshot, plus local and weather data.
/// Readers just take <see cref="CreateResolver"/>; they never wait on the network.
/// </summary>
public sealed class SensorHub : IDisposable
{
    private readonly LhmClient _client;
    private readonly WeatherService _weather;
    private readonly HttpClient _weatherHttp = new() { Timeout = TimeSpan.FromSeconds(8) };

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile SensorSnapshot _snapshot = SensorSnapshot.Empty;
    private int _intervalMs = 1000;
    private readonly Platform.AfterburnerReader _afterburner = new();
    private DateTime _lastWeather = DateTime.MinValue;

    public SensorHub(LhmClient? client = null)
    {
        _client = client ?? new LhmClient();
        _weather = new WeatherService(_weatherHttp);
    }

    public LocalInfo Local { get; } = new();

    public Uri? Url { get; private set; }

    /// <summary>True while the sensors come from this Linux computer instead of a remote LibreHardwareMonitor.</summary>
    public bool IsLocal { get; private set; }
    public ConnectionState State { get; private set; } = ConnectionState.Stopped;
    public string? LastError { get; private set; }
    public SensorSnapshot Snapshot => _snapshot;
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }

    /// <summary>Raised on the polling thread after every poll, successful or not.</summary>
    public event Action? Updated;

    public int IntervalMs
    {
        get => _intervalMs;
        set => _intervalMs = Math.Clamp(value, 200, 60_000);
    }

    public SensorResolver CreateResolver() => new(_snapshot, Local, _weather.Current);

    public void Start(Uri url, int intervalMs)
    {
        Stop();
        Url = url;
        IsLocal = false;
        Begin(intervalMs, ct => _client.FetchAsync(url, ct));
    }

    /// <summary>Polls this computer's own sensors (Linux: /proc and /sys, nvidia-smi) instead of a remote LibreHardwareMonitor.</summary>
    public void StartLocal(int intervalMs)
    {
        Stop();
        Url = null;
        IsLocal = true;
        var reader = new LinuxSensorReader();
        Begin(intervalMs, ct => Task.Run(reader.Read, ct));
    }

    /// <summary>One reading of this computer's sensors; used to fill the sensor lists while nothing is polling.</summary>
    public static SensorSnapshot ReadLocalOnce() => new LinuxSensorReader().Read();

    private void Begin(int intervalMs, Func<CancellationToken, Task<SensorSnapshot>> fetch)
    {
        IntervalMs = intervalMs;
        State = ConnectionState.Connecting;
        LastError = null;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(() => RunAsync(fetch, ct), ct);
    }

    public void Stop()
    {
        var cts = _cts;
        if (cts is null) return;
        _cts = null;
        cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        cts.Dispose();
        State = ConnectionState.Stopped;
    }

    /// <summary>One-off fetch, used by the "Test connection" button. Does not touch the running poll.</summary>
    public Task<SensorSnapshot> TestAsync(Uri url, CancellationToken ct = default) => _client.FetchAsync(url, ct);

    private async Task RunAsync(Func<CancellationToken, Task<SensorSnapshot>> fetch, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _snapshot = await fetch(ct).ConfigureAwait(false);
                State = ConnectionState.Connected;
                LastError = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                State = ConnectionState.Error;
                LastError = ex is TaskCanceledException ? "Timed out waiting for LibreHardwareMonitor." : ex.Message;
            }

            PollAfterburner();
            await RefreshWeatherIfDueAsync(ct).ConfigureAwait(false);

            try { Updated?.Invoke(); }
            catch { /* a faulty subscriber must not kill polling */ }

            try { await Task.Delay(_intervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void PollAfterburner()
    {
        var reading = _afterburner.Read();
        Local.FramesPerSecond = reading?.Fps ?? 0;
        Local.FrameTimeMs = reading?.FrameTimeMs ?? 0;
    }

    private async Task RefreshWeatherIfDueAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Latitude) || string.IsNullOrWhiteSpace(Longitude)) return;
        if (DateTime.UtcNow - _lastWeather < TimeSpan.FromMinutes(10)) return;
        _lastWeather = DateTime.UtcNow;
        try { await _weather.RefreshAsync(Latitude, Longitude, ct).ConfigureAwait(false); }
        catch (Exception) when (!ct.IsCancellationRequested) { _lastWeather = DateTime.UtcNow - TimeSpan.FromMinutes(9); }
    }

    public void Dispose()
    {
        Stop();
        _client.Dispose();
        _weatherHttp.Dispose();
        _afterburner.Dispose();
    }
}
