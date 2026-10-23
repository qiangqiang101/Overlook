using BigBro.Core.Settings;

namespace BigBro.Core.Tests;

public class UiTextTests
{
    [Theory]
    [InlineData("RainbowCycle", "Rainbow cycle")]
    [InlineData("Landscape16_9", "Landscape 16:9")]
    [InlineData("XLarge", "X large")]
    [InlineData("UniformToFill", "Uniform to fill")]
    [InlineData("Style1", "Style 1")]
    [InlineData("Fixed3D", "Fixed 3D")]
    [InlineData("CpuLoad", "CPU load")]
    [InlineData("MoboFan", "Motherboard fan")]
    [InlineData("None", "None")]
    public void Enum_values_get_readable_english(string name, string expected) =>
        Assert.Equal(expected, UiText.EnumDefault(name));

    [Theory]
    [InlineData("BackColor", "Background color")]
    [InlineData("LineCap", "Line cap")]
    [InlineData("Text", "Text")]
    public void Property_labels_get_readable_english(string name, string expected) =>
        Assert.Equal(expected, UiText.PropertyDefault(name));
}
