namespace BigBro.Core.Sensors;

public enum HardwareKind
{
    Other,
    Cpu,
    Gpu,
    Memory,
    VirtualMemory,
    Dimm,
    Motherboard,
    Storage,
    Network,
    Battery,
}

/// <summary>One sensor from LibreHardwareMonitor's <c>data.json</c>.</summary>
public sealed record LhmSensor
{
    /// <summary>LHM SensorId, e.g. <c>/intelcpu/0/temperature/16</c>. Unique.</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>LHM sensor type: Temperature, Load, Clock, Fan, Power, Data, SmallData, Throughput, ...</summary>
    public required string Type { get; init; }
    public double? Value { get; init; }
    /// <summary>Unit as text ("MHz", "%", "°C", "B/s", ...). Empty for unit-less sensors.</summary>
    public string Unit { get; init; } = "";
    /// <summary>The value as LHM formats it for display.</summary>
    public string DisplayValue { get; init; } = "";

    /// <summary>The top-level hardware item this sensor belongs to (a motherboard's Super-I/O chip belongs to the board).</summary>
    public required string HardwareId { get; init; }
    public required string HardwareName { get; init; }
    public HardwareKind Hardware { get; init; }
    /// <summary>The group node it sits under in LHM's tree: "Temperatures", "Load", ...</summary>
    public string Group { get; init; } = "";

    /// <summary>
    /// The sensor's address as LibreHardwareMonitor's tree shows it: <c>Computer/Hardware/[Chip/]Group/Name</c>,
    /// e.g. <c>ROGPC2/Intel Core i5-14600K/Load/CPU Total</c>.
    /// </summary>
    public string Path { get; init; } = "";
}

public sealed record HardwareItem(string Id, string Name, HardwareKind Kind);

/// <summary>Immutable copy of everything LHM reported in one poll.</summary>
public sealed class SensorSnapshot
{
    public static readonly SensorSnapshot Empty = new([], [], DateTimeOffset.MinValue);

    private readonly Dictionary<string, LhmSensor> _byId;
    private readonly Dictionary<string, LhmSensor> _byPath;
    private readonly Dictionary<string, LhmSensor> _byPathNoComputer;

    public SensorSnapshot(IReadOnlyList<HardwareItem> hardware, IReadOnlyList<LhmSensor> sensors, DateTimeOffset takenAt, string computerName = "")
    {
        ComputerName = computerName;
        Hardware = hardware;
        Sensors = sensors;
        TakenAt = takenAt;
        _byId = new Dictionary<string, LhmSensor>(StringComparer.Ordinal);
        _byPath = new Dictionary<string, LhmSensor>(StringComparer.OrdinalIgnoreCase);
        _byPathNoComputer = new Dictionary<string, LhmSensor>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sensors)
        {
            _byId[s.Id] = s;
            _byPath.TryAdd(s.Path, s);                     // first one wins when LHM lists the same name twice
            var slash = s.Path.IndexOf('/');
            if (slash >= 0) _byPathNoComputer.TryAdd(s.Path[(slash + 1)..], s);
        }
    }

    /// <summary>The monitored PC's name, the first level of every sensor path.</summary>
    public string ComputerName { get; }
    public IReadOnlyList<HardwareItem> Hardware { get; }
    public IReadOnlyList<LhmSensor> Sensors { get; }
    public DateTimeOffset TakenAt { get; }
    public bool IsEmpty => Sensors.Count == 0;

    public LhmSensor? ById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// Finds a sensor by its tree path. A theme made on another PC still works: if the computer name differs,
    /// the rest of the path is tried on its own.
    /// </summary>
    public LhmSensor? ByPath(string path)
    {
        if (_byPath.TryGetValue(path, out var s)) return s;
        var slash = path.IndexOf('/');
        return slash >= 0 && _byPathNoComputer.TryGetValue(path[(slash + 1)..], out s) ? s : null;
    }

    /// <summary>The <paramref name="index"/>-th hardware of that kind, in the order LHM lists them.</summary>
    public HardwareItem? HardwareAt(HardwareKind kind, int index = 0)
    {
        if (index < 0) return null;
        return Hardware.Where(h => h.Kind == kind).Skip(index).FirstOrDefault();
    }

    public IEnumerable<LhmSensor> Of(HardwareItem? hardware) =>
        hardware is null ? [] : Sensors.Where(s => s.HardwareId == hardware.Id);

    public IEnumerable<LhmSensor> Of(HardwareKind kind, int index = 0) => Of(HardwareAt(kind, index));
}
