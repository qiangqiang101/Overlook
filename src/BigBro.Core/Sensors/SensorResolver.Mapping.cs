using BigBro.Core.Legacy;
using BigBro.Core.Theming;

namespace BigBro.Core.Sensors;

public sealed partial class SensorResolver
{
    /// <summary>The sensor an <see cref="LegacySensorKind.Lhm"/> parameter points at: a tree path, or an LHM sensor id (starts with '/').</summary>
    private LhmSensor? LhmSensorFor(string? param)
    {
        if (string.IsNullOrWhiteSpace(param)) return null;
        var key = param.Trim();
        return key.StartsWith('/') ? _s.ById(key) : _s.ByPath(key);
    }

    /// <summary>
    /// The LibreHardwareMonitor sensor a 1.x sensor kind reads from on this machine, or null when the machine has no such sensor.
    /// Used to suggest sensors when 1.x themes are imported.
    /// </summary>
    public LhmSensor? SensorFor(LegacySensorKind kind, string? param)
    {
        static int Index(string? p, int fallback) => int.TryParse(p?.Trim(), out var i) ? i : fallback;
        var gpu = () => Gpu(Index(param, 0));

        return kind switch
        {
            LegacySensorKind.CpuClockSpeed => Pick(Cpu, "Clock", $"CPU Core #{Index(param, 1)}"),
            LegacySensorKind.CpuBusSpeed => Pick(Cpu, "Clock", "Bus Speed"),
            LegacySensorKind.CpuTemperatureC or LegacySensorKind.CpuTemperatureF => Pick(Cpu, "Temperature", "CPU Package", "Core (Tctl/Tdie)", "Core Average", "Core Max", "CPU Core #1"),
            LegacySensorKind.CpuCoreTemperatureC or LegacySensorKind.CpuCoreTemperatureF => Pick(Cpu, "Temperature", $"CPU Core #{Index(param, 1)}"),
            LegacySensorKind.CpuLoadPercent => Pick(Cpu, "Load", "CPU Total"),
            LegacySensorKind.CpuPowerWattage => Pick(Cpu, "Power", "CPU Package"),
            LegacySensorKind.CpuCorePower => Pick(Cpu, "Power", "CPU Cores"),
            LegacySensorKind.CpuGraphicPower => Pick(Cpu, "Power", "CPU Graphics", "Graphics"),
            LegacySensorKind.CpuDramPower => Pick(Cpu, "Power", "CPU DRAM", "CPU Memory", "DRAM"),
            LegacySensorKind.CpuFan or LegacySensorKind.MoboFan => Pick(Mobo, "Fan", $"Fan #{Index(param, 1)}"),

            LegacySensorKind.GpuClockSpeed => Pick(gpu(), "Clock", "GPU Core"),
            LegacySensorKind.GpuMemoryClock => Pick(gpu(), "Clock", "GPU Memory"),
            LegacySensorKind.GpuShaderClock => Pick(gpu(), "Clock", "GPU Shader"),
            LegacySensorKind.GpuTemperatureC or LegacySensorKind.GpuTemperatureF => Pick(gpu(), "Temperature", "GPU Core"),
            LegacySensorKind.GpuLoadPercent => Pick(gpu(), "Load", "GPU Core"),
            LegacySensorKind.GpuMemoryPercent => Pick(gpu(), "Load", "GPU Memory"),
            LegacySensorKind.GpuFrameBufferLoad => Pick(gpu(), "Load", "GPU Frame Buffer", "GPU Memory Controller"),
            LegacySensorKind.GpuVideoEngineLoad => Pick(gpu(), "Load", "GPU Video Engine"),
            LegacySensorKind.GpuBusInterfaceLoad => Pick(gpu(), "Load", "GPU Bus Interface", "GPU Bus"),
            LegacySensorKind.GpuPowerWattage => Pick(gpu(), "Power", "GPU Power", "GPU Package", "GPU"),
            LegacySensorKind.GpuFan => Pick(gpu(), "Fan", "GPU Fan", "GPU"),
            LegacySensorKind.GpuVramUsage => Pick(gpu(), ["SmallData", "Data"], ["GPU Memory Used"]),
            LegacySensorKind.GpuVramFree => Pick(gpu(), ["SmallData", "Data"], ["GPU Memory Free"]),
            LegacySensorKind.GpuVramTotal => Pick(gpu(), ["SmallData", "Data"], ["GPU Memory Total"]),

            LegacySensorKind.RamLoadPercent => Pick(Mem, "Load", "Memory"),
            LegacySensorKind.RamUsage => Pick(Mem, "Data", "Memory Used"),
            LegacySensorKind.RamAvailable => Pick(Mem, "Data", "Memory Available"),
            LegacySensorKind.RamVirtualUsage => Pick(VMem, "Data", "Memory Used"),
            LegacySensorKind.RamVirtualAvailable => Pick(VMem, "Data", "Memory Available"),

            LegacySensorKind.HddTemperatureC or LegacySensorKind.HddTemperatureF => Pick(_s.Of(HardwareKind.Storage, Index(param, 0)), "Temperature", "Temperature"),
            LegacySensorKind.HddLoadPercent => Pick(_s.Of(HardwareKind.Storage, Index(param, 0)), "Load", "Used Space"),

            LegacySensorKind.MoboTemperatureC or LegacySensorKind.MoboTemperatureF => Pick(Mobo, "Temperature", "CPU Core", "CPU", "Temperature #1"),

            LegacySensorKind.DownloadSpeed => Nic("Download Speed"),
            LegacySensorKind.UploadSpeed => Nic("Upload Speed"),
            _ => null,
        };
    }
}

/// <summary>The values that are not a single LibreHardwareMonitor sensor.</summary>
public static class SensorKinds
{
    public static bool IsSpecial(SensorKind kind) => kind is not (SensorKind.None or SensorKind.Lhm);

    public static readonly SensorKind[] Specials = [.. Enum.GetValues<SensorKind>().Where(IsSpecial)];
}
