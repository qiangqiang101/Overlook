using BigBro.Core.Sensors;

namespace BigBro.Core.Tests;

public sealed class LinuxSensorReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"overlook-linux-{Guid.NewGuid():N}");
    private readonly string _host = Environment.MachineName;

    public LinuxSensorReaderTests()
    {
        Put("proc/cpuinfo", "processor\t: 0\nmodel name\t: AMD Ryzen 7 5800X 8-Core Processor\ncpu MHz\t\t: 3800.000\n");
        Put("proc/stat", "cpu  100 0 100 800 0 0 0 0 0 0\ncpu0 100 0 100 800 0 0 0 0 0 0\nintr 1\n");
        Put("proc/meminfo", "MemTotal:       16000000 kB\nMemFree:         1000000 kB\nMemAvailable:    4000000 kB\nSwapTotal:       2000000 kB\nSwapFree:        1500000 kB\n");
        Put("proc/net/dev", "Inter-|   Receive\n face |bytes packets errs drop fifo frame compressed multicast|bytes packets\n" +
            "    lo: 5000 1 0 0 0 0 0 0 5000 1 0 0 0 0 0 0\n  eth0: 1000000 10 0 0 0 0 0 0 500000 10 0 0 0 0 0 0\n docker0: 9 1 0 0 0 0 0 0 9 1 0 0 0 0 0 0\n");
        Put("sys/class/net/eth0/speed", "1000\n");
        Put("sys/class/dmi/id/board_name", "ROG STRIX B550-F\n");

        Put("sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq", "4200000\n");

        Put("sys/class/hwmon/hwmon0/name", "k10temp\n");
        Put("sys/class/hwmon/hwmon0/temp1_input", "45500\n");
        Put("sys/class/hwmon/hwmon0/temp1_label", "Tctl\n");

        Put("sys/class/hwmon/hwmon1/name", "nct6798\n");
        Put("sys/class/hwmon/hwmon1/temp1_input", "36000\n");
        Put("sys/class/hwmon/hwmon1/temp1_label", "SYSTIN\n");
        Put("sys/class/hwmon/hwmon1/fan1_input", "1200\n");
        Put("sys/class/hwmon/hwmon1/in0_input", "1008\n");

        Put("sys/class/hwmon/hwmon2/name", "nvme\n");
        Put("sys/class/hwmon/hwmon2/device/model", "Samsung SSD 980 PRO\n");
        Put("sys/class/hwmon/hwmon2/temp1_input", "41000\n");
        Put("sys/class/hwmon/hwmon2/temp1_label", "Composite\n");

        Put("sys/class/hwmon/hwmon3/name", "amdgpu\n");
        Put("sys/class/hwmon/hwmon3/temp1_input", "60000\n");
        Put("sys/class/hwmon/hwmon3/temp1_label", "edge\n");
        Put("sys/class/hwmon/hwmon3/power1_average", "120000000\n");
        Put("sys/class/hwmon/hwmon3/fan1_input", "900\n");
        Put("sys/class/hwmon/hwmon3/device/product_name", "Radeon RX 6800\n");
        Put("sys/class/hwmon/hwmon3/device/gpu_busy_percent", "35\n");
        Put("sys/class/hwmon/hwmon3/device/mem_info_vram_used", "1073741824\n");
        Put("sys/class/hwmon/hwmon3/device/mem_info_vram_total", "8589934592\n");
    }

    private void Put(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private double? Value(SensorSnapshot s, string path) => s.ByPath(path)?.Value;

    [Fact]
    public void Reads_cpu_board_drive_gpu_memory_and_network()
    {
        var reader = new LinuxSensorReader(_root, () => null);
        var s = reader.Read();
        var cpu = $"{_host}/AMD Ryzen 7 5800X 8-Core Processor";

        Assert.Equal(45.5, Value(s, $"{cpu}/Temperatures/Core (Tctl/Tdie)"));
        Assert.Equal(4200, Value(s, $"{cpu}/Clocks/CPU Core #1"));
        Assert.NotNull(s.ByPath($"{cpu}/Load/CPU Total"));

        Assert.Equal(36, Value(s, $"{_host}/ROG STRIX B550-F/nct6798/Temperatures/SYSTIN"));
        Assert.Equal(1200, Value(s, $"{_host}/ROG STRIX B550-F/nct6798/Fans/Fan #1"));
        Assert.Equal(1.008, Value(s, $"{_host}/ROG STRIX B550-F/nct6798/Voltages/Voltage #0")!.Value, 3);

        Assert.Equal(41, Value(s, $"{_host}/Samsung SSD 980 PRO/Temperatures/Temperature"));

        var gpu = $"{_host}/Radeon RX 6800";
        Assert.Equal(60, Value(s, $"{gpu}/Temperatures/GPU Core"));
        Assert.Equal(35, Value(s, $"{gpu}/Load/GPU Core"));
        Assert.Equal(120, Value(s, $"{gpu}/Powers/GPU Package"));
        Assert.Equal(1024, Value(s, $"{gpu}/Data/GPU Memory Used"));
        Assert.Equal(12.5, Value(s, $"{gpu}/Load/GPU Memory")!.Value, 3);

        // 16,000,000 kB total, 4,000,000 kB available
        Assert.Equal(75, Value(s, $"{_host}/Generic Memory/Load/Memory"));
        Assert.Equal(11.444, Value(s, $"{_host}/Generic Memory/Data/Memory Used")!.Value, 3);
        Assert.Equal(25, Value(s, $"{_host}/Virtual Memory/Load/Virtual Memory"));

        Assert.NotNull(s.ByPath($"{_host}/eth0/Throughput/Download Speed"));
        Assert.Null(s.ByPath($"{_host}/lo/Throughput/Download Speed"));
        Assert.Null(s.ByPath($"{_host}/docker0/Throughput/Download Speed"));

        Assert.Equal(HardwareKind.Cpu, s.HardwareAt(HardwareKind.Cpu)!.Kind);
        Assert.Equal(HardwareKind.Gpu, s.HardwareAt(HardwareKind.Gpu)!.Kind);
        Assert.Equal(HardwareKind.Storage, s.HardwareAt(HardwareKind.Storage)!.Kind);
        Assert.Equal(HardwareKind.Network, s.HardwareAt(HardwareKind.Network)!.Kind);
    }

    [Fact]
    public void Load_and_throughput_come_from_the_difference_between_two_reads()
    {
        var reader = new LinuxSensorReader(_root, () => null);
        var first = reader.Read();
        var cpu = $"{_host}/AMD Ryzen 7 5800X 8-Core Processor/Load/CPU Total";
        Assert.Equal(0, Value(first, cpu));                                      // nothing to compare with yet

        // 1000 more jiffies, 500 of them idle: half busy
        Put("proc/stat", "cpu  350 0 350 1300 0 0 0 0 0 0\ncpu0 350 0 350 1300 0 0 0 0 0 0\n");
        Put("proc/net/dev", "Inter-|   Receive\n face |bytes\n  eth0: 3000000 20 0 0 0 0 0 0 1500000 20 0 0 0 0 0 0\n");
        Thread.Sleep(50);

        var second = reader.Read();
        Assert.Equal(50, Value(second, cpu)!.Value, 1);
        Assert.True(Value(second, $"{_host}/eth0/Throughput/Download Speed") > 0);
        Assert.True(Value(second, $"{_host}/eth0/Throughput/Upload Speed") > 0);
    }

    [Fact]
    public void Nvidia_smi_output_becomes_a_gpu()
    {
        const string csv = "NVIDIA GeForce RTX 4070, 55, 12, 7, 1800, 9501, 33, 80.50, 2048, 12282\n";
        var s = new LinuxSensorReader(_root, () => csv).Read();
        var gpu = $"{_host}/NVIDIA GeForce RTX 4070";

        Assert.Equal(55, Value(s, $"{gpu}/Temperatures/GPU Core"));
        Assert.Equal(12, Value(s, $"{gpu}/Load/GPU Core"));
        Assert.Equal(1800, Value(s, $"{gpu}/Clocks/GPU Core"));
        Assert.Equal(33, Value(s, $"{gpu}/Controls/GPU Fan"));
        Assert.Equal(80.5, Value(s, $"{gpu}/Powers/GPU Package"));
        Assert.Equal(2048, Value(s, $"{gpu}/Data/GPU Memory Used"));
    }

    [Fact]
    public void Not_available_fields_are_left_out()
    {
        const string csv = "Some GPU, 40, 5, [N/A], 300, 700, [Not Supported], [N/A], 100, 4096\n";
        var s = new LinuxSensorReader(_root, () => csv).Read();
        var gpu = $"{_host}/Some GPU";

        Assert.Equal(40, Value(s, $"{gpu}/Temperatures/GPU Core"));
        Assert.Null(s.ByPath($"{gpu}/Controls/GPU Fan"));
        Assert.Null(s.ByPath($"{gpu}/Powers/GPU Package"));
    }

    [Fact]
    public void An_empty_or_unreadable_system_gives_an_empty_snapshot_not_an_exception()
    {
        var s = new LinuxSensorReader(Path.Combine(_root, "nothing-here"), () => null).Read();
        Assert.True(s.IsEmpty);
    }

    [Fact]
    public void Sensor_ids_are_unique()
    {
        var s = new LinuxSensorReader(_root, () => "GPU, 1, 2, 3, 4, 5, 6, 7, 8, 9\n").Read();
        Assert.Equal(s.Sensors.Count, s.Sensors.Select(x => x.Id).Distinct().Count());
    }
}
