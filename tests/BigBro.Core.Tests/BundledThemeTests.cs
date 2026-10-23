using BigBro.Core.Sensors;
using BigBro.Core.Theming;

namespace BigBro.Core.Tests;

/// <summary>The themes that ship with the app must load, look finished and work on any PC.</summary>
public class BundledThemeTests
{
    private static string ThemesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "BigBro.App", "Themes");
    }

    private static IReadOnlyList<ThemeEntry> Load(out List<string> errors)
    {
        var problems = new List<string>();
        var themes = ThemeStore.Scan(ThemesDirectory(), (path, ex) => problems.Add($"{path}: {ex.Message}"));
        errors = problems;
        return themes;
    }

    [Fact]
    public void Every_bundled_theme_loads()
    {
        var themes = Load(out var errors);
        Assert.Empty(errors);
        Assert.True(themes.Count >= 11, $"expected NVIDIA plus ten more, found {themes.Count}");
    }

    [Fact]
    public void Theme_names_and_element_names_are_unique()
    {
        var themes = Load(out _);
        Assert.Equal(themes.Count, themes.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var theme in themes)
        {
            var names = theme.Document.Flatten().Select(e => e.Name).ToList();
            Assert.True(names.Count == names.Distinct().Count(), $"{theme.Name} has duplicate element names");
        }
    }

    [Fact]
    public void Every_theme_has_a_preview_a_background_and_a_sensible_size()
    {
        foreach (var theme in Load(out _))
        {
            Assert.False(string.IsNullOrEmpty(theme.Document.Preview), $"{theme.Name} has no preview");
            Assert.False(string.IsNullOrEmpty(theme.Document.BackgroundImage), $"{theme.Name} has no background image");
            Assert.InRange(theme.Document.Width, 320, 4000);
            Assert.InRange(theme.Document.Height, 200, 4000);
            Assert.False(theme.Document.NeedsSensors, $"{theme.Name} still uses 1.x sensors");
        }
    }

    // the three display sizes the themes are made for, in both orientations: 8.8", 5" and 7" panels
    private static readonly HashSet<(int, int)> Sizes =
        [(1920, 480), (480, 1920), (800, 480), (480, 800), (1024, 600), (600, 1024)];

    [Fact]
    public void Bundled_themes_use_only_the_supported_display_sizes()
    {
        foreach (var theme in Load(out _))
            Assert.True(Sizes.Contains((theme.Document.Width, theme.Document.Height)),
                $"{theme.Name} is {theme.Document.Width}x{theme.Document.Height}, which is not 1920x480, 800x480 or 1024x600 (or rotated)");
    }

    [Fact]
    public void Elements_stay_inside_the_panel()
    {
        foreach (var theme in Load(out _))
            foreach (var e in theme.Document.Elements)
            {
                Assert.True(e.X >= -10 && e.Y >= -10, $"{theme.Name}/{e.Name} starts off the panel");
                Assert.True(e.X + e.Width <= theme.Document.Width + 10 && e.Y + e.Height <= theme.Document.Height + 10, $"{theme.Name}/{e.Name} ends off the panel");
            }
    }

    [Fact]
    public void The_new_themes_use_only_portable_sensors_and_specials()
    {
        var portable = PortableSensors.All.Select(p => p.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in Load(out _).Where(t => t.Name != "NVIDIA"))
            foreach (var e in theme.Document.Flatten())
            {
                if (e.Sensor is not { Kind: SensorKind.Lhm } sensor) continue;
                Assert.True(portable.Contains(sensor.Param ?? ""), $"{theme.Name}/{e.Name} is bound to '{sensor.Param}', which is not a portable sensor");
            }
    }

    [Fact]
    public void Every_portable_sensor_in_a_theme_shows_a_value_on_the_sample_pc()
    {
        var resolver = new SensorResolver(LhmParserTests.LoadFixture(), new LocalInfo());
        foreach (var theme in Load(out _).Where(t => t.Name != "NVIDIA"))
            foreach (var e in theme.Document.Flatten())
            {
                if (e.Sensor is not { Kind: SensorKind.Lhm } sensor || sensor.Param == "@GpuPowerWattage") continue;   // the sample GPU has no power sensor
                Assert.False(string.IsNullOrEmpty(resolver.Resolve(sensor).Text), $"{theme.Name}/{e.Name} ({sensor.Param}) reads as empty");
            }
    }
}
