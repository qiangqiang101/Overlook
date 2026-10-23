using System.Text;
using BigBro.Core.Legacy;

namespace BigBro.App.Views;

/// <summary>
/// Importing 1.x themes in two steps: <see cref="Prepare"/> converts them in memory and copies fonts, then, once the user has chosen
/// sensors, <see cref="Finish"/> binds them and writes the themes.
/// </summary>
internal sealed class ImportReport
{
    private readonly List<ConversionResult> _results = [];
    private readonly string _themesDirectory;

    private ImportReport(string themesDirectory) => _themesDirectory = themesDirectory;

    public int Converted { get; private set; }
    public int Warnings { get; private set; }
    public int FontsCopied { get; private set; }
    public int UnboundSensors { get; private set; }
    public string? LastConverted { get; private set; }
    public List<string> Failures { get; } = [];
    public SortedSet<string> FontsUsed { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every hardware sensor the themes use, grouped so identical ones are asked about once.</summary>
    public IReadOnlyList<PendingGroup> PendingGroups() =>
        _results.SelectMany(r => r.Pending.Select(p => (Result: r, Pending: p)))
            .GroupBy(x => x.Pending.Key)
            .Select(g => new PendingGroup(g.Key, g.First().Pending.Kind, g.First().Pending.Param, g.Count(), g.Select(x => x.Result).Distinct().Count()))
            .ToList();

    public static ImportReport Prepare(IReadOnlyCollection<string> xmlFiles, string themesDirectory, string fontsDirectory)
    {
        var report = new ImportReport(themesDirectory);
        var used = new HashSet<string>();

        foreach (var file in xmlFiles)
        {
            try
            {
                report._results.Add(LegacyThemeConverter.Prepare(file, themesDirectory, used));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                report.Failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        // A 1.x install keeps its fonts in a Fonts folder next to the Themes folder.
        foreach (var dir in xmlFiles.Select(f => Path.GetDirectoryName(Path.GetFullPath(f))!).Distinct())
        {
            var candidates = new[] { Path.Combine(dir, "Fonts"), Path.Combine(dir, "..", "Fonts") };
            foreach (var fonts in candidates.Where(Directory.Exists))
                report.FontsCopied += CopyFonts(fonts, fontsDirectory);
        }
        return report;
    }

    /// <param name="sensorFor">The chosen sensor path per <see cref="PendingGroup.Key"/>; null leaves everything unbound.</param>
    public void Finish(IReadOnlyDictionary<string, string?>? sensorFor)
    {
        foreach (var result in _results)
        {
            LegacyThemeConverter.ApplySensors(result.Pending,
                (kind, param) => sensorFor is not null && sensorFor.TryGetValue(new PendingSensor(null!, kind, param).Key, out var path) ? path : null);
            UnboundSensors += result.Pending.Count(p => p.Element.Sensor is null);

            try
            {
                LegacyThemeConverter.Save(result);
                Converted++;
                Warnings += result.Warnings.Count;
                LastConverted = result.Document.Name;
                foreach (var family in result.FontFamilies) FontsUsed.Add(family);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Failures.Add($"{result.Document.Name}: {ex.Message}");
            }
        }
    }

    private static int CopyFonts(string from, string to)
    {
        Directory.CreateDirectory(to);
        var copied = 0;
        foreach (var file in Directory.EnumerateFiles(from))
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".otf", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(to, Path.GetFileName(file));
            if (File.Exists(target)) continue;
            File.Copy(file, target);
            copied++;
        }
        return copied;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Converted} theme(s) imported" + (Warnings > 0 ? $", {Warnings} note(s)." : "."));
        if (UnboundSensors > 0)
            sb.AppendLine($"{UnboundSensors} element(s) have no sensor yet; pick one in the theme editor.");
        if (FontsCopied > 0) sb.AppendLine($"{FontsCopied} font file(s) copied from your 1.x install.");
        else if (FontsUsed.Count > 0)
            sb.AppendLine("Fonts used: " + string.Join(", ", FontsUsed) + ". Install them or put the files in the Fonts folder if a theme looks wrong.");
        if (Failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{Failures.Count} file(s) could not be imported:");
            foreach (var f in Failures.Take(8)) sb.AppendLine("  • " + f);
            if (Failures.Count > 8) sb.AppendLine($"  ...and {Failures.Count - 8} more.");
        }
        return sb.ToString().TrimEnd();
    }
}
