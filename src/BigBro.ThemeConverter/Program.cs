using System.Diagnostics;
using BigBro.Core.Legacy;
using BigBro.Core.Sensors;

const string usage = """
    bigbro-convert - convert BigBro Monitor 1.x XML themes to the 2.0 JSON format

    Usage:
      bigbro-convert <theme.xml | folder> [more inputs...] [-o <output folder>] [-r] [-v] [--sensors <data.json | http://host:8085/data.json>]

    Options:
      -o, --output <dir>   Where theme folders are created (default: ./Themes)
      -r, --recursive      Also look in sub-folders of input folders
      -v, --verbose        Print every warning
      --sensors <source>   A LibreHardwareMonitor data.json (file or URL) of your PC. Hardware sensors used by the 1.x themes are
                           bound to the matching sensors of that PC; without it they are left unbound (open the theme in the
                           editor, or import through the app, to choose them).
      -h, --help           Show this help

    Each theme becomes <output>/<theme name>/theme.json with the images embedded as base64.
    """;

var inputs = new List<string>();
string output = Path.Combine(Directory.GetCurrentDirectory(), "Themes");
string? sensorSource = null;
bool recursive = false, verbose = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-h" or "--help" or "/?":
            Console.WriteLine(usage);
            return 0;
        case "-o" or "--output":
            if (++i >= args.Length) return Fail("Missing value for --output.");
            output = Path.GetFullPath(args[i]);
            break;
        case "--sensors":
            if (++i >= args.Length) return Fail("Missing value for --sensors.");
            sensorSource = args[i];
            break;
        case "-r" or "--recursive": recursive = true; break;
        case "-v" or "--verbose": verbose = true; break;
        default:
            if (args[i].StartsWith('-')) return Fail($"Unknown option {args[i]}.");
            inputs.Add(args[i]);
            break;
    }
}

if (inputs.Count == 0)
{
    Console.WriteLine(usage);
    return 1;
}

var files = new List<string>();
foreach (var input in inputs)
{
    if (File.Exists(input)) files.Add(Path.GetFullPath(input));
    else if (Directory.Exists(input))
        files.AddRange(Directory.EnumerateFiles(input, "*.xml",
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Select(Path.GetFullPath));
    else return Fail($"'{input}' does not exist.");
}

if (files.Count == 0) return Fail("No .xml files found.");

SensorResolver? resolver = null;
if (sensorSource is not null)
{
    try
    {
        SensorSnapshot snapshot;
        if (LhmClient.TryNormalizeUrl(sensorSource, out var url) && !File.Exists(sensorSource))
        {
            using var client = new LhmClient();
            snapshot = client.FetchAsync(url).GetAwaiter().GetResult();
        }
        else
        {
            using var stream = File.OpenRead(sensorSource);
            snapshot = LhmParser.Parse(stream);
        }
        resolver = new SensorResolver(snapshot, new LocalInfo());
        Console.WriteLine($"Using sensors of {snapshot.ComputerName} ({snapshot.Sensors.Count} sensors).");
    }
    catch (Exception ex)
    {
        return Fail($"Could not read sensors from '{sensorSource}': {ex.Message}");
    }
}

Directory.CreateDirectory(output);
int converted = 0, skipped = 0, failed = 0, warnings = 0;
var fonts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
var usedFolders = new HashSet<string>();
var sw = Stopwatch.StartNew();

foreach (var file in files.Order(StringComparer.OrdinalIgnoreCase))
{
    var label = Path.GetFileName(file);
    try
    {
        var r = LegacyThemeConverter.Prepare(file, output, usedFolders);
        var pending = r.Pending.Count;
        if (resolver is not null)
        {
            LegacyThemeConverter.ApplySensors(r.Pending, (kind, param) => resolver.SensorFor(kind, param)?.Path);
            var missing = r.Pending.Count(p => p.Element.Sensor is null);
            if (missing > 0) r.Warnings.Add($"{missing} sensor(s) have no match on that PC and were left unbound.");
        }
        else if (pending > 0)
        {
            r.Warnings.Add($"{pending} hardware sensor binding(s) left unbound (no --sensors given).");
        }
        LegacyThemeConverter.Save(r);
        converted++;
        warnings += r.Warnings.Count;
        foreach (var f in r.FontFamilies) fonts.Add(f);
        Console.WriteLine($"ok    {label} -> {Path.GetFileName(r.OutputDirectory)}  ({r.Document.Flatten().Count()} elements, {r.AssetCount} images, {r.Warnings.Count} warnings)");
        if (verbose) foreach (var w in r.Warnings) Console.WriteLine($"        ! {w}");
    }
    catch (InvalidDataException ex) when (ex.Message.Contains("expected <ThemeData>"))
    {
        skipped++;
        Console.WriteLine($"skip  {label}: not a theme");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL  {label}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{converted} converted, {skipped} skipped, {failed} failed, {warnings} warnings in {sw.Elapsed.TotalSeconds:F1}s -> {output}");
if (fonts.Count > 0)
    Console.WriteLine($"Fonts used: {string.Join(", ", fonts)}  (copy the .ttf/.otf files to the app's Fonts folder)");

return failed == 0 ? 0 : 2;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
