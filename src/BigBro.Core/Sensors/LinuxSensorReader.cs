using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace BigBro.Core.Sensors;

/// <summary>
/// Reads this Linux computer's own sensors straight from <c>/proc</c> and <c>/sys</c> (hwmon, powercap, thermal zones, drm) and,
/// for NVIDIA cards, from <c>nvidia-smi</c>. The result has the same shape as a LibreHardwareMonitor poll
/// (<c>Computer/Hardware/[Chip/]Group/Name</c>), so themes, the sensor picker and the editor work with it unchanged.
/// </summary>
public sealed class LinuxSensorReader
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly string _root;
    private readonly Func<string?> _nvidiaSmi;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly Dictionary<string, (long Total, long Idle)> _cpuPrev = new();
    private readonly Dictionary<string, (long Rx, long Tx, double At)> _netPrev = new();
    private (long Energy, double At)? _energyPrev;
    private string? _nvidiaCache;
    private double _nvidiaAt = double.NegativeInfinity;
    private double _nvidiaRetryAt;

    public static bool IsSupported => OperatingSystem.IsLinux();

    /// <param name="root">The filesystem root; tests point it at a folder that imitates <c>/proc</c> and <c>/sys</c>.</param>
    /// <param name="nvidiaSmi">Returns the CSV that <c>nvidia-smi --query-gpu=...</c> prints, or null when there is none.</param>
    public LinuxSensorReader(string root = "/", Func<string?>? nvidiaSmi = null)
    {
        _root = root;
        _nvidiaSmi = nvidiaSmi ?? RunNvidiaSmi;
    }

    private sealed record Chip(string Dir, string Name);

    public SensorSnapshot Read()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var b = new Builder(Environment.MachineName);
        var chips = HwmonChips();

        AddCpu(b, chips);
        AddBoardChips(b, chips);
        AddGpus(b, chips, now);
        AddMemory(b);
        AddStorage(b, chips);
        AddNetwork(b, now);

        return new SensorSnapshot(b.Hardware, b.Sensors, DateTimeOffset.UtcNow, b.Computer);
    }

    // ================================================================== CPU

    private void AddCpu(Builder b, List<Chip> chips)
    {
        var cpuinfo = Lines("/proc/cpuinfo");
        var name = cpuinfo.Select(l => Field(l, "model name")).FirstOrDefault(v => v is not null)
                   ?? cpuinfo.Select(l => Field(l, "Hardware")).FirstOrDefault(v => v is not null)
                   ?? cpuinfo.Select(l => Field(l, "Model")).FirstOrDefault(v => v is not null)
                   ?? "CPU";
        var id = name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "/amdcpu/0"
               : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "/intelcpu/0"
               : "/cpu/0";
        var cpu = new HardwareItem(id, name, HardwareKind.Cpu);

        // load: share of non-idle time between two polls, like Task Manager
        foreach (var line in Lines("/proc/stat"))
        {
            if (!line.StartsWith("cpu", StringComparison.Ordinal)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            var values = parts.Skip(1).Take(8).Select(p => long.TryParse(p, out var n) ? n : 0).ToArray();
            var total = values.Sum();
            var idle = values[3] + (values.Length > 4 ? values[4] : 0);        // idle + iowait

            double load = 0;
            if (_cpuPrev.TryGetValue(parts[0], out var prev) && total > prev.Total)
                load = 100.0 * (1.0 - (double)(idle - prev.Idle) / (total - prev.Total));
            _cpuPrev[parts[0]] = (total, idle);

            var label = parts[0] == "cpu" ? "CPU Total" : int.TryParse(parts[0][3..], out var core) ? $"CPU Core #{core + 1}" : null;
            if (label is not null) b.Add(cpu, null, "Load", label, "Load", Math.Clamp(load, 0, 100), "%");
        }

        // clocks
        var cores = Directory_("/sys/devices/system/cpu").Where(d => IsCpuDir(Path.GetFileName(d))).OrderBy(d => CpuIndex(Path.GetFileName(d))).ToList();
        var clocked = false;
        foreach (var dir in cores)
        {
            if (Number(Text(dir + "/cpufreq/scaling_cur_freq")) is { } khz)
            {
                b.Add(cpu, null, "Clocks", $"CPU Core #{CpuIndex(Path.GetFileName(dir)) + 1}", "Clock", khz / 1000.0, "MHz");
                clocked = true;
            }
        }
        if (!clocked)
        {
            var n = 0;
            foreach (var mhz in cpuinfo.Select(l => Field(l, "cpu MHz")).Where(v => v is not null))
                b.Add(cpu, null, "Clocks", $"CPU Core #{++n}", "Clock", Number(mhz), "MHz");
        }

        // temperatures
        var temps = false;
        foreach (var chip in chips.Where(c => c.Name is "coretemp" or "k10temp" or "zenpower" or "cpu_thermal" or "cpu-thermal"))
        {
            foreach (var (index, file) in HwmonInputs(chip, "temp"))
            {
                var label = Text(chip.Dir + $"/temp{index}_label");
                b.Add(cpu, null, "Temperatures", CpuTempName(label, index), "Temperature", Number(Text(file)) / 1000.0, "°C");
                temps = true;
            }
        }
        if (!temps)
        {
            foreach (var zone in Directory_("/sys/class/thermal").Where(d => Path.GetFileName(d).StartsWith("thermal_zone", StringComparison.Ordinal)))
            {
                var type = Text(zone + "/type") ?? "";
                if (type == "x86_pkg_temp" || type.Contains("cpu", StringComparison.OrdinalIgnoreCase))
                    b.Add(cpu, null, "Temperatures", "CPU Package", "Temperature", Number(Text(zone + "/temp")) / 1000.0, "°C");
            }
        }

        // package power from the RAPL energy counter (often root-only; skipped when unreadable)
        if (Number(Text("/sys/class/powercap/intel-rapl:0/energy_uj")) is { } energy)
        {
            var now = _clock.Elapsed.TotalSeconds;
            if (_energyPrev is { } prev && now > prev.At)
            {
                var delta = energy - prev.Energy;
                if (delta < 0) delta += Number(Text("/sys/class/powercap/intel-rapl:0/max_energy_range_uj")) ?? 0;       // the counter wrapped
                if (delta >= 0) b.Add(cpu, null, "Powers", "CPU Package", "Power", delta / 1e6 / (now - prev.At), "W");
            }
            _energyPrev = ((long)energy, now);
        }
    }

    private static bool IsCpuDir(string name) => name.Length > 3 && name.StartsWith("cpu", StringComparison.Ordinal) && name[3..].All(char.IsDigit);
    private static int CpuIndex(string name) => int.Parse(name[3..], Inv);

    private static string CpuTempName(string? label, int index)
    {
        if (string.IsNullOrWhiteSpace(label)) return $"Temperature #{index}";
        if (label.StartsWith("Package id", StringComparison.OrdinalIgnoreCase)) return "CPU Package";
        if (label.StartsWith("Core ", StringComparison.OrdinalIgnoreCase) && int.TryParse(label[5..], out var core)) return $"CPU Core #{core + 1}";
        if (label is "Tctl" or "Tdie") return "Core (Tctl/Tdie)";
        return label;
    }

    // ================================================================== motherboard: every other hwmon chip

    private static readonly HashSet<string> NotBoard = new(StringComparer.OrdinalIgnoreCase)
    {
        "coretemp", "k10temp", "zenpower", "cpu_thermal", "cpu-thermal", "amdgpu", "nouveau", "nvidia", "i915", "xe",
        "nvme", "drivetemp", "BAT0", "BAT1", "ACAD", "AC", "ucsi_source_psy_USBC000:001",
    };

    private void AddBoardChips(Builder b, List<Chip> chips)
    {
        var board = new HardwareItem("/motherboard", Text("/sys/class/dmi/id/board_name") ?? "Motherboard", HardwareKind.Motherboard);
        foreach (var chip in chips.Where(c => !NotBoard.Contains(c.Name)))
        {
            foreach (var (index, file) in HwmonInputs(chip, "temp"))
                b.Add(board, chip.Name, "Temperatures", Label(chip, "temp", index, "Temperature"), "Temperature", Number(Text(file)) / 1000.0, "°C");
            foreach (var (index, file) in HwmonInputs(chip, "fan"))
                b.Add(board, chip.Name, "Fans", Label(chip, "fan", index, "Fan"), "Fan", Number(Text(file)), "RPM");
            foreach (var (index, file) in HwmonInputs(chip, "in"))
                b.Add(board, chip.Name, "Voltages", Label(chip, "in", index, "Voltage"), "Voltage", Number(Text(file)) / 1000.0, "V");
            foreach (var (index, file) in HwmonInputs(chip, "power"))
                b.Add(board, chip.Name, "Powers", Label(chip, "power", index, "Power"), "Power", Number(Text(file)) / 1e6, "W");
        }
    }

    private string Label(Chip chip, string kind, int index, string fallback) =>
        Text(chip.Dir + $"/{kind}{index}_label") is { Length: > 0 } label ? label : $"{fallback} #{index}";

    // ================================================================== GPUs

    private void AddGpus(Builder b, List<Chip> chips, double now)
    {
        var n = 0;
        foreach (var chip in chips.Where(c => c.Name == "amdgpu"))
        {
            var gpu = new HardwareItem($"/gpu-amd/{n++}", Text(chip.Dir + "/device/product_name") ?? "AMD GPU", HardwareKind.Gpu);
            foreach (var (index, file) in HwmonInputs(chip, "temp"))
            {
                var name = Text(chip.Dir + $"/temp{index}_label") switch
                {
                    "edge" => "GPU Core",
                    "junction" => "GPU Hot Spot",
                    "mem" => "GPU Memory",
                    { Length: > 0 } other => other,
                    _ => index == 1 ? "GPU Core" : $"Temperature #{index}",
                };
                b.Add(gpu, null, "Temperatures", name, "Temperature", Number(Text(file)) / 1000.0, "°C");
            }
            foreach (var (index, file) in HwmonInputs(chip, "fan"))
                b.Add(gpu, null, "Fans", "GPU Fan", "Fan", Number(Text(file)), "RPM");
            var power = Number(Text(chip.Dir + "/power1_average")) ?? Number(Text(chip.Dir + "/power1_input"));
            b.Add(gpu, null, "Powers", "GPU Package", "Power", power / 1e6, "W");
            b.Add(gpu, null, "Clocks", "GPU Core", "Clock", Number(Text(chip.Dir + "/freq1_input")) / 1e6, "MHz");
            b.Add(gpu, null, "Clocks", "GPU Memory", "Clock", Number(Text(chip.Dir + "/freq2_input")) / 1e6, "MHz");
            b.Add(gpu, null, "Load", "GPU Core", "Load", Number(Text(chip.Dir + "/device/gpu_busy_percent")), "%");

            var used = Number(Text(chip.Dir + "/device/mem_info_vram_used"));
            var total = Number(Text(chip.Dir + "/device/mem_info_vram_total"));
            b.Add(gpu, null, "Data", "GPU Memory Used", "SmallData", used / 1048576.0, "MB");
            b.Add(gpu, null, "Data", "GPU Memory Total", "SmallData", total / 1048576.0, "MB");
            if (used is { } u && total is > 0) b.Add(gpu, null, "Load", "GPU Memory", "Load", 100.0 * u / total.Value, "%");
        }

        AddNvidia(b, now);
    }

    private void AddNvidia(Builder b, double now)
    {
        string? csv;
        if (now - _nvidiaAt < 1.5) csv = _nvidiaCache;
        else if (now < _nvidiaRetryAt) csv = null;
        else
        {
            csv = _nvidiaSmi();
            _nvidiaAt = now;
            _nvidiaCache = csv;
            if (csv is null) _nvidiaRetryAt = now + 60;          // no driver or no binary: don't spawn a process every poll
        }
        if (string.IsNullOrWhiteSpace(csv)) return;

        var n = 0;
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // name, temperature, gpu %, memory-controller %, core MHz, memory MHz, fan %, power W, memory used MB, memory total MB
            var fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length < 10) continue;
            var f = fields[^9..];
            var gpu = new HardwareItem($"/gpu-nvidia/{n++}", string.Join(", ", fields[..^9]), HardwareKind.Gpu);

            b.Add(gpu, null, "Temperatures", "GPU Core", "Temperature", Number(f[0]), "°C");
            b.Add(gpu, null, "Load", "GPU Core", "Load", Number(f[1]), "%");
            b.Add(gpu, null, "Load", "GPU Memory Controller", "Load", Number(f[2]), "%");
            b.Add(gpu, null, "Clocks", "GPU Core", "Clock", Number(f[3]), "MHz");
            b.Add(gpu, null, "Clocks", "GPU Memory", "Clock", Number(f[4]), "MHz");
            b.Add(gpu, null, "Controls", "GPU Fan", "Control", Number(f[5]), "%");
            b.Add(gpu, null, "Powers", "GPU Package", "Power", Number(f[6]), "W");
            var used = Number(f[7]);
            var total = Number(f[8]);
            b.Add(gpu, null, "Data", "GPU Memory Used", "SmallData", used, "MB");
            b.Add(gpu, null, "Data", "GPU Memory Total", "SmallData", total, "MB");
            if (used is { } u && total is > 0) b.Add(gpu, null, "Load", "GPU Memory", "Load", 100.0 * u / total.Value, "%");
        }
    }

    private static string? RunNvidiaSmi()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("nvidia-smi",
                "--query-gpu=name,temperature.gpu,utilization.gpu,utilization.memory,clocks.gr,clocks.mem,fan.speed,power.draw,memory.used,memory.total --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return null;
            }
            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    // ================================================================== memory

    private void AddMemory(Builder b)
    {
        var info = new Dictionary<string, double>();
        foreach (var line in Lines("/proc/meminfo"))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var number = line[(colon + 1)..].Replace("kB", "").Trim();
            if (double.TryParse(number, NumberStyles.Float, Inv, out var kb)) info[line[..colon]] = kb;
        }
        if (!info.TryGetValue("MemTotal", out var total) || total <= 0) return;

        const double GB = 1024.0 * 1024.0;                       // meminfo is in kB
        var available = info.GetValueOrDefault("MemAvailable", info.GetValueOrDefault("MemFree"));
        var used = total - available;
        var ram = new HardwareItem("/ram", "Generic Memory", HardwareKind.Memory);
        b.Add(ram, null, "Load", "Memory", "Load", 100.0 * used / total, "%");
        b.Add(ram, null, "Data", "Memory Used", "Data", used / GB, "GB");
        b.Add(ram, null, "Data", "Memory Available", "Data", available / GB, "GB");

        if (info.TryGetValue("SwapTotal", out var swap) && swap > 0)
        {
            var swapUsed = swap - info.GetValueOrDefault("SwapFree");
            var virt = new HardwareItem("/vram/0", "Virtual Memory", HardwareKind.VirtualMemory);
            b.Add(virt, null, "Load", "Virtual Memory", "Load", 100.0 * swapUsed / swap, "%");
            b.Add(virt, null, "Data", "Virtual Memory Used", "Data", swapUsed / GB, "GB");
            b.Add(virt, null, "Data", "Virtual Memory Available", "Data", (swap - swapUsed) / GB, "GB");
        }
    }

    // ================================================================== drives

    private void AddStorage(Builder b, List<Chip> chips)
    {
        var n = 0;
        foreach (var chip in chips.Where(c => c.Name is "nvme" or "drivetemp"))
        {
            var nvme = chip.Name == "nvme";
            var model = Text(chip.Dir + "/device/model");
            var drive = new HardwareItem(nvme ? $"/nvme/{n++}" : $"/hdd/{n++}", string.IsNullOrWhiteSpace(model) ? (nvme ? "NVMe SSD" : "Drive") : model, HardwareKind.Storage);
            foreach (var (index, file) in HwmonInputs(chip, "temp"))
            {
                var label = Text(chip.Dir + $"/temp{index}_label");
                var name = label is null or "Composite" ? (index == 1 ? "Temperature" : $"Temperature #{index}") : label;
                b.Add(drive, null, "Temperatures", name, "Temperature", Number(Text(file)) / 1000.0, "°C");
            }
        }
    }

    // ================================================================== network

    private static readonly string[] VirtualNics = ["lo", "veth", "docker", "br-", "virbr", "tun", "tap", "vmnet", "zt"];

    private void AddNetwork(Builder b, double now)
    {
        foreach (var line in Lines("/proc/net/dev"))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var nic = line[..colon].Trim();
            if (VirtualNics.Any(v => nic.StartsWith(v, StringComparison.Ordinal))) continue;

            var f = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 9 || !long.TryParse(f[0], out var rx) || !long.TryParse(f[8], out var tx)) continue;

            double down = 0, up = 0;
            if (_netPrev.TryGetValue(nic, out var prev) && now > prev.At && rx >= prev.Rx && tx >= prev.Tx)
            {
                down = (rx - prev.Rx) / (now - prev.At);
                up = (tx - prev.Tx) / (now - prev.At);
            }
            _netPrev[nic] = (rx, tx, now);

            var hw = new HardwareItem($"/nic/{nic}", nic, HardwareKind.Network);
            if (Number(Text($"/sys/class/net/{nic}/speed")) is > 0 and var mbit)
                b.Add(hw, null, "Load", "Network Utilization", "Load", Math.Min(100, Math.Max(down, up) * 8 / (mbit * 1e6) * 100), "%");
            b.Add(hw, null, "Data", "Data Uploaded", "Data", tx / 1073741824.0, "GB");
            b.Add(hw, null, "Data", "Data Downloaded", "Data", rx / 1073741824.0, "GB");
            b.Add(hw, null, "Throughput", "Upload Speed", "Throughput", up, "B/s");
            b.Add(hw, null, "Throughput", "Download Speed", "Throughput", down, "B/s");
        }
    }

    // ================================================================== file helpers

    private string Full(string path) => Path.Combine(_root, path.TrimStart('/'));

    private string? Text(string path)
    {
        try { return File.ReadAllText(Full(path)).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }      // missing, root-only or "No data"
    }

    private IEnumerable<string> Lines(string path)
    {
        try { return File.ReadAllLines(Full(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private IEnumerable<string> Directory_(string path)
    {
        try { return Directory.EnumerateDirectories(Full(path)).Select(d => path.TrimEnd('/') + "/" + Path.GetFileName(d)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, Inv, out var v) ? v : null;

    private static string? Field(string line, string key)
    {
        var colon = line.IndexOf(':');
        return colon > 0 && line[..colon].Trim().Equals(key, StringComparison.Ordinal) ? line[(colon + 1)..].Trim() : null;
    }

    private List<Chip> HwmonChips() =>
        Directory_("/sys/class/hwmon")
            .Select(d => new Chip(d, Text(d + "/name") ?? ""))
            .Where(c => c.Name.Length > 0)
            .OrderBy(c => c.Dir, StringComparer.Ordinal)
            .ToList();

    /// <summary>The <c>{kind}N_input</c> files of a chip (temp1_input, fan2_input...) as (N, path), in order.</summary>
    private IEnumerable<(int Index, string File)> HwmonInputs(Chip chip, string kind)
    {
        var found = new List<(int, string)>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(Full(chip.Dir), kind + "*_input"))
            {
                var name = Path.GetFileName(file);
                var digits = name[kind.Length..^"_input".Length];
                if (int.TryParse(digits, out var index)) found.Add((index, chip.Dir + "/" + name));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return found.OrderBy(f => f.Item1);
    }

    // ================================================================== result building

    private sealed class Builder(string computer)
    {
        private readonly Dictionary<string, int> _counts = new();
        private readonly HashSet<string> _listed = new();

        public string Computer { get; } = computer;
        public List<HardwareItem> Hardware { get; } = [];
        public List<LhmSensor> Sensors { get; } = [];

        public void Add(HardwareItem hw, string? chip, string group, string name, string type, double? value, string unit)
        {
            if (value is not { } v || !double.IsFinite(v)) return;
            if (_listed.Add(hw.Id)) Hardware.Add(hw);

            var key = $"{hw.Id}/{type.ToLowerInvariant()}";
            var n = _counts.GetValueOrDefault(key);
            _counts[key] = n + 1;

            var path = chip is null ? $"{Computer}/{hw.Name}/{group}/{name}" : $"{Computer}/{hw.Name}/{chip}/{group}/{name}";
            Sensors.Add(new LhmSensor
            {
                Id = $"{key}/{n}", Name = name, Type = type, Value = v, Unit = unit, DisplayValue = Format(v, unit),
                HardwareId = hw.Id, HardwareName = hw.Name, Hardware = hw.Kind, Group = group, Path = path,
            });
        }

        private static string Format(double v, string unit) => unit switch
        {
            "°C" => $"{v.ToString("0.0", Inv)} °C",
            "%" => $"{v.ToString("0.0", Inv)} %",
            "MHz" => $"{v.ToString("0.0", Inv)} MHz",
            "RPM" => $"{v.ToString("0", Inv)} RPM",
            "W" => $"{v.ToString("0.0", Inv)} W",
            "V" => $"{v.ToString("0.000", Inv)} V",
            "GB" => $"{v.ToString("0.0", Inv)} GB",
            "MB" => $"{v.ToString("0", Inv)} MB",
            "B/s" => v >= 1048576 ? $"{(v / 1048576).ToString("0.0", Inv)} MB/s" : v >= 1024 ? $"{(v / 1024).ToString("0.0", Inv)} KB/s" : $"{v.ToString("0", Inv)} B/s",
            _ => v.ToString("0.##", Inv),
        };
    }
}
