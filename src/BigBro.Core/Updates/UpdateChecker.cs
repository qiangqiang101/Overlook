using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace BigBro.Core.Updates;

/// <summary>A release newer than the running version.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Url, string Notes);

/// <param name="Update">The newer release, or null when this is the latest one.</param>
/// <param name="Error">Why the check failed; null when it worked.</param>
public sealed record UpdateCheckResult(UpdateInfo? Update, string? Error)
{
    public bool Failed => Error is not null;
}

/// <summary>Asks GitHub whether a newer release than the running one has been published.</summary>
public static class UpdateChecker
{
    public const string Repository = "qiangqiang101/Overlook";

    /// <summary>The running version (declared before the client, which reads it while static fields are initialised in order) (without any "-dev.N" or build suffix).</summary>
    public static Version CurrentVersion { get; } =
        TryParseVersion(Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, out var v)
            ? v
            : new Version(0, 0, 0);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Overlook", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateCheckResult(null, null);       // no release yet
            if (!response.IsSuccessStatusCode) return new UpdateCheckResult(null, $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(json, CurrentVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new UpdateCheckResult(null, ex is HttpRequestException ? ex.Message : "GitHub did not answer in time.");
        }
    }

    /// <summary>Reads GitHub's "latest release" JSON. Public so tests can feed it samples.</summary>
    public static UpdateCheckResult Parse(string json, Version current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return new UpdateCheckResult(null, null);
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return new UpdateCheckResult(null, null);

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (!TryParseVersion(tag, out var latest)) return new UpdateCheckResult(null, $"Cannot read the version of the latest release ('{tag}').");
            if (latest <= current) return new UpdateCheckResult(null, null);

            var url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            return new UpdateCheckResult(new UpdateInfo(latest, tag!, url ?? $"https://github.com/{Repository}/releases/latest", notes ?? ""), null);
        }
        catch (JsonException ex)
        {
            return new UpdateCheckResult(null, $"Unexpected answer from GitHub: {ex.Message}");
        }
    }

    /// <summary>"v2.1.0", "2.1.0-dev.5" and "2.1.0+abc" all read as 2.1.0.</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var core = text.Trim().TrimStart('v', 'V');
        var cut = core.IndexOfAny(['-', '+']);
        if (cut >= 0) core = core[..cut];
        if (!Version.TryParse(core, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }
}
