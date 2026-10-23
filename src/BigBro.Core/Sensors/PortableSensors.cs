using BigBro.Core.Settings;

namespace BigBro.Core.Sensors;

/// <summary>
/// Sensors named by what they measure instead of by one PC's hardware path, for example <c>@CpuLoadPercent</c> or
/// <c>@CpuFan:1</c>. The resolver finds the matching LibreHardwareMonitor (or Linux) sensor on whatever PC it is reading, so a
/// theme built with them works everywhere without choosing sensors first.
/// </summary>
public static class PortableSensors
{
    public static readonly (string Alias, string English)[] All =
    [
        ("@CpuLoadPercent", "CPU load"),
        ("@CpuTemperatureC", "CPU temperature"),
        ("@CpuClockSpeed", "CPU clock speed"),
        ("@CpuPowerWattage", "CPU power"),
        ("@CpuFan:1", "CPU fan"),
        ("@GpuLoadPercent", "GPU load"),
        ("@GpuTemperatureC", "GPU temperature"),
        ("@GpuClockSpeed", "GPU clock speed"),
        ("@GpuMemoryClock", "GPU memory clock"),
        ("@GpuPowerWattage", "GPU power"),
        ("@GpuFan", "GPU fan"),
        ("@GpuMemoryPercent", "GPU memory load"),
        ("@GpuVramUsage", "GPU memory used"),
        ("@RamLoadPercent", "Memory load"),
        ("@RamUsage", "Memory used"),
        ("@RamAvailable", "Memory available"),
        ("@MoboTemperatureC", "Motherboard temperature"),
        ("@DownloadSpeed", "Download speed"),
        ("@UploadSpeed", "Upload speed"),
    ];

    public static bool IsAlias(string? param) => param is { Length: > 1 } && param[0] == '@';

    public static string Key(string alias) => "portable." + alias.TrimStart('@').Replace(':', '_');

    /// <summary>The translated name of an alias, or null when <paramref name="param"/> isn't a known one.</summary>
    public static string? NameOf(string? param)
    {
        foreach (var (alias, english) in All)
            if (string.Equals(alias, param, StringComparison.OrdinalIgnoreCase)) return Loc.T(Key(alias), english);
        return null;
    }
}
