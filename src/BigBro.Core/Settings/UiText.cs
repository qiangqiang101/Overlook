using System.Text.RegularExpressions;

namespace BigBro.Core.Settings;

/// <summary>
/// Display text for things the UI generates from code names: theme property labels, enum values and element types.
/// Each has an English default derived from its name and can be translated with the keys
/// <c>prop.Name</c>, <c>enum.Type.Value</c> and <c>element.type</c>.
/// </summary>
public static class UiText
{
    private static readonly Dictionary<string, string> PropertyLabels = new()
    {
        ["BackColor"] = "Background color",
        ["ForeColor"] = "Text color",
        ["X"] = "X (left)",
        ["Y"] = "Y (top)",
        ["UpdateInterval"] = "Update interval (ms)",
        ["Size"] = "Size (pt)",
        ["Opacity"] = "Opacity (0-1)",
        ["Rgb"] = "RGB background",
        ["YoutubeId"] = "YouTube video id",
    };

    // names whose generated text would lose a unit or read badly
    private static readonly Dictionary<string, string> EnumOverrides = new()
    {
        ["CustomDateTime"] = "Custom date / time",
        ["WeatherTempC"] = "Weather temperature (°C)",
        ["WeatherTempF"] = "Weather temperature (°F)",
        ["WeatherWindSpeedKmh"] = "Weather wind speed (km/h)",
        ["WeatherWindSpeedMph"] = "Weather wind speed (mph)",
    };

    /// <summary>The English label of a theme property, e.g. <c>BackColor</c> becomes "Background color".</summary>
    public static string PropertyDefault(string name) =>
        PropertyLabels.TryGetValue(name, out var label) ? label : SplitWords(name);

    public static string Property(string name) => Loc.T("prop." + name, PropertyDefault(name));

    /// <summary>The English text of an enum value, e.g. <c>RainbowCycle</c> becomes "Rainbow cycle", <c>Landscape16_9</c> "Landscape 16:9".</summary>
    public static string EnumDefault(string valueName)
    {
        if (EnumOverrides.TryGetValue(valueName, out var fixedText)) return fixedText;
        var spaced = Regex.Replace(valueName, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|(?<=[A-Za-z])(?=[0-9])", " ").Replace('_', ':');
        return Polish(char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant());
    }

    /// <summary>Restores acronyms that lower-casing flattened ("Cpu load" becomes "CPU load").</summary>
    private static string Polish(string text) =>
        Regex.Replace(text, @"\b(cpu|gpu|ram|hdd|rgb|led|fps)\b", m => m.Value.ToUpperInvariant(), RegexOptions.IgnoreCase)
            .Replace("Mobo", "Motherboard").Replace("3 d", "3D");

    public static string EnumKey(Type type, string valueName) => $"enum.{type.Name}.{valueName}";

    public static string EnumValue(Type type, string valueName) => Loc.T(EnumKey(type, valueName), EnumDefault(valueName));

    public static string EnumValue(Enum value) => EnumValue(value.GetType(), value.ToString());

    private static string SplitWords(string name)
    {
        var spaced = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[a-z])(?=[0-9])", " ");
        return Polish(char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant());
    }
}
