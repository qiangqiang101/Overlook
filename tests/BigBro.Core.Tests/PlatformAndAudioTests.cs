using System.IO.MemoryMappedFiles;
using BigBro.Core.Audio;
using BigBro.Core.Platform;
using BigBro.Core.Settings;

namespace BigBro.Core.Tests;

public class PlatformAndAudioTests
{
    [Fact]
    public void Afterburner_block_is_parsed()
    {
        var block = AfterburnerReader.BuildBlock(144.5, 6.9);
        using var mmf = MemoryMappedFile.CreateNew(null, block.Length);
        using (var w = mmf.CreateViewAccessor()) w.WriteArray(0, block, 0, block.Length);
        using var r = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        var reading = AfterburnerReader.Parse(r);

        Assert.NotNull(reading);
        Assert.Equal(144.5, reading!.Value.Fps, 3);
        Assert.Equal(6.9, reading.Value.FrameTimeMs, 3);
    }

    [Fact]
    public void Afterburner_without_frametime_counter_derives_it_from_fps()
    {
        var block = AfterburnerReader.BuildBlock(125, 0, frameTimeEntry: false);
        using var stream = new MemoryStream(block);
        using var map = MemoryMappedFile.CreateNew(null, block.Length);
        using (var w = map.CreateViewAccessor()) w.WriteArray(0, block, 0, block.Length);
        using var r = map.CreateViewAccessor(0, block.Length, MemoryMappedFileAccess.Read);

        var reading = AfterburnerReader.Parse(r);
        Assert.Equal(125, reading!.Value.Fps);
        Assert.Equal(8.0, reading.Value.FrameTimeMs, 3);
    }

    [Fact]
    public void Afterburner_block_with_wrong_signature_or_no_counters_is_ignored()
    {
        var block = AfterburnerReader.BuildBlock(60, 16);
        block[0] = 0;
        using var mmf = MemoryMappedFile.CreateNew(null, block.Length);
        using (var w = mmf.CreateViewAccessor()) w.WriteArray(0, block, 0, block.Length);
        using var r = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        Assert.Null(AfterburnerReader.Parse(r));
    }

    [Fact]
    public void Afterburner_no_value_sentinel_reads_as_zero()
    {
        var block = AfterburnerReader.BuildBlock(float.MaxValue, float.MaxValue);
        using var mmf = MemoryMappedFile.CreateNew(null, block.Length);
        using (var w = mmf.CreateViewAccessor()) w.WriteArray(0, block, 0, block.Length);
        using var r = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        Assert.Equal((0.0, 0.0), AfterburnerReader.Parse(r));
    }

    [Fact]
    public void Afterburner_live_read_never_throws()
    {
        // Whether or not Afterburner is installed here, a read is either null or sane numbers.
        var reading = new AfterburnerReader().Read();
        if (reading is { } v) Assert.InRange(v.Fps, 0, 100_000);
    }

    [Theory]
    [InlineData(440)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void A_sine_lights_up_the_band_that_contains_it(double hz)
    {
        var analyzer = new SpectrumAnalyzer { SampleRate = 48_000 };
        var samples = new float[SpectrumAnalyzer.FftSize * 2];            // stereo
        for (var i = 0; i < SpectrumAnalyzer.FftSize; i++)
            samples[2 * i] = samples[2 * i + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / 48_000));
        analyzer.AddSamples(samples, 2);
        analyzer.Update();

        const int bars = 32;
        var bands = analyzer.Bands(bars, useAverage: false, ScalingStrategy.Decibel);
        var expected = (int)(bars * Math.Log(hz / 20) / Math.Log(1000));
        var loudest = Array.IndexOf(bands, bands.Max());

        Assert.InRange(loudest, expected - 1, expected + 1);
        Assert.True(bands[loudest] > 0.8f, $"peak band only {bands[loudest]}");
        Assert.True(bands[(loudest + bars / 2) % bars] < bands[loudest] * 0.7f);
    }

    [Fact]
    public void Silence_and_too_little_data_give_flat_bars()
    {
        var analyzer = new SpectrumAnalyzer();
        analyzer.Update();
        Assert.All(analyzer.Bands(16, true, ScalingStrategy.Linear), v => Assert.Equal(0, v));
        Assert.False(analyzer.HasData);

        analyzer.AddSamples(new float[SpectrumAnalyzer.FftSize * 2], 2);
        analyzer.Update();
        Assert.True(analyzer.HasData);
        Assert.All(analyzer.Bands(16, false, ScalingStrategy.Decibel), v => Assert.Equal(0, v));
    }

    [Fact]
    public void Localization_falls_back_to_english_for_missing_files_and_keys()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bigbro-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Test.json"), """{ "a": "Alfa" }""");
        try
        {
            Assert.Contains("Test", Loc.Available(dir));
            Loc.Load("Test", dir);
            Assert.Equal("Alfa", Loc.T("a", "A"));
            Assert.Equal("B", Loc.T("b", "B"));
            Assert.Equal("Hi Bob", Loc.T("c", "Hi {0}", "Bob"));

            Loc.Load("Nope", dir);
            Assert.Equal("English", Loc.Current);
            Assert.Equal("A", Loc.T("a", "A"));
        }
        finally
        {
            Loc.Load("English");
            Directory.Delete(dir, true);
        }
    }
}

public class DisplayTests
{
    [Fact]
    public void Refresh_rate_is_read_once_and_cached()
    {
        var local = new BigBro.Core.Sensors.LocalInfo();
        var first = local.RefreshRate;
        Assert.InRange(first, 0, 1000);
        if (OperatingSystem.IsWindows() && Environment.UserInteractive) Assert.True(first >= 24, $"expected a real refresh rate, got {first}");
        Assert.Equal(first, local.RefreshRate);
    }
}

public class SensorPathTests
{
    private static BigBro.Core.Sensors.SensorSnapshot Snap() => LhmParserTests.LoadFixture();
    private static BigBro.Core.Sensors.SensorResolver Res() => new(Snap(), new BigBro.Core.Sensors.LocalInfo());

    [Fact]
    public void Sensors_have_the_path_shown_in_the_lhm_tree()
    {
        var s = Snap();
        Assert.Equal("ROGPC2", s.ComputerName);
        Assert.Equal("ROGPC2/Intel Core i5-14600K/Load/CPU Total", s.ById("/intelcpu/0/load/0")!.Path);
        // a motherboard's chip is a level of its own
        Assert.Equal("ROGPC2/ASUS ROG STRIX B760-F GAMING WIFI/Nuvoton NCT6798D/Fans/Fan #2", s.ById("/lpc/nct6798d/0/fan/1")!.Path);
    }

    [Fact]
    public void Lhm_binding_reads_by_path_and_still_by_id()
    {
        var r = Res();
        Assert.Equal("17 %", r.Resolve(BigBro.Core.Theming.SensorKind.Lhm, "ROGPC2/Intel Core i5-14600K/Load/CPU Total").Text);
        Assert.Equal(17, r.Resolve(BigBro.Core.Theming.SensorKind.Lhm, "ROGPC2/Intel Core i5-14600K/Load/CPU Total").Number);
        Assert.Equal(17, r.Resolve(BigBro.Core.Theming.SensorKind.Lhm, "/intelcpu/0/load/0").Number);
    }

    [Fact]
    public void A_theme_made_on_another_pc_still_finds_its_sensors()
        => Assert.Equal(17, Res().Resolve(BigBro.Core.Theming.SensorKind.Lhm, "MYOTHERPC/Intel Core i5-14600K/Load/CPU Total").Number);

    [Fact]
    public void Unknown_path_reads_as_nothing()
        => Assert.Equal(BigBro.Core.Sensors.SensorReading.None, Res().Resolve(BigBro.Core.Theming.SensorKind.Lhm, "ROGPC2/Nope/Load/Nothing"));
}

public class RgbTests
{
    private static byte[] Frame(int index, int count, params byte[] rgb) => [0, (byte)index, (byte)count, .. rgb];

[Fact]
    public void A_frame_split_over_two_packets_is_assembled_in_order()
    {
        var source = new BigBro.Core.Rgb.RgbSource(new BigBro.Core.Rgb.RgbSettings());

        source.Process(Frame(0, 2, 255, 0, 0, 0, 255, 0));                       // red, green
        Assert.Empty(source.Frame);                                              // not complete yet
        source.Process(Frame(1, 2, 0, 0, 255, 255, 255, 255));                   // blue, white

        Assert.Equal(4, source.Frame.Length);
        Assert.Equal(new BigBro.Core.Theming.ThemeColor(255, 255, 0, 0), source.Frame[0]);
        Assert.Equal(new BigBro.Core.Theming.ThemeColor(255, 0, 0, 255), source.Frame[2]);
        Assert.Equal(new BigBro.Core.Theming.ThemeColor(255, 255, 255, 255), source.Frame[3]);
    }

    [Fact]
    public void A_lost_packet_does_not_corrupt_the_next_frame()
    {
        var source = new BigBro.Core.Rgb.RgbSource(new BigBro.Core.Rgb.RgbSettings());

        source.Process(Frame(0, 3, 9, 9, 9, 9, 9, 9));
        // packets 1 and 2 are lost; the next frame starts over
        source.Process(Frame(0, 2, 1, 2, 3, 4, 5, 6));
        source.Process(Frame(1, 2, 7, 8, 9, 10, 11, 12));

        Assert.Equal(4, source.Frame.Length);
        Assert.Equal(new BigBro.Core.Theming.ThemeColor(255, 1, 2, 3), source.Frame[0]);
    }

    [Fact]
    public void A_settings_packet_updates_the_settings_and_raises_the_event()
    {
        var settings = new BigBro.Core.Rgb.RgbSettings();
        var source = new BigBro.Core.Rgb.RgbSource(settings);
        var raised = 0;
        source.SettingsChanged += () => raised++;

        var path = System.Text.Encoding.UTF8.GetBytes("/covers/diffuser.png");
        byte[] packet =
        [
            1, 8 /*Landscape21_9*/, 3 /*XLarge*/, 12 /*Fire*/, 1, 15 /*blur*/, 2 /*Sphere*/, 6 /*radius*/, 2 /*padding*/, 30 /*fps*/, 3 /*UniformToFill*/,
            70, 10, 20, 30, /*background*/ 40, 50, 60, /*shutdown*/ (byte)path.Length, .. path,
        ];
        source.Process(packet);

        Assert.Equal(1, raised);
        Assert.Equal(BigBro.Core.Rgb.MatrixSizeType.Landscape21_9, settings.MatrixSizeType);
        Assert.Equal(BigBro.Core.Rgb.MatrixSizeTier.XLarge, settings.MatrixSizeTier);
        Assert.Equal(BigBro.Core.Rgb.OfflineEffect.Fire, settings.OfflineEffect);
        Assert.Equal((15, 6, 30), (settings.BlurIntensity, settings.RoundedRectangleCornerRadius, settings.Fps));
        Assert.Equal(BigBro.Core.Rgb.LedShape.Sphere, settings.LedShape);
        Assert.Equal(BigBro.Core.Rgb.CoverStretch.UniformToFill, settings.CoverImageStretch);
        Assert.Equal(new BigBro.Core.Theming.ThemeColor(255, 10, 20, 30), settings.BackgroundColor);
        Assert.Equal(new BigBro.Core.Theming.ThemeColor(255, 40, 50, 60), settings.ShutdownColor);
        Assert.Equal("/covers/diffuser.png", settings.CoverImage);
    }

    [Fact]
    public void Garbage_and_short_packets_are_ignored()
    {
        var source = new BigBro.Core.Rgb.RgbSource(new BigBro.Core.Rgb.RgbSettings());
        source.Process([]);
        source.Process([0, 0, 1]);
        source.Process([9, 9, 9, 9, 9, 9, 9, 9, 9]);
        source.Process([1, 99, 99, 99, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);     // out-of-range enums
        Assert.Empty(source.Frame);
    }

    [Theory]
    [InlineData(BigBro.Core.Rgb.MatrixSizeType.Landscape16_9, BigBro.Core.Rgb.MatrixSizeTier.Normal, 48, 27)]
    [InlineData(BigBro.Core.Rgb.MatrixSizeType.Portrait16_9, BigBro.Core.Rgb.MatrixSizeTier.Normal, 27, 48)]
    [InlineData(BigBro.Core.Rgb.MatrixSizeType.Landscape32_9, BigBro.Core.Rgb.MatrixSizeTier.XLarge, 256, 72)]
    [InlineData(BigBro.Core.Rgb.MatrixSizeType.Portrait4_1, BigBro.Core.Rgb.MatrixSizeTier.Small, 2, 8)]
    [InlineData(BigBro.Core.Rgb.MatrixSizeType.Portrait21_9, BigBro.Core.Rgb.MatrixSizeTier.Large, 36, 48)]   // the plugin's own table
    public void Matrix_sizes_match_the_plugin(BigBro.Core.Rgb.MatrixSizeType type, BigBro.Core.Rgb.MatrixSizeTier tier, int w, int h)
        => Assert.Equal((w, h), BigBro.Core.Rgb.RgbMatrix.Size(type, tier));

    [Fact]
    public void Every_matrix_size_exists()
    {
        foreach (var type in Enum.GetValues<BigBro.Core.Rgb.MatrixSizeType>())
            foreach (var tier in Enum.GetValues<BigBro.Core.Rgb.MatrixSizeTier>())
            {
                var (w, h) = BigBro.Core.Rgb.RgbMatrix.Size(type, tier);
                Assert.True(w > 0 && h > 0);
            }
    }

    [Fact]
    public void Every_offline_effect_fills_the_frame_with_opaque_colours()
    {
        var analyzer = new BigBro.Core.Audio.SpectrumAnalyzer();
        var effects = new BigBro.Core.Rgb.RgbEffects();
        foreach (var effect in Enum.GetValues<BigBro.Core.Rgb.OfflineEffect>())
        {
            var frame = new BigBro.Core.Theming.ThemeColor[16 * 9];
            for (var i = 0; i < 3; i++) effects.Render(effect, frame, 16, 9, new BigBro.Core.Theming.ThemeColor(255, 200, 50, 50), analyzer);
            Assert.All(frame, c => Assert.Equal(255, c.A));
            if (effect != BigBro.Core.Rgb.OfflineEffect.Breathing)                 // breathing is dark at the bottom of each pulse
                Assert.Contains(frame, c => c.R > 0 || c.G > 0 || c.B > 0);
        }
    }

    [Fact]
    public void Solid_effect_uses_the_chosen_colour()
    {
        var frame = new BigBro.Core.Theming.ThemeColor[4];
        new BigBro.Core.Rgb.RgbEffects().Render(BigBro.Core.Rgb.OfflineEffect.SolidColor, frame, 2, 2, new BigBro.Core.Theming.ThemeColor(255, 1, 2, 3));
        Assert.All(frame, c => Assert.Equal(new BigBro.Core.Theming.ThemeColor(255, 1, 2, 3), c));
    }

    [Fact]
    public void Two_sockets_can_share_the_port_so_the_wallpaper_and_the_panel_both_receive()
    {
        var port = 38000 + Random.Shared.Next(1000);
        using var a = new BigBro.Core.Rgb.RgbSource(new BigBro.Core.Rgb.RgbSettings());
        using var b = new BigBro.Core.Rgb.RgbSource(new BigBro.Core.Rgb.RgbSettings());
        a.Start(port);
        b.Start(port);
        Assert.Null(a.Error);
        Assert.Null(b.Error);
        Assert.True(a.IsListening && b.IsListening);
    }
}
