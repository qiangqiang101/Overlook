using System.Net;
using System.Net.Sockets;
using System.Text;
using BigBro.Core.Theming;

namespace BigBro.Core.Rgb;

/// <summary>
/// Receives lighting from the SignalRGB / OpenRGB / RGBJunkie plugins over UDP, the same protocol RGB Wallpaper uses:
/// packet type 0 carries LED colours (split over several packets), type 1 carries display settings.
/// The socket is opened with address reuse so Overlook and RGB Wallpaper can listen on the same port at once and
/// show the same lighting (this works for broadcast, which is what the plugins send).
/// </summary>
public sealed class RgbSource : IDisposable
{
    private readonly List<byte> _pending = [];
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private volatile ThemeColor[] _frame = [];
    private long _lastPacketTicks;

    public RgbSource(RgbSettings settings) => Settings = settings;

    /// <summary>Updated in place when the plugin sends settings.</summary>
    public RgbSettings Settings { get; }

    /// <summary>Raised (on the receive thread) after a settings packet changed <see cref="Settings"/>.</summary>
    public event Action? SettingsChanged;

    public bool IsListening { get; private set; }
    public string? Error { get; private set; }

    /// <summary>When the last lighting frame arrived (UTC); <see cref="DateTime.MinValue"/> if none yet.</summary>
    public DateTime LastFrameUtc => new(Interlocked.Read(ref _lastPacketTicks), DateTimeKind.Utc);

    /// <summary>True while frames are arriving (the plugin pauses, so offline effects take over after two seconds).</summary>
    public bool HasLiveFrames => IsListening && DateTime.UtcNow - LastFrameUtc < TimeSpan.FromSeconds(2);

    /// <summary>The newest complete frame, one colour per LED, row by row.</summary>
    public ThemeColor[] Frame => _frame;

    public void Start(int port)
    {
        Stop();
        Error = null;
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.ExclusiveAddressUse = false;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            _udp = udp;
            var cts = _cts = new CancellationTokenSource();
            IsListening = true;
            _ = Task.Run(() => ReceiveLoop(udp, cts.Token));
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            Error = ex is SocketException { SocketErrorCode: SocketError.AccessDenied or SocketError.AddressAlreadyInUse }
                ? BigBro.Core.Settings.Loc.T("rgb.portTaken", "UDP port {0} is already taken by another program (RGB Wallpaper?) that does not allow sharing it. Close it, use a different port, or make that program open its socket with address reuse.", port)
                : BigBro.Core.Settings.Loc.T("rgb.cannotListen", "Could not listen on UDP port {0}: {1}", port, ex.Message);
            IsListening = false;
        }
    }

    public void Stop()
    {
        IsListening = false;
        _cts?.Cancel();
        _cts = null;
        _udp?.Close();
        _udp = null;
    }

    private async Task ReceiveLoop(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                Process(result.Buffer);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (ct.IsCancellationRequested) return;
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);   // e.g. network change
            }
        }
    }

    /// <summary>Handles one UDP datagram. Public so tests can feed packets without a socket.</summary>
    public void Process(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8) return;

        switch (data[0])
        {
            case 0:
                ProcessFramePacket(data);
                break;
            case 1:
                ProcessSettingsPacket(data);
                break;
        }
    }

    private void ProcessFramePacket(ReadOnlySpan<byte> data)
    {
        int index = data[1], count = data[2];
        if (index == 0) _pending.Clear();           // a new frame starts; drop leftovers of a frame that lost a packet
        _pending.AddRange(data[3..].ToArray());
        if (index != count - 1) return;

        var rgb = _pending.ToArray();
        _pending.Clear();

        var colors = new ThemeColor[rgb.Length / 3];
        for (var i = 0; i < colors.Length; i++)
            colors[i] = new ThemeColor(255, rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
        _frame = colors;
        Interlocked.Exchange(ref _lastPacketTicks, DateTime.UtcNow.Ticks);
    }

    private void ProcessSettingsPacket(ReadOnlySpan<byte> data)
    {
        if (data.Length < 19) return;

        var s = Settings;
        if (Enum.IsDefined((MatrixSizeType)data[1])) s.MatrixSizeType = (MatrixSizeType)data[1];
        if (Enum.IsDefined((MatrixSizeTier)data[2])) s.MatrixSizeTier = (MatrixSizeTier)data[2];
        if (Enum.IsDefined((OfflineEffect)data[3])) s.OfflineEffect = (OfflineEffect)data[3];
        // data[4] is "show FPS" in the plugin; there is no FPS counter on the panel.
        s.BlurIntensity = data[5];
        if (Enum.IsDefined((LedShape)data[6])) s.LedShape = (LedShape)data[6];
        s.RoundedRectangleCornerRadius = data[7];
        s.LedPadding = data[8];
        s.Fps = data[9];
        if (Enum.IsDefined((CoverStretch)data[10])) s.CoverImageStretch = (CoverStretch)data[10];
        // data[11] is the CPU usage at which the wallpaper pauses (not used here)
        s.BackgroundColor = new ThemeColor(255, data[12], data[13], data[14]);
        s.ShutdownColor = new ThemeColor(255, data[15], data[16], data[17]);
        // data[18] is the length byte; the cover image path follows
        var image = Encoding.UTF8.GetString(data[19..]).TrimEnd('\0').Trim();
        s.CoverImage = image.Length == 0 ? null : image;

        SettingsChanged?.Invoke();
    }

    public void Dispose() => Stop();
}
