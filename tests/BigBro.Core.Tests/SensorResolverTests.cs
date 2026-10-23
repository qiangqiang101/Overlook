using BigBro.Core.Legacy;
using BigBro.Core.Sensors;
using BigBro.Core.Theming;

namespace BigBro.Core.Tests;

/// <summary>Checked against a real data.json from an i5-14600K / RTX 4060 / NCT6798D machine.</summary>
public class SensorResolverTests
{
    private const string Cpu = "ROGPC2/Intel Core i5-14600K/";
    private const string Gpu = "ROGPC2/NVIDIA GeForce RTX 4060/";
    private const string Chip = "ROGPC2/ASUS ROG STRIX B760-F GAMING WIFI/Nuvoton NCT6798D/";

    private static SensorResolver Resolver() =>
        new(LhmParserTests.LoadFixture(), new LocalInfo(), WeatherData.Unknown);

    private static SensorReading Path(string path) => Resolver().Resolve(SensorKind.Lhm, path);
    private static SensorReading R(SensorKind kind, string? param = null) => Resolver().Resolve(kind, param);

    [Fact]
    public void Percentages_clocks_and_fans_round_up_like_1x()
    {
        var load = Path(Cpu + "Load/CPU Total");                    // 16.3 %
        Assert.Equal("17 %", load.Text);
        Assert.Equal("17", load.Value);
        Assert.Equal(17, load.Number);
        Assert.Equal(100, load.Max);
        Assert.Equal(0.17, load.Fraction);

        Assert.Equal("5292 MHz", Path(Cpu + "Clocks/CPU Core #1").Text);    // 5291.5
        Assert.Null(Path(Cpu + "Clocks/CPU Core #1").Max);                   // no natural maximum: bars track the peak
        Assert.Equal("1001 RPM", Path(Chip + "Fans/Fan #2").Text);
        Assert.Equal("26 %", Path(Gpu + "Load/GPU Memory").Text);            // 25.5
    }

    [Fact]
    public void Temperature_shows_a_decimal_only_when_there_is_one()
    {
        var t = Path(Cpu + "Temperatures/CPU Package");
        Assert.Equal("46 °C", t.Text);
        Assert.Equal("46", t.Value);
        Assert.Equal(46, t.Number);
        Assert.Equal(120, t.Max);
        Assert.Equal("43.2 °C", Path(Cpu + "Temperatures/Core Average").Text);
    }

    [Fact]
    public void Power_has_two_decimals()
    {
        var p = Path(Cpu + "Powers/CPU Package");
        Assert.Equal("69.80 W", p.Text);
        Assert.Equal("69.8", p.Value);
    }

    [Fact]
    public void Memory_sizes_round_up_to_whole_units()
    {
        Assert.Equal("2089 MB", Path(Gpu + "Data/GPU Memory Used").Text);
        Assert.Equal("6098 MB", Path(Gpu + "Data/GPU Memory Free").Text);
        Assert.Equal("14 GB", Path("ROGPC2/Total Memory/Data/Memory Used").Text);
        Assert.Equal("18 GB", Path("ROGPC2/Total Memory/Data/Memory Available").Text);     // 17.7
    }

    [Fact]
    public void Throughput_is_scaled_and_exposes_kilobytes_per_second()
    {
        var down = Path("ROGPC2/Ethernet/Throughput/Download Speed");
        Assert.Equal("9.56 KB/s", down.Text);
        Assert.Equal(9.56, down.Number);
        Assert.Equal("38.22 MB/s", Path("ROGPC2/KINGSTON SKC3000D2048G/Throughput/Read Rate").Text);
    }

    [Fact]
    public void Unitless_sensors_show_the_plain_number()
        => Assert.Equal("2.993", Path("ROGPC2/KINGSTON SUV400S37480G/Factors/Write Amplification").Text);

    [Fact]
    public void Paths_and_ids_find_the_same_sensor()
    {
        Assert.Equal(46, Path(Cpu + "Temperatures/CPU Package").Number);
        Assert.Equal(46, R(SensorKind.Lhm, "/intelcpu/0/temperature/16").Number);
    }

    [Fact]
    public void A_theme_made_on_another_pc_still_finds_its_sensors()
        => Assert.Equal(46, Path("MYOTHERPC/Intel Core i5-14600K/Temperatures/CPU Package").Number);

    [Fact]
    public void Unknown_or_missing_paths_read_as_nothing()
    {
        Assert.Equal(SensorReading.None, Path("ROGPC2/Nope/Load/Nothing"));
        Assert.Equal(SensorReading.None, R(SensorKind.Lhm, null));
        Assert.Equal(SensorReading.None, R(SensorKind.Lhm, "  "));
    }

    [Fact]
    public void Core_count_and_memory_totals()
    {
        Assert.Equal(14, R(SensorKind.CpuCoreCount).Number);
        var total = R(SensorKind.RamTotal);
        Assert.Equal("32 GB", total.Text);                                       // 14 + 18, each rounded up
        Assert.Equal("45 GB", R(SensorKind.RamVirtualTotal).Text);
    }

    [Fact]
    public void Ram_speed_is_derived_from_spd_cycle_time()
        => Assert.Equal("4800 MHz", R(SensorKind.RamClockSpeed).Text);

    [Theory]
    [InlineData(0, "0.00 KB/s")]
    [InlineData(1536, "1.50 KB/s")]
    [InlineData(1048576, "1.00 MB/s")]
    [InlineData(1630150000, "1.52 GB/s")]
    public void Speed_text_scales_units(double bytesPerSecond, string expected)
        => Assert.Equal(expected, SensorResolver.SpeedText(bytesPerSecond));

    [Fact]
    public void Custom_date_time_format_and_fallback()
    {
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", R(SensorKind.CustomDateTime, "yyyy-MM-dd").Text);
        Assert.NotEmpty(R(SensorKind.CustomDateTime, null).Text);
        Assert.NotEmpty(R(SensorKind.CustomDateTime, "%%%invalid\\").Text);
    }

    [Fact]
    public void Uptime_has_a_default_format_and_survives_a_bad_one()
    {
        Assert.Matches(@"^\d+\.\d+:\d{2}:\d{2}$", R(SensorKind.SystemUptime).Text);
        Assert.NotEmpty(R(SensorKind.SystemUptime, "zzz").Text);
    }

    [Fact]
    public void Weather_readings()
    {
        var weather = new WeatherData(21.5, 10, 90, 3, WeatherService.Describe(3));
        var r = new SensorResolver(SensorSnapshot.Empty, new LocalInfo(), weather);

        Assert.Equal("21.5 °C", r.Resolve(SensorKind.WeatherTempC, null).Text);
        Assert.Equal("70.7 °F", r.Resolve(SensorKind.WeatherTempF, null).Text);
        Assert.Equal("Cloudy", r.Resolve(SensorKind.WeatherName, null).Text);
        Assert.Equal(3, r.Resolve(SensorKind.WeatherCode, null).ImageIndex);
        Assert.Equal("10 Km/h", r.Resolve(SensorKind.WeatherWindSpeedKmh, null).Text);
    }

    [Fact]
    public void Every_sensor_kind_resolves_without_throwing()
    {
        foreach (var data in new[] { SensorSnapshot.Empty, LhmParserTests.LoadFixture() })
        {
            var r = new SensorResolver(data, new LocalInfo());
            foreach (var kind in Enum.GetValues<SensorKind>()) _ = r.Resolve(kind, null);
        }
    }

    [Fact]
    public void Detail_lists_list_each_name_once_with_units()
    {
        var (names, values) = Resolver().Detail(DetailGroup.GpuLoadPercent);
        var n = names.Split('\n');
        var v = values.Split('\n');

        Assert.Equal(n.Length, v.Length);
        Assert.Equal(n.Length, n.Distinct().Count());                              // "D3D Copy" appears once
        Assert.Equal("GPU Core", n[0]);
        Assert.Equal("69 %", v[0]);

        var (fanNames, fanValues) = Resolver().Detail(DetailGroup.MoboFan);
        Assert.Equal("Fan #1", fanNames.Split('\n')[0]);
        Assert.Equal("0 RPM", fanValues.Split('\n')[0]);

        Assert.Equal(("", ""), Resolver().Detail(DetailGroup.None));
    }

    // ---- suggestions made when a 1.x theme is imported

    [Theory]
    [InlineData(LegacySensorKind.CpuTemperatureC, null, Cpu + "Temperatures/CPU Package")]
    [InlineData(LegacySensorKind.CpuLoadPercent, null, Cpu + "Load/CPU Total")]
    [InlineData(LegacySensorKind.CpuClockSpeed, "3", Cpu + "Clocks/CPU Core #3")]
    [InlineData(LegacySensorKind.CpuPowerWattage, null, Cpu + "Powers/CPU Package")]
    [InlineData(LegacySensorKind.GpuLoadPercent, "0", Gpu + "Load/GPU Core")]
    [InlineData(LegacySensorKind.GpuVramUsage, null, Gpu + "Data/GPU Memory Used")]
    [InlineData(LegacySensorKind.GpuFrameBufferLoad, null, Gpu + "Load/GPU Memory Controller")]
    [InlineData(LegacySensorKind.CpuFan, "2", Chip + "Fans/Fan #2")]
    [InlineData(LegacySensorKind.MoboTemperatureC, null, Chip + "Temperatures/CPU Core")]
    [InlineData(LegacySensorKind.RamLoadPercent, null, "ROGPC2/Total Memory/Load/Memory")]
    [InlineData(LegacySensorKind.HddTemperatureC, "2", "ROGPC2/KINGSTON SKC3000D2048G/Temperatures/Temperature")]
    [InlineData(LegacySensorKind.DownloadSpeed, null, "ROGPC2/Ethernet/Throughput/Download Speed")]
    public void Legacy_sensors_get_a_suggested_path_on_this_pc(LegacySensorKind kind, string? param, string expected)
        => Assert.Equal(expected, Resolver().SensorFor(kind, param)?.Path);

    [Fact]
    public void No_suggestion_when_the_pc_lacks_the_hardware()
    {
        Assert.Null(Resolver().SensorFor(LegacySensorKind.GpuLoadPercent, "3"));
        Assert.Null(Resolver().SensorFor(LegacySensorKind.LongDate, null));
    }

    [Fact]
    public void Legacy_sensor_names_are_readable()
    {
        Assert.Equal("Gpu Temperature C (graphics card 1)", LegacySensors.Describe(LegacySensorKind.GpuTemperatureC, "1"));
        Assert.Equal("Cpu Load Percent", LegacySensors.Describe(LegacySensorKind.CpuLoadPercent, null));
    }
}
