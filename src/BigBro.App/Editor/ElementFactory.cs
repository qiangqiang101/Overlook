using BigBro.Core.Theming;

namespace BigBro.App.Editor;

/// <summary>Creates new elements with usable defaults and helpers to find/move them in the tree.</summary>
public static class ElementFactory
{
    public static readonly (string Type, string Label, Func<ThemeDocument, ThemeElement> Create)[] Types =
    [
        ("text", "Text label", t => new TextElement
        {
            Width = 160, Height = 36, Text = "Label", Font = DefaultFont(t, 16), ForeColor = t.TextColor,
        }),
        ("customText", "Title / value / unit", t => new CustomTextElement
        {
            Width = 260, Height = 50, Title = "CPU", Value = "0", Unit = "%",
            TitleFont = DefaultFont(t, 14), ValueFont = DefaultFont(t, 20), UnitFont = DefaultFont(t, 14),
            TitleColor = t.TextColor, ValueColor = t.TextColor, UnitColor = t.TextColor,
        }),
        ("image", "Image", _ => new ImageElement { Width = 120, Height = 120, SizeMode = ImageSizeMode.Zoom }),
        ("bar", "Progress bar", t => new BarElement
        {
            Width = 240, Height = 26, BackColor = new ThemeColor(255, 40, 40, 40), Font = DefaultFont(t, 12), ForeColor = t.TextColor,
        }),
        ("circularBar", "Circular gauge", t => new CircularBarElement
        {
            Width = 160, Height = 160, Style = CircularStyle.Style2, Thickness = 12, Font = DefaultFont(t, 22), ForeColor = t.TextColor,
            TrackColor = new ThemeColor(255, 50, 50, 50), ProgressColor = ThemeColor.Lime, ProgressColor2 = new ThemeColor(255, 0, 170, 255),
            Unit = "%", SubText = "CPU", ProgressShape = ProgressShape.Round,
        }),
        ("chart", "Line chart", t => new ChartElement
        {
            Width = 240, Height = 120, Font = DefaultFont(t, 10), ForeColor = t.TextColor,
        }),
        ("detail", "Sensor list", t => new DetailElement
        {
            Width = 260, Height = 160, Font = DefaultFont(t, 12), ForeColor = t.TextColor, Group = DetailGroup.CpuTemperature,
        }),
        ("youtube", "YouTube video", _ => new YoutubeElement { Width = 320, Height = 180, YoutubeId = "", Play = true }),
        ("audio", "Audio visualizer", _ => new AudioElement
        {
            Width = 400, Height = 100, BarCount = 32, BarSpacing = 2, ScalingStrategy = "Decibel", Direction = "Bottom",
            LineCap = "Round", BarStyle = "Bar", ColorStyle = "Rainbow", Speed = 0.02,
            Colors = [new(255, 128, 0, 128), new(255, 255, 0, 0), new(255, 255, 255, 0), new(255, 0, 255, 0), new(255, 0, 255, 255), new(255, 0, 0, 255), new(255, 128, 0, 128)],
        }),
    ];

    private static ThemeFont DefaultFont(ThemeDocument t, double size) =>
        new() { Family = t.Flatten().OfType<TextElement>().Select(e => e.Font.Family).FirstOrDefault() ?? "Segoe UI", Size = size };

    public static ThemeElement Create(string type, ThemeDocument theme)
    {
        var entry = Types.First(t => t.Type == type);
        var element = entry.Create(theme);
        element.Name = UniqueName(theme, entry.Label.Split(' ', '/')[0].Replace(":", ""));
        element.X = 20;
        element.Y = 20;
        return element;
    }

    public static string TypeOf(ThemeElement e) => e switch
    {
        TextElement => "text", CustomTextElement => "customText", ImageElement => "image", BarElement => "bar",
        CircularBarElement => "circularBar", ChartElement => "chart", DetailElement => "detail", YoutubeElement => "youtube",
        _ => "audio",
    };

    /// <summary>The element type's name for display, translated. (New elements are still named from the English label.)</summary>
    public static string LocalLabel(string type, string english) => BigBro.Core.Settings.Loc.T("element." + type, english);

    public static string LabelOf(ThemeElement e) =>
        Types.Where(t => t.Type == TypeOf(e)).Select(t => LocalLabel(t.Type, t.Label)).First();

    public static string UniqueName(ThemeDocument theme, string baseName)
    {
        var used = theme.Flatten().Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(baseName)) return baseName;
        for (var i = 2; ; i++)
            if (!used.Contains($"{baseName}{i}")) return $"{baseName}{i}";
    }

    /// <summary>Deep copy through JSON, renamed so it doesn't clash.</summary>
    public static ThemeElement Clone(ThemeDocument theme, ThemeElement source)
    {
        var holder = new ThemeDocument { Elements = [source] };
        var copy = ThemeJson.Deserialize(ThemeJson.Serialize(holder)).Elements[0];
        Rename(theme, copy);
        copy.X += 12;
        copy.Y += 12;
        return copy;
    }

    private static void Rename(ThemeDocument theme, ThemeElement e)
    {
        e.Name = UniqueName(theme, e.Name + "_copy");
        foreach (var c in e.Children) Rename(theme, c);
    }

    // ------------------------------------------------------------------ tree operations

    /// <summary>The list holding <paramref name="element"/> (the theme's root list or its parent's children) and its parent.</summary>
    public static (List<ThemeElement> Siblings, ThemeElement? Parent)? Locate(ThemeDocument theme, ThemeElement element)
    {
        if (theme.Elements.Contains(element)) return (theme.Elements, null);
        foreach (var e in theme.Flatten())
            if (e.Children.Contains(element)) return (e.Children, e);
        return null;
    }

    public static (int X, int Y) AbsolutePosition(ThemeDocument theme, ThemeElement element)
    {
        int x = 0, y = 0;
        for (var cur = element; cur is not null; cur = Locate(theme, cur)?.Parent)
        {
            x += cur.X;
            y += cur.Y;
        }
        return (x, y);
    }

    public static bool IsDescendant(ThemeElement ancestor, ThemeElement candidate) =>
        ancestor.Children.Any(c => c == candidate || IsDescendant(c, candidate));

    /// <summary>Moves an element under a new parent (null = top level), keeping it where it is on the panel.</summary>
    public static void Reparent(ThemeDocument theme, ThemeElement element, ThemeElement? newParent)
    {
        if (newParent == element || (newParent is not null && IsDescendant(element, newParent))) return;
        var (ax, ay) = AbsolutePosition(theme, element);
        Locate(theme, element)?.Siblings.Remove(element);

        var (px, py) = newParent is null ? (0, 0) : AbsolutePosition(theme, newParent);
        element.X = ax - px;
        element.Y = ay - py;
        (newParent?.Children ?? theme.Elements).Add(element);     // new children go in front
    }

    /// <summary>Moves toward the front (<paramref name="forward"/>) or back in its sibling list.</summary>
    public static bool Reorder(ThemeDocument theme, ThemeElement element, bool forward)
    {
        if (Locate(theme, element) is not { } loc) return false;
        var i = loc.Siblings.IndexOf(element);
        var j = forward ? i + 1 : i - 1;
        if (j < 0 || j >= loc.Siblings.Count) return false;
        (loc.Siblings[i], loc.Siblings[j]) = (loc.Siblings[j], loc.Siblings[i]);
        return true;
    }

    public static void Delete(ThemeDocument theme, ThemeElement element) =>
        Locate(theme, element)?.Siblings.Remove(element);
}
