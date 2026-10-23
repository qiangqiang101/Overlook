using System.Globalization;
using System.Text;
using System.Xml.Linq;
using BigBro.Core.Sensors;
using BigBro.Core.Theming;

namespace BigBro.Core.Legacy;

/// <summary>
/// A hardware sensor a 1.x element was bound to. 2.0 themes address sensors by LibreHardwareMonitor path, which depends on the
/// user's machine, so the importer asks the user to pick one (see <see cref="LegacyThemeConverter.ApplySensors"/>).
/// </summary>
public sealed record PendingSensor(ThemeElement Element, LegacySensorKind Kind, string? Param)
{
    /// <summary>Elements with the same key can share one answer.</summary>
    public string Key => $"{Kind}|{(LegacySensors.UsesParam(Kind) ? Param ?? "" : "")}";
}

public sealed class ConversionResult
{
    public required ThemeDocument Document { get; init; }
    public List<string> Warnings { get; } = [];
    /// <summary>Folder the theme was written to (null when converting in memory).</summary>
    public string? OutputDirectory { get; set; }
    public int AssetCount { get; set; }
    /// <summary>Hardware sensors still to be chosen; those elements are unbound until <see cref="LegacyThemeConverter.ApplySensors"/> runs.</summary>
    public List<PendingSensor> Pending { get; } = [];
    /// <summary>Where <see cref="LegacyThemeConverter.Save"/> will write the theme.</summary>
    public string? TargetDirectory { get; set; }
    public HashSet<string> FontFamilies { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Converts a BigBro Monitor 1.x theme (the XmlSerializer output of <c>ThemeData</c>) to the 2.0 JSON format.
/// </summary>
public static class LegacyThemeConverter
{
    /// <summary>Converts <paramref name="xmlPath"/> and writes it, leaving hardware sensors unbound (see <see cref="Prepare"/>).</summary>
    public static ConversionResult ConvertFile(string xmlPath, string outputRoot, ISet<string>? usedFolders = null)
    {
        var result = Prepare(xmlPath, outputRoot, usedFolders);
        Save(result);
        return result;
    }

    /// <summary>
    /// Reads a 1.x theme into memory. Nothing is written until <see cref="Save"/>, so the caller can first let the user choose
    /// sensors for <see cref="ConversionResult.Pending"/> and apply them with <see cref="ApplySensors"/>.
    /// </summary>
    /// <param name="usedFolders">
    /// Folder names already produced in this batch; a clash gets a numeric suffix instead of overwriting the earlier theme.
    /// </param>
    public static ConversionResult Prepare(string xmlPath, string outputRoot, ISet<string>? usedFolders = null)
    {
        XDocument xml;
        try
        {
            xml = XDocument.Load(xmlPath, LoadOptions.None);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException(
                $"'{Path.GetFileName(xmlPath)}' is not a 1.x XML theme ({ex.Message}). " +
                "Themes saved with 'Build' (binary) can't be read by .NET 10; open them in 1.x and use Save to get the XML.", ex);
        }

        var fallbackName = Path.GetFileNameWithoutExtension(xmlPath);
        var name = xml.Root?.Element("Name")?.Value;
        var folderName = SanitizeFolderName(string.IsNullOrWhiteSpace(name) ? fallbackName : name!);
        if (usedFolders is not null)
        {
            var unique = folderName;
            for (var i = 2; !usedFolders.Add(unique.ToLowerInvariant()); i++) unique = $"{folderName} ({i})";
            folderName = unique;
        }

        var sink = new DataUriAssetSink();
        var result = Convert(xml, sink, fallbackName);
        result.TargetDirectory = Path.Combine(outputRoot, folderName);
        result.AssetCount = sink.Count;
        return result;
    }

    /// <summary>Writes a prepared theme to its folder.</summary>
    public static void Save(ConversionResult result)
    {
        var folder = result.TargetDirectory ?? throw new InvalidOperationException("The theme has no target folder; use Prepare().");
        Directory.CreateDirectory(folder);
        ThemeJson.Save(result.Document, Path.Combine(folder, ThemeJson.ThemeFileName));
        result.OutputDirectory = folder;
    }

    /// <summary>
    /// Binds each pending element to the path <paramref name="pathFor"/> returns for its 1.x sensor (null leaves it unbound).
    /// </summary>
    public static void ApplySensors(IEnumerable<PendingSensor> pending, Func<LegacySensorKind, string?, string?> pathFor)
    {
        foreach (var p in pending)
        {
            var path = pathFor(p.Kind, p.Param);
            p.Element.Sensor = string.IsNullOrWhiteSpace(path) ? null : new SensorBinding { Kind = SensorKind.Lhm, Param = path };
        }
    }

    public static ConversionResult Convert(XDocument xml, IAssetSink assets, string fallbackName = "Untitled Theme")
    {
        var root = xml.Root ?? throw new InvalidDataException("Empty XML document.");
        if (root.Name.LocalName != "ThemeData")
            throw new InvalidDataException($"Root element is <{root.Name.LocalName}>, expected <ThemeData>; not a 1.x theme.");

        var ctx = new Context(assets);
        var theme = new ThemeDocument
        {
            Name = Str(root, "Name") is { Length: > 0 } n ? n : fallbackName,
            Author = NullIfEmpty(Str(root, "Author")),
            Version = Str(root, "Version") is { Length: > 0 } v ? v : "1.0",
            Tags = root.Element("Tags")?.Elements("string").Select(e => e.Value).Where(s => s.Length > 0).ToList() ?? [],
            BackgroundColor = Color(root, "BackgroundColor", ThemeColor.White),
            TextColor = Color(root, "TextColor", ThemeColor.Red),
            UpdateInterval = Int(root, "UpdateInterval", 3000),
            Opacity = Dbl(root, "Opacity", 1.0),
        };
        var (w, h) = Size(root, "Size", 480, 320);
        theme.Width = w;
        theme.Height = h;

        theme.BackgroundImage = ctx.Image(root, "BackgroundImage", "theme background");
        theme.Preview = ctx.Image(root, "Snapshot", "preview", quiet: true) ?? ctx.Image(root, "CustomPreview", "preview", quiet: true);

        // The 1.x animated gradient is replaced by the live RGB lighting background.
        if (Bool(root, "RGBBackground", false)) theme.Rgb = new RgbBackground();

        // 1.x added the lists to the form in this order; later additions end up *behind* earlier ones.
        var nodes = new List<Node>();
        Add(nodes, root, "TextLabels", ctx.TextLabel);
        Add(nodes, root, "CustomTexts", ctx.CustomText);
        Add(nodes, root, "ImageBoxes", ctx.ImageBox);
        Add(nodes, root, "StatusBars", ctx.StatusBar);
        Add(nodes, root, "CircularSBs", ctx.CircularBar);
        Add(nodes, root, "PlotCharts", ctx.PlotChart);
        Add(nodes, root, "DetailSensors", ctx.Detail);
        Add(nodes, root, "YoutubeVideos", ctx.Youtube);
        Add(nodes, root, "AudioVisualizers", ctx.Audio);

        theme.Elements = BuildTree(nodes, ctx);

        var result = new ConversionResult { Document = theme };
        result.Warnings.AddRange(ctx.Warnings);
        result.Pending.AddRange(ctx.Pending);
        foreach (var f in ctx.Fonts) result.FontFamilies.Add(f);
        return result;
    }

    // ---------------------------------------------------------------- tree

    private sealed class Node
    {
        public required ThemeElement Element;
        public string? ParentName;
        public int AbsX, AbsY;
        public int AddOrder;
    }

    private static void Add(List<Node> nodes, XElement root, string listName, Func<XElement, (ThemeElement e, string? parent)> convert)
    {
        foreach (var x in root.Element(listName)?.Elements() ?? [])
        {
            var (e, parent) = convert(x);
            nodes.Add(new Node { Element = e, ParentName = NullIfEmpty(parent), AbsX = e.X, AbsY = e.Y, AddOrder = nodes.Count });
        }
    }

    private static List<ThemeElement> BuildTree(List<Node> nodes, Context ctx)
    {
        // Names must be unique: user overrides and parent links use them.
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in nodes)
        {
            var name = string.IsNullOrWhiteSpace(n.Element.Name) ? n.Element.GetType().Name.Replace("Element", "") : n.Element.Name;
            if (seen.TryGetValue(name, out var count))
            {
                seen[name] = count + 1;
                var renamed = $"{name}_{count + 1}";
                ctx.Warnings.Add($"Duplicate element name '{name}' renamed to '{renamed}'.");
                name = renamed;
            }
            else seen[name] = 1;
            n.Element.Name = name;
        }

        // Parent lookups use the 1.x (pre-rename) names, which for the first occurrence are unchanged.
        var byName = nodes.GroupBy(n => n.Element.Name).ToDictionary(g => g.Key, g => g.First());
        var children = new Dictionary<Node, List<Node>>();
        var top = new List<Node>();

        foreach (var n in nodes)
        {
            var parent = n.ParentName is not null && byName.TryGetValue(n.ParentName, out var p) && p != n && !IsAncestor(n, p, byName) ? p : null;
            if (n.ParentName is not null && parent is null)
                ctx.Warnings.Add($"'{n.Element.Name}': parent '{n.ParentName}' not found (or circular); placed at top level.");
            if (parent is null) top.Add(n);
            else
            {
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
                list.Add(n);
            }
        }

        List<ThemeElement> Realize(IEnumerable<Node> level, Node? parent)
        {
            // JSON is back-to-front; 1.x z-order was front-to-back in add order.
            var ordered = level.OrderByDescending(n => n.AddOrder).ToList();
            var result = new List<ThemeElement>(ordered.Count);
            foreach (var n in ordered)
            {
                if (parent is not null)
                {
                    // 1.x stored absolute positions and made them parent-relative on load.
                    n.Element.X = n.AbsX - parent.AbsX;
                    n.Element.Y = n.AbsY - parent.AbsY;
                }
                if (children.TryGetValue(n, out var kids)) n.Element.Children = Realize(kids, n);
                result.Add(n.Element);
            }
            return result;
        }

        return Realize(top, null);
    }

    private static bool IsAncestor(Node candidate, Node start, Dictionary<string, Node> byName)
    {
        // true if `candidate` appears in the ParentName chain above `start`
        var hops = 0;
        for (var cur = start; cur.ParentName is not null && hops < 64; hops++)
        {
            if (!byName.TryGetValue(cur.ParentName, out var next)) return false;
            if (next == candidate) return true;
            cur = next;
        }
        return false;
    }

    // ---------------------------------------------------------------- per-element conversion

    private sealed class Context(IAssetSink assets)
    {
        public readonly List<string> Warnings = [];
        public readonly List<PendingSensor> Pending = [];
        public readonly HashSet<string> Fonts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _imageCache = new();

        public string? Image(XElement parent, string childName, string what, bool quiet = false)
        {
            var text = parent.Element(childName)?.Value;
            if (string.IsNullOrWhiteSpace(text)) return null;

            if (_imageCache.TryGetValue(text, out var cached)) return cached;

            string? path = null;
            try
            {
                var bytes = System.Convert.FromBase64String(text.Replace(" ", "+").Replace("\r", "").Replace("\n", ""));
                if (ImageSniffer.Extension(bytes) is ".bin" or ".tif")
                {
                    if (!quiet) Warnings.Add($"Unsupported image format in {what}; skipped.");
                }
                else
                {
                    path = assets.Add(bytes);
                }
            }
            catch (FormatException)
            {
                if (!quiet) Warnings.Add($"Invalid base64 image data in {what}; skipped.");
            }
            _imageCache[text] = path;
            return path;
        }

        private T Common<T>(T e, XElement x) where T : ThemeElement
        {
            e.Name = Str(x, "Name");
            (e.X, e.Y) = Point(x, "Location");
            (e.Width, e.Height) = Size(x, "Size", 100, 30);
            e.Visible = Bool(x, "Visible", true);
            e.BackColor = Color(x, "BackColor", ThemeColor.Transparent);
            var dock = Str(x, "Dock");
            if (dock.Length > 0 && dock != "None")
                Warnings.Add($"'{e.Name}': Dock={dock} is not supported and was ignored.");
            return e;
        }

        /// <summary>
        /// Special values (date, ping, weather...) convert directly. Hardware sensors can't: the element is recorded as
        /// pending and left unbound until the user chooses a sensor path.
        /// </summary>
        private SensorBinding? Sensor(XElement x, ThemeElement element)
        {
            var text = Str(x, "Sensor");
            if (text.Length == 0) return null;
            if (!System.Enum.TryParse<LegacySensorKind>(text, ignoreCase: true, out var legacy))
            {
                Warnings.Add($"'{element.Name}': unknown sensor '{text}'; unbound.");
                return null;
            }
            if (legacy == LegacySensorKind.None) return null;

            var param = NullIfEmpty(Str(x, "SensorParam"));
            if (System.Enum.TryParse<SensorKind>(legacy.ToString(), ignoreCase: true, out var special) && SensorKinds.IsSpecial(special))
                return new SensorBinding { Kind = special, Param = param };

            Pending.Add(new PendingSensor(element, legacy, param));
            return null;
        }

        private static bool HadSensor(XElement x) =>
            Str(x, "Sensor") is { Length: > 0 } t && !t.Equals("None", StringComparison.OrdinalIgnoreCase);

        private ThemeFont Font(XElement x, string name)
        {
            var f = x.Element(name);
            if (f is null) return new ThemeFont();
            var family = Str(f, "Name");
            var size = Dbl(f, "Size", 9);
            // 1.x allowed any GraphicsUnit; the new format is always points.
            size = Str(f, "Unit") switch
            {
                "Pixel" => size * 72.0 / 96.0,
                "Inch" => size * 72.0,
                "Millimeter" => size * 72.0 / 25.4,
                "Document" => size * 72.0 / 300.0,
                "Display" => size * 72.0 / 75.0,
                _ => size,
            };
            if (family.Length > 0) Fonts.Add(family);
            return new ThemeFont
            {
                Family = family.Length > 0 ? family : "Segoe UI",
                Size = Math.Round(size, 2),
                Bold = Bool(f, "Bold", false),
                Italic = Bool(f, "Italic", false),
                Underline = Bool(f, "Underline", false),
                Strikeout = Bool(f, "Strikeout", false),
            };
        }

        public (ThemeElement, string?) TextLabel(XElement x)
        {
            var e = Common(new TextElement(), x);
            e.Font = Font(x, "Font");
            e.ForeColor = Color(x, "ForeColor", ThemeColor.Black);
            e.RightToLeft = Bool(x, "RightToLeft", false);
            e.BeforeText = NullIfEmpty(Str(x, "BeforeText"));
            e.Text = Str(x, "Text");
            e.TextAlign = Enum(x, "TextAlign", ContentAlign.MiddleCenter);
            e.AutoSize = Bool(x, "AutoSize", false);
            e.AutoEllipsis = Bool(x, "AutoEllipsis", false);
            e.BorderStyle = Enum(x, "BorderStyle", BorderKind.None);
            e.Image = Image(x, "Image", $"'{e.Name}'");
            e.ImageAlign = Enum(x, "ImageAlign", ContentAlign.MiddleCenter);
            e.AllowUserEdit = Bool(x, "AllowUserEdit", false);
            e.Sensor = Sensor(x, e);
            // With a sensor the saved text is only the last value seen; don't carry stale data over.
            if (HadSensor(x)) e.Text = "";
            return (e, Str(x, "ParentName"));
        }

        public (ThemeElement, string?) CustomText(XElement x)
        {
            var e = Common(new CustomTextElement(), x);
            e.Title = Str(x, "Title");
            e.Value = Str(x, "Value");
            e.Unit = Str(x, "Unit");
            e.TitleColor = Color(x, "TitleColor", ThemeColor.Black);
            e.ValueColor = Color(x, "ValueColor", ThemeColor.Black);
            e.UnitColor = Color(x, "UnitColor", ThemeColor.Black);
            e.TitleFont = Font(x, "TitleFont");
            e.ValueFont = Font(x, "Font");
            e.UnitFont = Font(x, "UnitFont");
            e.TitleAlign = Enum(x, "TitleAlign", ContentAlign.MiddleLeft);
            e.ValueAlign = Enum(x, "ValueAlign", ContentAlign.MiddleCenter);
            e.UnitAlign = Enum(x, "UnitAlign", ContentAlign.MiddleRight);
            e.AutoWidth = Bool(x, "AutoWidth", true);
            e.TitleWidth = Dbl(x, "TitleWidth", 0);
            e.ValueWidth = Dbl(x, "ValueWidth", 0);
            e.UnitWidth = Dbl(x, "UnitWidth", 0);
            var (tx, ty) = Point(x, "TitleTextAdjustment");
            var (vx, vy) = Point(x, "ValueTextAdjustment");
            var (ux, uy) = Point(x, "UnitTextAdjustment");
            e.TitleOffset = new Offset(tx, ty);
            e.ValueOffset = new Offset(vx, vy);
            e.UnitOffset = new Offset(ux, uy);
            e.Sensor = Sensor(x, e);
            if (HadSensor(x)) e.Value = "";
            return (e, Str(x, "ParentName"));
        }

        public (ThemeElement, string?) ImageBox(XElement x)
        {
            var e = Common(new ImageElement(), x);
            e.BackgroundImage = Image(x, "BackgroundImage", $"'{e.Name}' background");
            e.BackgroundImageLayout = Enum(x, "BackgroundImageLayout", ImageLayoutKind.Tile);
            e.Image = Image(x, "Image", $"'{e.Name}'");
            e.SizeMode = Enum(x, "SizeMode", ImageSizeMode.Normal);
            e.BorderStyle = Enum(x, "BorderStyle", BorderKind.None);
            e.EnableDynamicImages = Bool(x, "EnableDynamicImages", false);
            e.AllowUserEdit = Bool(x, "AllowUserEdit", false);
            foreach (var si in x.Element("DynamicImages")?.Element("List")?.Elements() ?? [])
            {
                e.DynamicImages.Add(new DynamicImage
                {
                    Index = Int(si, "DisplayIndex", 0),
                    Name = Str(si, "Name"),
                    Image = Image(si, "Image", $"'{e.Name}' dynamic image"),
                });
            }
            e.DynamicImages.Sort((a, b) => a.Index.CompareTo(b.Index));
            e.Sensor = Sensor(x, e);
            return (e, Str(x, "ParentName"));
        }

        public (ThemeElement, string?) StatusBar(XElement x)
        {
            var e = Common(new BarElement(), x);
            e.Text = Str(x, "Text");
            e.Font = Font(x, "Font");
            e.ForeColor = Color(x, "ForeColor", ThemeColor.Black);
            e.ForegroundColor = Color(x, "ForegroundColor", new ThemeColor(255, 41, 128, 185));
            e.Texture = Image(x, "Texture", $"'{e.Name}' texture");
            (e.TextureWidth, e.TextureHeight) = Size(x, "TextureSize", 0, 0);
            e.UseTexture = Bool(x, "UseTexture", false) && e.Texture is not null;
            e.Minimum = Int(x, "Minimum", 0);
            e.Maximum = Int(x, "Maximum", 100);
            e.FillDirection = Enum(x, "FillDirection", FillDirection.LeftToRight);
            e.ShowValue = Bool(x, "ShowValue", false);
            e.Unit = Str(x, "Unit");
            e.Sensor = Sensor(x, e);
            return (e, Str(x, "ParentName"));
        }

        public (ThemeElement, string?) CircularBar(XElement x)
        {
            var e = Common(new CircularBarElement(), x);
            e.Text = Str(x, "Text");
            e.SubText = Str(x, "SubText");
            e.Font = Font(x, "Font");
            e.ForeColor = Color(x, "ForeColor", ThemeColor.Black);
            e.TrackColor = Color(x, "ForegroundColor", new ThemeColor(255, 40, 40, 40));
            e.ProgressColor = Color(x, "ProgressColor", ThemeColor.Black);
            e.ProgressColor2 = Color(x, "ProgressColor2", ThemeColor.Black);
            e.GradientMode = Enum(x, "GradientMode", GradientDirection.Horizontal);
            e.Style = Enum(x, "Style", CircularStyle.Style1);
            e.ProgressShape = Enum(x, "ProgressShape", ProgressShape.Flat);
            e.BarWidth = Int(x, "BarWidth", 270);
            e.Thickness = Int(x, "Thickness", 10);
            e.StartAngle = Int(x, "StartAngle", 0);
            e.Minimum = Int(x, "Minimum", 0);
            e.Maximum = Int(x, "Maximum", 100);
            e.Unit = Str(x, "Unit");
            e.TextMode = Enum(x, "TextMode", CircularTextMode.Value);
            e.Sensor = Sensor(x, e);
            return (e, Str(x, "ParentName"));
        }

        public (ThemeElement, string?) PlotChart(XElement x)
        {
            var e = Common(new ChartElement(), x);
            e.Text = Str(x, "Text");
            e.Font = Font(x, "Font");
            e.ForeColor = Color(x, "ForeColor", ThemeColor.White);
            e.ScaleMode = Enum(x, "ScaleMode", ChartScaleMode.Absolute);
            e.BorderStyle = Enum(x, "BorderStyle", ChartBorder.None);
            var s = x.Element("PlotChartStyle");
            if (s is not null)
            {
                e.Style = new ChartStyle
                {
                    ShowVerticalGridLines = Bool(s, "ShowVerticalGridLines", true),
                    ShowHorizontalGridLines = Bool(s, "ShowHorizontalGridLines", true),
                    ShowCurMax = Bool(s, "ShowCurMax", false),
                    ShowAverageLine = Bool(s, "ShowAverageLine", false),
                    VerticalGridPen = Pen(s, "VerticalGridPen"),
                    HorizontalGridPen = Pen(s, "HorizontalGridPen"),
                    AvgLinePen = Pen(s, "AvgLinePen"),
                    ChartLinePen = Pen(s, "ChartLinePen"),
                    AntiAliasing = Bool(s, "AntiAliasing", true),
                    BackgroundColorTop = Color(s, "BackgroundColorTop", ThemeColor.Black),
                    BackgroundColorBottom = Color(s, "BackgroundColorBottom", ThemeColor.Black),
                };
            }
            e.Sensor = Sensor(x, e);
            return (e, Str(x, "ParentName"));
        }

        private static ChartPen Pen(XElement parent, string name)
        {
            var p = parent.Element(name);
            if (p is null) return new ChartPen();
            return new ChartPen
            {
                Color = Color(p, "Color", ThemeColor.Black),
                DashStyle = Enum(p, "DashStyle", LineDash.Solid),
                Width = Dbl(p, "Width", 1),
            };
        }

        public (ThemeElement, string?) Detail(XElement x)
        {
            var e = Common(new DetailElement(), x);
            e.Font = Font(x, "Font");
            e.ForeColor = Color(x, "ForeColor", ThemeColor.White);
            e.Group = Enum(x, "DetailedSensor", DetailGroup.None);
            e.DrawBorder = Bool(x, "DrawBorder", false);
            return (e, Str(x, "ParentName"));
        }

        public (ThemeElement, string?) Youtube(XElement x)
        {
            var e = Common(new YoutubeElement(), x);
            e.YoutubeId = NullIfEmpty(Str(x, "YoutubeID"));
            e.Play = Bool(x, "Play", false);
            e.Sensor = Sensor(x, e);
            return (e, Str(x, "ParentName"));
        }

        public (ThemeElement, string?) Audio(XElement x)
        {
            var e = Common(new AudioElement(), x);
            e.BackgroundImage = Image(x, "BackgroundImage", $"'{e.Name}' background");
            e.UseAverage = Bool(x, "UseAverage", false);
            e.BarCount = Int(x, "BarCount", 0);
            e.BarSpacing = Int(x, "BarSpacing", 0);
            e.ScalingStrategy = NullIfEmpty(Str(x, "ScalingStrategy"));
            e.Direction = NullIfEmpty(Str(x, "Direction"));
            e.LineCap = NullIfEmpty(Str(x, "LineCap"));
            e.BarStyle = NullIfEmpty(Str(x, "BarStyle"));
            e.ColorStyle = NullIfEmpty(Str(x, "ColorStyle"));
            e.Speed = Dbl(x, "Speed", 0);
            for (var i = 1; i <= 7; i++) e.Colors.Add(Color(x, $"Color{i}", ThemeColor.Black));
            e.Sensor = Sensor(x, e);
            return (e, Str(x, "ParentName"));
        }
    }

    // ---------------------------------------------------------------- XML helpers

    private static string Str(XElement parent, string name) => parent.Element(name)?.Value ?? "";

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static int Int(XElement parent, string name, int def) =>
        int.TryParse(parent.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    private static double Dbl(XElement parent, string name, double def) =>
        double.TryParse(parent.Element(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    private static bool Bool(XElement parent, string name, bool def) =>
        bool.TryParse(parent.Element(name)?.Value, out var v) ? v : def;

    private static T Enum<T>(XElement parent, string name, T def) where T : struct, Enum =>
        System.Enum.TryParse<T>(parent.Element(name)?.Value, ignoreCase: true, out var v) ? v : def;

    private static ThemeColor Color(XElement parent, string name, ThemeColor def)
    {
        var c = parent.Element(name);
        if (c is null) return def;
        return new ThemeColor(
            (byte)Int(c, "A", 255), (byte)Int(c, "R", 0), (byte)Int(c, "G", 0), (byte)Int(c, "B", 0));
    }

    private static (int X, int Y) Point(XElement parent, string name)
    {
        var p = parent.Element(name);
        return p is null ? (0, 0) : (Int(p, "X", 0), Int(p, "Y", 0));
    }

    private static (int W, int H) Size(XElement parent, string name, int defW, int defH)
    {
        var p = parent.Element(name);
        return p is null ? (defW, defH) : (Int(p, "Width", defW), Int(p, "Height", defH));
    }

    public static string SanitizeFolderName(string name)
    {
        var sb = new StringBuilder(name.Length);
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var ch in name.Trim())
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        var s = sb.ToString().Trim('.', ' ');
        return s.Length == 0 ? "theme" : s;
    }
}
