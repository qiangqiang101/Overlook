using BigBro.Core.Settings;
using BigBro.Core.Theming;

namespace BigBro.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void SensorOverrideSurvivesSaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"overlook-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings();
            settings.Overrides["Theme"] = new(StringComparer.Ordinal)
            {
                ["CPU"] = new ElementOverride { Sensor = new SensorBinding { Kind = SensorKind.Lhm, Param = "PC/CPU/Load/CPU Total" } },
            };
            SettingsStore.Save(settings, path);

            var loaded = SettingsStore.Load(path);
            var ov = loaded.FindOverride("Theme", "CPU");
            Assert.NotNull(ov);
            Assert.False(ov!.IsEmpty);
            Assert.Equal(SensorKind.Lhm, ov.Sensor!.Kind);
            Assert.Equal("PC/CPU/Load/CPU Total", ov.Sensor.Param);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EmptyOverrideIsEmpty() => Assert.True(new ElementOverride().IsEmpty);
}
