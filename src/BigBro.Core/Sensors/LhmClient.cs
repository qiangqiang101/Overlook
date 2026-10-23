namespace BigBro.Core.Sensors;

/// <summary>Fetches <c>data.json</c> from a LibreHardwareMonitor instance with "Remote Web Server" enabled.</summary>
public sealed class LhmClient : IDisposable
{
    public const string DefaultUrl = "http://192.168.0.8:8085/data.json";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public LhmClient(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    }

    /// <summary>
    /// Accepts what people actually type: "192.168.0.8:8085", "http://pc:8085" or the full URL, and returns a usable data.json URL.
    /// </summary>
    public static bool TryNormalizeUrl(string? input, out Uri url)
    {
        url = null!;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();
        if (!s.Contains("://", StringComparison.Ordinal)) s = "http://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            return false;
        if (u.AbsolutePath is "" or "/")
            u = new UriBuilder(u) { Path = "/data.json" }.Uri;
        url = u;
        return true;
    }

    public async Task<SensorSnapshot> FetchAsync(Uri url, CancellationToken ct = default)
    {
        await using var stream = await _http.GetStreamAsync(url, ct).ConfigureAwait(false);
        return LhmParser.Parse(stream, DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
