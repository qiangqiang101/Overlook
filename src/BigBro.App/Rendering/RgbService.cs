using System.Text.Json;
using BigBro.Core.Rgb;
using BigBro.Core.Settings;

namespace BigBro.App.Rendering;

/// <summary>
/// Owns the UDP lighting receiver. It only listens while at least one RGB background is on screen, and it keeps the settings
/// the lighting plugin sends so the next start looks the same before the first packet arrives.
/// </summary>
public sealed class RgbService : IDisposable
{
    private readonly Action _save;
    private readonly object _gate = new();
    private int _users;
    private string _lastSaved;

    public RgbService(RgbSettings settings, Action save)
    {
        Settings = settings;
        _save = save;
        _lastSaved = JsonSerializer.Serialize(settings);
        Source = new RgbSource(settings);
        Source.SettingsChanged += OnPluginSettings;
    }

    public RgbSettings Settings { get; }
    public RgbSource Source { get; }

    /// <summary>Raised after the user edited the RGB settings, so open panels redraw with them.</summary>
    public event Action? Edited;

    public void NotifyEdited() => Edited?.Invoke();

    /// <summary>Call when an RGB background appears; pair with <see cref="Release"/>.</summary>
    public void Acquire()
    {
        lock (_gate)
        {
            if (++_users == 1) Restart();
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            if (_users > 0 && --_users == 0) Source.Stop();
        }
    }

    /// <summary>Re-opens the port after the user changed it or switched listening on or off.</summary>
    public void Restart()
    {
        lock (_gate)
        {
            Source.Stop();
            if (_users > 0 && Settings.Listen) Source.Start(Settings.Port);
        }
    }

    public string Status
    {
        get
        {
            if (!Settings.Listen) return Loc.T("rgb.off", "Listening is off; showing the offline effect.");
            if (Source.Error is { } error) return error;
            if (!Source.IsListening) return Loc.T("rgb.notYet", "Not listening yet (UDP port {0}); it starts when a theme with an RGB background is shown.", Settings.Port);
            return Source.HasLiveFrames
                ? Loc.T("rgb.receiving", "Receiving lighting on UDP port {0}.", Settings.Port)
                : Loc.T("rgb.waiting", "Waiting for SignalRGB / OpenRGB / RGBJunkie on UDP port {0}; showing the offline effect.", Settings.Port);
        }
    }

    // The plugin may repeat its settings every few seconds; only touch the disk when something changed.
    private void OnPluginSettings()
    {
        var json = JsonSerializer.Serialize(Settings);
        if (json == _lastSaved) return;
        _lastSaved = json;
        try { _save(); }
        catch (IOException) { }
    }

    public void Dispose()
    {
        Source.SettingsChanged -= OnPluginSettings;
        Source.Dispose();
    }
}
