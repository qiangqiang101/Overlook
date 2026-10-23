using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BigBro.App.Rendering;

/// <summary>
/// YouTube refuses to play an embed that has no referrer ("player configuration error", error 153). A web view that opens the
/// embed URL directly, or a page from <c>NavigateToString</c>, has none. So the iframe is hosted on a tiny loopback web server,
/// which gives it a real <c>http://127.0.0.1:port</c> origin.
/// </summary>
public sealed class YoutubeHost : IDisposable
{
    private static readonly Lazy<YoutubeHost> Shared = new(() => new YoutubeHost());
    public static YoutubeHost Instance => Shared.Value;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    private YoutubeHost()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Origin = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _ = Task.Run(AcceptLoop);
    }

    public string Origin { get; }

    /// <summary>The address the web view should open to play <paramref name="videoId"/>.</summary>
    public Uri PageFor(string videoId) => new($"{Origin}/{Uri.EscapeDataString(videoId)}");

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                _ = Task.Run(() => Serve(client));
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = client.SendTimeout = 5000;
                var stream = client.GetStream();
                var buffer = new byte[4096];
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                var request = Encoding.ASCII.GetString(buffer, 0, read);

                // "GET /<id> HTTP/1.1"; anything else gets an empty page.
                var path = request.Split(' ', 3) is [_, var p, ..] ? p.TrimStart('/').Split('?')[0] : "";
                var id = YoutubeEmbed.ExtractId(Uri.UnescapeDataString(path));
                var body = Encoding.UTF8.GetBytes(id is null ? "<!doctype html><body style=\"background:#000\">" : YoutubeEmbed.Html(id, Origin));
                var head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                    $"Content-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head).ConfigureAwait(false);
                await stream.WriteAsync(body).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}

public static class YoutubeEmbed
{
    /// <summary>The video id from a plain id or from any youtube.com / youtu.be link; null if there is none.</summary>
    public static string? ExtractId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Host.Contains("youtu", StringComparison.OrdinalIgnoreCase))
        {
            if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)) text = uri.AbsolutePath.Trim('/');
            else
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                text = query["v"] ?? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            }
        }
        return text.Length is >= 6 and <= 40 && text.All(c => char.IsLetterOrDigit(c) || c is '-' or '_') ? text : null;
    }

    public static string Html(string videoId, string origin)
    {
        var id = WebUtility.HtmlEncode(Uri.EscapeDataString(videoId));
        var src = $"https://www.youtube.com/embed/{id}?autoplay=1&mute=1&controls=0&rel=0&modestbranding=1&loop=1&playlist={id}&playsinline=1&origin={Uri.EscapeDataString(origin)}";
        return $$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <meta name="referrer" content="strict-origin-when-cross-origin">
            <style>html,body{margin:0;height:100%;background:#000;overflow:hidden}iframe{border:0;width:100%;height:100%}</style>
            </head>
            <body>
            <iframe src="{{src}}" allow="autoplay; encrypted-media; picture-in-picture" referrerpolicy="strict-origin-when-cross-origin" allowfullscreen></iframe>
            </body>
            </html>
            """;
    }
}
