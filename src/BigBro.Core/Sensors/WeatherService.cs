using System.Globalization;
using System.Text.Json;

namespace BigBro.Core.Sensors;

public sealed record WeatherData(double Temperature, double WindSpeed, double WindDirection, int Code, string Name)
{
    public static readonly WeatherData Unknown = new(0, 0, 0, 0, "N/A");
}

/// <summary>Current weather from open-meteo.com (no API key needed).</summary>
public sealed class WeatherService(HttpClient http)
{
    public WeatherData Current { get; private set; } = WeatherData.Unknown;

    public async Task RefreshAsync(string? latitude, string? longitude, CancellationToken ct = default)
    {
        if (!double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            Current = WeatherData.Unknown;
            return;
        }

        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true");
        await using var stream = await http.GetStreamAsync(url, ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        Current = ParseCurrent(doc.RootElement);
    }

    public static WeatherData ParseCurrent(JsonElement root)
    {
        var cw = root.GetProperty("current_weather");
        var code = (int)cw.GetProperty("weathercode").GetDouble();
        return new WeatherData(
            cw.GetProperty("temperature").GetDouble(),
            cw.GetProperty("windspeed").GetDouble(),
            cw.GetProperty("winddirection").GetDouble(),
            code,
            Describe(code));
    }

    /// <summary>WMO weather interpretation codes, worded as in 1.x.</summary>
    public static string Describe(int code) => code switch
    {
        0 => "Clear Sky",
        1 => "Mainly Clear",
        2 => "Partly Cloudy",
        3 => "Cloudy",
        45 => "Fog",
        48 => "Depositing Rime Fog",
        51 => "Light Drizzle",
        53 => "Moderate Drizzle",
        55 => "Dense Drizzle",
        56 => "Light Freezing Drizzle",
        57 => "Dense Freezing Drizzle",
        61 => "Slight Rain",
        63 => "Moderate Rain",
        65 => "Heavy Rain",
        66 => "Light Freezing Rain",
        67 => "Heavy Freezing Rain",
        71 => "Slight Snow fall",
        73 => "Moderate Snow fall",
        75 => "Heavy Snow fall",
        77 => "Snow grains",
        80 => "Slight Rain showers",
        81 => "Moderate Rain showers",
        82 => "Violent Rain showers",
        85 => "Slight Snow showers",
        86 => "Heavy Snow showers",
        95 => "Slight Thunderstorm",
        96 => "Slight Thunderstorm",
        99 => "Thunderstorm with Heavy Hail",
        _ => $"Unknown code {code}",
    };
}
