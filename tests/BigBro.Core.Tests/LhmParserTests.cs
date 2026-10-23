using BigBro.Core.Sensors;

namespace BigBro.Core.Tests;

public class LhmParserTests
{
    internal static SensorSnapshot LoadFixture()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lhm-data.json"));
        return LhmParser.Parse(stream);
    }

    [Fact]
    public void Parses_hardware_in_tree_order_with_kinds()
    {
        var s = LoadFixture();

        Assert.Equal(HardwareKind.Motherboard, s.HardwareAt(HardwareKind.Motherboard)!.Kind);
        Assert.Equal("Intel Core i5-14600K", s.HardwareAt(HardwareKind.Cpu)!.Name);
        Assert.Equal("NVIDIA GeForce RTX 4060", s.HardwareAt(HardwareKind.Gpu)!.Name);
        Assert.Equal(3, s.Hardware.Count(h => h.Kind == HardwareKind.Storage));
        Assert.Equal(2, s.Hardware.Count(h => h.Kind == HardwareKind.Dimm));
        Assert.Equal(7, s.Hardware.Count(h => h.Kind == HardwareKind.Network));
        Assert.Null(s.HardwareAt(HardwareKind.Gpu, 1));
    }

    [Fact]
    public void Super_io_sensors_belong_to_the_motherboard()
    {
        var s = LoadFixture();
        var fan = s.ById("/lpc/nct6798d/0/fan/1");

        Assert.NotNull(fan);
        Assert.Equal(HardwareKind.Motherboard, fan!.Hardware);
        Assert.Equal("/motherboard", fan.HardwareId);
        Assert.Equal("Fans", fan.Group);
        Assert.Equal(1001, fan.Value);
        Assert.Equal("RPM", fan.Unit);
    }

    [Fact]
    public void Temperature_unit_comes_from_the_type_not_the_garbled_text()
    {
        var t = LoadFixture().ById("/intelcpu/0/temperature/16")!;

        Assert.Equal("CPU Package", t.Name);
        Assert.Equal(46.0, t.Value);
        Assert.Equal("°C", t.Unit);
    }

    [Theory]
    [InlineData("1.160 V", 1.16, "V")]
    [InlineData("5291.5 MHz", 5291.5, "MHz")]
    [InlineData("1630150000.0 B/s", 1630150000.0, "B/s")]
    [InlineData("2.993", 2.993, "")]
    [InlineData("-10.0 �C", -10.0, "°C")]
    [InlineData("1291,5 MHz", 1291.5, "MHz")]   // server running with a comma-decimal locale
    [InlineData("1,291.5 MHz", 1291.5, "MHz")]
    public void ParseRaw_handles_the_formats_lhm_emits(string raw, double value, string unit)
    {
        var type = unit == "°C" ? "Temperature" : "Other";
        var (v, u) = LhmParser.ParseRaw(raw, type);

        Assert.Equal(value, v);
        Assert.Equal(unit, u);
    }

    [Fact]
    public void ParseRaw_gives_null_for_empty_values()
    {
        Assert.Null(LhmParser.ParseRaw("", "Load").Value);
        Assert.Null(LhmParser.ParseRaw("-", "Load").Value);
    }

    [Fact]
    public void Empty_or_unexpected_json_yields_an_empty_snapshot()
    {
        Assert.True(LhmParser.Parse("{}").IsEmpty);
        Assert.True(LhmParser.Parse("""{"id":0,"Text":"Sensor","Children":[]}""").IsEmpty);
    }

    [Theory]
    [InlineData("192.168.0.8:8085", "http://192.168.0.8:8085/data.json")]
    [InlineData("http://192.168.0.8:8085", "http://192.168.0.8:8085/data.json")]
    [InlineData("http://192.168.0.8:8085/", "http://192.168.0.8:8085/data.json")]
    [InlineData("  http://pc.local:8085/data.json ", "http://pc.local:8085/data.json")]
    [InlineData("https://host/custom/path.json", "https://host/custom/path.json")]
    public void Url_normalization(string input, string expected)
    {
        Assert.True(LhmClient.TryNormalizeUrl(input, out var url));
        Assert.Equal(expected, url.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://host/data.json")]
    [InlineData("not a url at all")]
    public void Url_normalization_rejects_garbage(string input) =>
        Assert.False(LhmClient.TryNormalizeUrl(input, out _));
}
