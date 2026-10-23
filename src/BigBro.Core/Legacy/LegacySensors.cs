using System.Text.RegularExpressions;

namespace BigBro.Core.Legacy;

public static partial class LegacySensors
{
    /// <summary>
    /// Whether the 1.x sensor's parameter means something (which GPU, disk, fan or core). Other kinds carried a default
    /// parameter that has no effect, so it must not split otherwise identical sensors into separate questions.
    /// </summary>
    public static bool UsesParam(LegacySensorKind kind)
    {
        var n = kind.ToString();
        return n.StartsWith("Gpu", StringComparison.Ordinal) || n.StartsWith("Hdd", StringComparison.Ordinal)
               || n.Contains("Fan", StringComparison.Ordinal)
               || kind is LegacySensorKind.CpuClockSpeed or LegacySensorKind.CpuCoreTemperatureC or LegacySensorKind.CpuCoreTemperatureF;
    }

    /// <summary>"GPUTemperatureC" -> "GPU Temperature C", for showing a 1.x sensor to the user.</summary>
    public static string Describe(LegacySensorKind kind, string? param)
    {
        var words = Words().Replace(kind.ToString(), " ").Trim();
        if (string.IsNullOrEmpty(param) || !UsesParam(kind)) return words;
        return kind.ToString() switch
        {
            var n when n.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase) => $"{words} (graphics card {param})",
            var n when n.StartsWith("Hdd", StringComparison.OrdinalIgnoreCase) => $"{words} (disk {param})",
            var n when n.Contains("Fan", StringComparison.OrdinalIgnoreCase) => $"{words} (fan {param})",
            var n when n.StartsWith("CpuClock", StringComparison.OrdinalIgnoreCase) || n.StartsWith("CpuCore", StringComparison.OrdinalIgnoreCase) => $"{words} (core {param})",
            _ => $"{words} ({param})",
        };
    }

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex Words();
}
