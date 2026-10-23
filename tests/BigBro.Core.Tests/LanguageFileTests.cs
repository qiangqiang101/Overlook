using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BigBro.Core.Rgb;
using BigBro.Core.Settings;
using BigBro.Core.Theming;

namespace BigBro.Core.Tests;

/// <summary>Every piece of UI text must exist in every language file, and no file may keep keys the app no longer uses.</summary>
public partial class LanguageFileTests
{
    // the editor hides these (PropertyEditor.Hidden) and never shows a sensor binding as a plain property
    private static readonly HashSet<string> HiddenProperties = ["Children", "Elements", "Directory", "FormatVersion", "SizePx"];
    private static readonly string[] ElementTypes = ["text", "customText", "image", "bar", "circularBar", "chart", "detail", "youtube", "audio"];

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex("""Loc\.T\(\s*"([^"]+)"\s*,\s*((?:@?"(?:[^"\\]|\\.)*")(?:\s*\+\s*@?"(?:[^"\\]|\\.)*")*)""")]
    private static partial Regex LocCall();

    /// <summary>Keys written as <c>Loc.T("key", "English")</c> in the source, with their English text (null when it isn't a plain literal).</summary>
    private static Dictionary<string, string?> StaticKeys()
    {
        var keys = new Dictionary<string, string?>();
        var src = Path.Combine(RepositoryRoot(), "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = file.Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
            foreach (Match m in LocCall().Matches(File.ReadAllText(file)))
            {
                var parts = Regex.Matches(m.Groups[2].Value, """@?"((?:[^"\\]|\\.)*)"[^"]*""").Select(p => p.Groups[1].Value);
                var text = string.Concat(parts).Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\");
                keys[m.Groups[1].Value] = text;
            }
        }
        // the three long hints get their English text from the XAML
        foreach (var k in new[] { "settings.sourceHint", "settings.rgbHint" }) keys[k] = null;
        return keys;
    }

    private static void Visit(Type type, HashSet<string> props, HashSet<Type> enums, HashSet<Type> seen)
    {
        if (!seen.Add(type)) return;
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0 || HiddenProperties.Contains(p.Name)) continue;
            if (p.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
            var t = p.PropertyType;
            if (t == typeof(SensorBinding)) continue;
            props.Add(p.Name);
            if (t.IsEnum) enums.Add(t);
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) { var inner = t.GetGenericArguments()[0]; if (inner.IsClass && inner != typeof(string)) Visit(inner, props, enums, seen); }
            else if (t.IsClass && t != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(t)) Visit(t, props, enums, seen);
        }
    }

    /// <summary>Keys the UI builds from names: property labels, enum values, element types.</summary>
    private static Dictionary<string, string> DynamicKeys()
    {
        var props = new HashSet<string>();
        var enums = new HashSet<Type>();
        var seen = new HashSet<Type>();
        Visit(typeof(ThemeDocument), props, enums, seen);
        foreach (var t in typeof(ThemeElement).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(ThemeElement).IsAssignableFrom(t)))
            Visit(t, props, enums, seen);
        foreach (var t in new[] { typeof(LedShape), typeof(MatrixSizeType), typeof(MatrixSizeTier), typeof(OfflineEffect), typeof(CoverStretch) }) enums.Add(t);

        var keys = new Dictionary<string, string>();
        foreach (var name in props) keys["prop." + name] = UiText.PropertyDefault(name);
        foreach (var type in enums)
            foreach (var value in Enum.GetNames(type))
                keys[UiText.EnumKey(type, value)] = UiText.EnumDefault(value);
        foreach (var kind in BigBro.Core.Sensors.SensorKinds.Specials)
            keys[UiText.EnumKey(typeof(SensorKind), kind.ToString())] = UiText.EnumDefault(kind.ToString());
        foreach (var type in ElementTypes) keys["element." + type] = type;
        foreach (var (alias, english) in BigBro.Core.Sensors.PortableSensors.All) keys[BigBro.Core.Sensors.PortableSensors.Key(alias)] = english;
        return keys;
    }

    private static Dictionary<string, string> LoadLanguage(string file) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "BigBro.App", "Languages", file)))!;

    public static IEnumerable<object[]> Languages() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "BigBro.App", "Languages"), "*.json").Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(Languages))]
    public void Language_file_has_every_key_and_nothing_stale(string file)
    {
        var language = LoadLanguage(file);
        var used = StaticKeys().Keys.Concat(DynamicKeys().Keys).ToHashSet();

        var dynamic = DynamicKeys();
        var statics = StaticKeys();
        var missing = used.Where(k => !language.ContainsKey(k))
            .Select(k => $"{k} => {(dynamic.TryGetValue(k, out var d) ? d : statics[k])}").Order().ToList();
        var stale = language.Keys.Where(k => !used.Contains(k)).Order().ToList();

        Assert.True(missing.Count == 0, $"{file} is missing {missing.Count} key(s):\n" + string.Join("\n", missing));
        Assert.True(stale.Count == 0, $"{file} has keys the app no longer uses:\n" + string.Join("\n", stale));
        Assert.DoesNotContain(language, kv => string.IsNullOrWhiteSpace(kv.Value));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translations_keep_the_placeholders_of_the_english_text(string file)
    {
        var language = LoadLanguage(file);
        var problems = new List<string>();
        foreach (var (key, english) in StaticKeys())
        {
            if (english is null || !language.TryGetValue(key, out var translated)) continue;
            var want = Regex.Matches(english, @"\{\d+\}").Select(m => m.Value).Order().ToList();
            var got = Regex.Matches(translated, @"\{\d+\}").Select(m => m.Value).Order().ToList();
            if (!want.SequenceEqual(got)) problems.Add($"{key}: English has [{string.Join(",", want)}], translation has [{string.Join(",", got)}]");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
