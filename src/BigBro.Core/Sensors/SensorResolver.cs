using System.Globalization;
using BigBro.Core.Theming;

namespace BigBro.Core.Sensors;

/// <summary>
/// Turns a <see cref="SensorBinding"/> into a <see cref="SensorReading"/> using one LHM snapshot.
/// This is the 1.x <c>CPUSensors</c>/<c>GPUSensors</c>/... family rewritten over remote data; the name patterns,
/// units and rounding follow the originals.
/// </summary>
public sealed partial class SensorResolver
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly SensorSnapshot _s;
    private readonly LocalInfo _local;
    private readonly WeatherData _weather;

    private List<LhmSensor>? _cpu, _mobo, _mem, _vmem;

    public SensorResolver(SensorSnapshot snapshot, LocalInfo local, WeatherData? weather = null)
    {
        _s = snapshot;
        _local = local;
        _weather = weather ?? WeatherData.Unknown;
    }

    private List<LhmSensor> Cpu => _cpu ??= _s.Of(HardwareKind.Cpu).ToList();
    private List<LhmSensor> Mobo => _mobo ??= _s.Sensors.Where(x => x.Hardware == HardwareKind.Motherboard).ToList();
    private List<LhmSensor> Mem => _mem ??= _s.Of(HardwareKind.Memory).ToList();
    private List<LhmSensor> VMem => _vmem ??= _s.Of(HardwareKind.VirtualMemory).ToList();
    private List<LhmSensor> Gpu(int index) => _s.Of(HardwareKind.Gpu, index).ToList();

    public SensorReading Resolve(SensorBinding? binding) =>
        binding is null ? SensorReading.None : Resolve(binding.Kind, binding.Param);

    public SensorReading Resolve(SensorKind kind, string? param)
    {
        switch (kind)
        {
            case SensorKind.None:
                return SensorReading.None;

            case SensorKind.Lhm:
                return Raw(LhmSensorFor(param));

            case SensorKind.CpuCoreCount:
                return Count(Cpu.Count(x => x.Type == "Clock" && x.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase)));

            // ---- memory totals LHM doesn't report directly
            case SensorKind.RamTotal:
            {
                var (used, avail) = RamGb(Mem);
                return Gigabytes(used + avail, used + avail);
            }
            case SensorKind.RamVirtualTotal:
            {
                var (used, avail) = RamGb(VMem);
                return Gigabytes(used + avail, used + avail);
            }
            case SensorKind.RamClockSpeed:
            {
                var mhz = RamSpeed();
                return new SensorReading { Text = $"{mhz} MHz", Value = mhz.ToString(Inv), Number = mhz, ChartUnit = " MHz" };
            }

            // ---- this computer
            case SensorKind.HddTotalSize:
            case SensorKind.HddTotalFreeSpace:
            case SensorKind.HddUsage:
                return DriveReading(kind, param);
            case SensorKind.Ping:
                return PingReading(param);

            case SensorKind.LongDate: return Str(DateTime.Now.ToLongDateString());
            case SensorKind.ShortDate: return Str(DateTime.Now.ToShortDateString());
            case SensorKind.LongTime: return Str(DateTime.Now.ToLongTimeString());
            case SensorKind.ShortTime: return Str(DateTime.Now.ToShortTimeString());
            case SensorKind.CustomDateTime:
                return Str(SafeFormat(DateTime.Now, param));
            case SensorKind.SystemUptime:
                return Str(SafeFormat(_local.Uptime, param, @"d\.h\:mm\:ss"));

            // FPS/frametime come from MSI Afterburner (Windows, same PC); anything unavailable reads 0.
            case SensorKind.DisplayScreenResolution:
                return Str(_local.ScreenResolution());
            case SensorKind.DisplayRefreshRate:
                return Display(_local.RefreshRate, "Hz", " Hz");
            case SensorKind.DisplayFramerate:
                return Display(_local.FramesPerSecond, "FPS", " FPS");
            case SensorKind.DisplayFrametime:
                return Display(_local.FrameTimeMs, "ms", " ms");

            case SensorKind.WeatherTempC:
                return Weather($"{Plain(_weather.Temperature)} °C", Plain(_weather.Temperature), _weather.Temperature, "°C");
            case SensorKind.WeatherTempF:
            {
                var f = _weather.Temperature * 1.8 + 32;
                return Weather($"{Plain(f)} °F", Plain(f), f, "°F");
            }
            case SensorKind.WeatherName:
                return Str(_weather.Name);
            case SensorKind.WeatherCode:
                return new SensorReading { Text = _weather.Code.ToString(Inv), Value = _weather.Code.ToString(Inv), Number = _weather.Code, ImageIndex = _weather.Code };
            case SensorKind.WeatherWindSpeedKmh:
                return Weather($"{Plain(_weather.WindSpeed)} Km/h", Plain(_weather.WindSpeed), _weather.WindSpeed, " Km/h");
            case SensorKind.WeatherWindSpeedMph:
            {
                var mph = _weather.WindSpeed / 1.609;
                return Weather($"{Plain(mph)} Mph", Plain(mph), mph, " Mph");
            }
            case SensorKind.WeatherWindDirection:
                return Weather($"{Plain(_weather.WindDirection)} °", Plain(_weather.WindDirection), _weather.WindDirection, "°");

            default:
                return SensorReading.None;
        }
    }

    // ================================================================== lookups

    /// <summary>
    /// Finds a sensor of <paramref name="type"/> by name. Each candidate is tried in order, exact match first,
    /// then "contains" (as the 1.x code did).
    /// </summary>
    public static LhmSensor? Pick(IEnumerable<LhmSensor> sensors, string type, params string[] names)
        => Pick(sensors, [type], names);

    private static LhmSensor? Pick(IEnumerable<LhmSensor> sensors, string[] types, string[] names)
    {
        var list = sensors as IList<LhmSensor> ?? sensors.ToList();
        var ofType = list.Where(x => types.Contains(x.Type, StringComparer.OrdinalIgnoreCase)).ToList();
        foreach (var name in names)
        {
            var exact = ofType.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            var contains = ofType.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (contains is not null) return contains;
        }
        return null;
    }

    private static (double Used, double Available) RamGb(List<LhmSensor> mem)
    {
        var used = Math.Ceiling(Pick(mem, "Data", "Memory Used")?.Value ?? 0);
        var avail = Math.Ceiling(Pick(mem, "Data", "Memory Available")?.Value ?? 0);
        return (used, avail);
    }

    /// <summary>Rated speed (MT/s) of the fastest DIMM, from its SPD cycle time. LHM has no live memory clock.</summary>
    private int RamSpeed()
    {
        var tck = _s.Sensors
            .Where(x => x.Hardware == HardwareKind.Dimm && x.Name.StartsWith("tCKAVGmin", StringComparison.OrdinalIgnoreCase) && x.Value is > 0)
            .Select(x => x.Value!.Value)
            .DefaultIfEmpty(0)
            .Min();
        if (tck <= 0) return 0;
        var raw = 2000.0 / tck;
        int[] bins = [1600, 1866, 2133, 2400, 2666, 2933, 3200, 3600, 4000, 4266, 4800, 5200, 5600, 6000, 6400, 6800, 7200, 7600, 8000, 8400];
        var nearest = bins.MinBy(b => Math.Abs(b - raw));
        return Math.Abs(nearest - raw) / nearest < 0.012 ? nearest : (int)Math.Round(raw);
    }

    private LhmSensor? Nic(string sensorName)
    {
        var nics = _s.Hardware.Where(h => h.Kind == HardwareKind.Network).ToList();
        if (nics.Count == 0) return null;

        // The adapter carrying the most traffic right now; LHM also lists every virtual adapter.
        var chosen = nics.MaxBy(n => _s.Of(n)
            .Where(x => x.Type == "Throughput" && (x.Name == "Download Speed" || x.Name == "Upload Speed"))
            .Sum(x => x.Value ?? 0)) ?? nics[0];

        return Pick(_s.Of(chosen), "Throughput", sensorName);
    }

    // ================================================================== builders

    private static string Ceil(double v) => Math.Ceiling(v).ToString("0", Inv);
    private static string Round0(double v) => Math.Round(v).ToString("0", Inv);
    private static string Plain(double v) => Math.Round(v, 2).ToString("0.##", Inv);

    private static SensorReading Display(double value, string unit, string chartUnit)
    {
        var v = (int)Math.Round(value);
        return new SensorReading { Text = $"{v} {unit}", Value = v.ToString(Inv), Number = v, ChartUnit = chartUnit };
    }

    private static SensorReading Str(string s) => new() { Text = s, Value = s };

    private static SensorReading Count(int n) => new() { Text = n.ToString(Inv), Value = n.ToString(Inv), Number = n };

    private static SensorReading Weather(string text, string value, double number, string unit) =>
        new() { Text = text, Value = value, Number = number, ChartUnit = unit };

    private static SensorReading Gigabytes(double value, double total) => new()
    {
        Text = $"{value.ToString("0", Inv)} GB", Value = value.ToString("0", Inv), Number = value,
        Min = 0, Max = total, ChartUnit = " GB", Fraction = total > 0 ? Math.Clamp(value / total, 0, 1) : 0,
    };

    /// <summary>"1.25 MB/s" style text, 1024-based, two decimals; matches the 1.x GetSpeed.</summary>
    public static string SpeedText(double bytesPerSecond)
    {
        var b = Math.Max(0, bytesPerSecond);
        return b switch
        {
            >= 1099511627776.0 => $"{(b / 1099511627776.0).ToString("N2", Inv)} TB/s",
            >= 1073741824.0 => $"{(b / 1073741824.0).ToString("N2", Inv)} GB/s",
            >= 1048576.0 => $"{(b / 1048576.0).ToString("N2", Inv)} MB/s",
            _ => $"{(b / 1024.0).ToString("N2", Inv)} KB/s",
        };
    }

    /// <summary>"1.25 GB" style text, 1024-based, two decimals; matches the 1.x GetDiskSize.</summary>
    public static string SizeText(double bytes)
    {
        var b = Math.Max(0, bytes);
        return b switch
        {
            >= 1099511627776.0 => $"{(b / 1099511627776.0).ToString("N2", Inv)} TB",
            >= 1073741824.0 => $"{(b / 1073741824.0).ToString("N2", Inv)} GB",
            >= 1048576.0 => $"{(b / 1048576.0).ToString("N2", Inv)} MB",
            >= 1024.0 => $"{(b / 1024.0).ToString("N2", Inv)} KB",
            _ => $"{b.ToString("N2", Inv)} bytes",
        };
    }

    private SensorReading DriveReading(SensorKind kind, string? param)
    {
        var drive = _local.Drive(param);
        if (drive is null) return new SensorReading { Text = "0 byte", Value = "0", ChartUnit = " GB" };

        double total = drive.TotalSize, free = drive.TotalFreeSpace, used = total - free;
        var bytes = kind switch
        {
            SensorKind.HddTotalSize => total,
            SensorKind.HddTotalFreeSpace => free,
            _ => used,
        };
        var gb = (int)(bytes / 1073741824.0);
        var totalGb = (int)(total / 1073741824.0);
        return new SensorReading
        {
            Text = SizeText(bytes), Value = gb.ToString(Inv), Number = gb,
            Min = 0, Max = totalGb, ChartUnit = " GB",
            Fraction = total > 0 ? Math.Clamp(bytes / total, 0, 1) : 0,
        };
    }

    private SensorReading PingReading(string? host)
    {
        var ms = _local.Ping(string.IsNullOrWhiteSpace(host) ? "8.8.8.8" : host.Trim());
        return ms >= 0
            ? new SensorReading { Text = $"{ms} ms", Value = ms.ToString(Inv), Number = ms, ChartUnit = " ms" }
            : new SensorReading { Text = "Requested Timed Out", Value = "0", ChartUnit = " ms" };
    }

    /// <summary>
    /// A LibreHardwareMonitor sensor as the panel shows it. Number formats follow the 1.x conventions so migrated themes look the same:
    /// percentages, clocks, fan speeds and memory sizes round up to whole numbers, temperatures show one decimal, power two.
    /// </summary>
    private static SensorReading Raw(LhmSensor? s)
    {
        if (s is null) return SensorReading.None;
        var v = s.Value ?? 0;
        var unit = s.Unit;

        switch (unit)
        {
            case "%":
            {
                var n = Math.Ceiling(v);
                return new SensorReading
                {
                    Text = $"{Whole(n)} %", Value = Whole(n), Number = n, Min = 0, Max = 100, ChartUnit = "%",
                    Fraction = Math.Clamp(n / 100, 0, 1),
                };
            }
            case "°C":
                return new SensorReading
                {
                    Text = $"{v.ToString("0.#", Inv)} °C", Value = Round0(v), Number = Math.Round(v), Min = 0, Max = 120, ChartUnit = "°C",
                    Fraction = Math.Clamp(Math.Round(v) / 100, 0, 1),
                };
            case "MHz" or "RPM" or "MB" or "GB":
                return new SensorReading
                {
                    Text = $"{Ceil(v)} {unit}", Value = Ceil(v), Number = Math.Ceiling(v), ChartUnit = " " + unit,
                };
            case "W":
                return new SensorReading
                {
                    Text = $"{v.ToString("N2", Inv)} W", Value = Math.Round(v, 2).ToString("0.##", Inv), Number = Math.Round(v), ChartUnit = " W",
                };
            case "B/s" when s.Type == "Throughput":
            {
                var kb = Math.Round(v / 1024.0, 2);
                return new SensorReading { Text = SpeedText(v), Value = kb.ToString("0.##", Inv), Number = kb, ChartUnit = " KB/s" };
            }
            default:
            {
                var value = v.ToString("0.###", Inv);
                return new SensorReading
                {
                    Text = unit.Length > 0 ? $"{value} {unit}" : value, Value = value, Number = v,
                    ChartUnit = unit.Length > 0 ? " " + unit : "",
                };
            }
        }
    }

    private static string Whole(double v) => v.ToString("0", Inv);

    private static string SafeFormat(DateTime now, string? format)
    {
        if (string.IsNullOrEmpty(format)) return now.ToString(CultureInfo.CurrentCulture);
        try { return now.ToString(format, CultureInfo.CurrentCulture); }
        catch (FormatException) { return now.ToString(CultureInfo.CurrentCulture); }
    }

    private static string SafeFormat(TimeSpan span, string? format, string fallback)
    {
        try { return span.ToString(string.IsNullOrEmpty(format) ? fallback : format, CultureInfo.InvariantCulture); }
        catch (FormatException) { return span.ToString(fallback, CultureInfo.InvariantCulture); }
    }

    // ================================================================== detail lists

    /// <summary>The two text columns of a <see cref="DetailElement"/>: sensor names and their values, one per line.</summary>
    public (string Names, string Values) Detail(DetailGroup group)
    {
        IEnumerable<LhmSensor> source;
        string type, unit;
        switch (group)
        {
            case DetailGroup.CpuClockSpeed: source = Cpu; type = "Clock"; unit = " MHz"; break;
            case DetailGroup.CpuTemperature: source = Cpu; type = "Temperature"; unit = " °C"; break;
            case DetailGroup.CpuLoad: source = Cpu; type = "Load"; unit = " %"; break;
            case DetailGroup.CpuPowerWattage: source = Cpu; type = "Power"; unit = " W"; break;
            case DetailGroup.GpuClockSpeed: source = Gpu(0); type = "Clock"; unit = " MHz"; break;
            case DetailGroup.GpuTemperature: source = Gpu(0); type = "Temperature"; unit = " °C"; break;
            case DetailGroup.GpuLoadPercent: source = Gpu(0); type = "Load"; unit = " %"; break;
            case DetailGroup.MoboTemperature: source = Mobo; type = "Temperature"; unit = " °C"; break;
            case DetailGroup.MoboFan: source = Mobo; type = "Fan"; unit = " RPM"; break;
            default: return ("", "");
        }

        // 1.x listed each distinct sensor name once.
        var rows = source.Where(x => x.Type == type)
            .DistinctBy(x => x.Name)
            .ToList();
        return (string.Join("\n", rows.Select(r => r.Name)),
                string.Join("\n", rows.Select(r => Ceil(r.Value ?? 0) + unit)));
    }
}
