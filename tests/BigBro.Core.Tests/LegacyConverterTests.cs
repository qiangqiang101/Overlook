using System.Xml.Linq;
using BigBro.Core.Legacy;
using BigBro.Core.Theming;

namespace BigBro.Core.Tests;

public class LegacyConverterTests
{
    // 1x1 transparent PNG
    private const string Png1x1 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static string Color(string tag, int r, int g, int b, int a = 255) =>
        $"<{tag}><R>{r}</R><G>{g}</G><B>{b}</B><A>{a}</A></{tag}>";

    private static string Label(string name, int x, int y, string sensor = "None", string? parent = null, string text = "x") => $"""
        <MyTextLabel>
          {Color("BackColor", 255, 255, 255, 0)}
          <Font><Name>GeForce</Name><Size>19</Size><Unit>Point</Unit><Bold>true</Bold><Italic>false</Italic><Strikeout>false</Strikeout><Underline>false</Underline></Font>
          {Color("ForeColor", 10, 20, 30)}
          <BeforeText />
          <Text>{text}</Text>
          <Visible>true</Visible>
          <Name>{name}</Name>
          <Dock>None</Dock>
          <Location><X>{x}</X><Y>{y}</Y></Location>
          <Size><Width>145</Width><Height>30</Height></Size>
          <TextAlign>MiddleRight</TextAlign>
          <AutoSize>false</AutoSize>
          <Sensor>{sensor}</Sensor>
          <SensorParam>0</SensorParam>
          {(parent is null ? "" : $"<ParentName>{parent}</ParentName>")}
        </MyTextLabel>
        """;

    private static string Circular(string name, int x, int y, string sensor = "CPULoadPercent", string? parent = null) => $"""
        <MyCircularStatusBar>
          <Font><Name>Verdana</Name><Size>12</Size><Unit>Point</Unit></Font>
          {Color("ForeColor", 255, 255, 255)}
          <Name>{name}</Name>
          <Location><X>{x}</X><Y>{y}</Y></Location>
          <Size><Width>100</Width><Height>100</Height></Size>
          {Color("ForegroundColor", 40, 40, 40)}
          {Color("ProgressColor", 255, 0, 0)}
          {Color("ProgressColor2", 0, 0, 255)}
          <GradientMode>ForwardDiagonal</GradientMode>
          <BarWidth>270</BarWidth><Thickness>12</Thickness><StartAngle>135</StartAngle>
          <Unit>%</Unit><Minimum>0</Minimum><Maximum>100</Maximum>
          <TextMode>Value</TextMode>
          <Sensor>{sensor}</Sensor>
          <Style>Style2</Style>
          <SubText>CPU</SubText>
          <ProgressShape>Round</ProgressShape>
          {(parent is null ? "" : $"<ParentName>{parent}</ParentName>")}
        </MyCircularStatusBar>
        """;

    private static XDocument Theme(string labels = "", string circulars = "", string extra = "") => XDocument.Parse($"""
        <?xml version="1.0" encoding="utf-8"?>
        <ThemeData>
          <Name>Test</Name>
          <Tags><string>1920x480</string><string>green</string></Tags>
          <Author>me</Author>
          <Version>2.1</Version>
          <Size><Width>1920</Width><Height>480</Height></Size>
          <BackgroundImage>{Png1x1}</BackgroundImage>
          {Color("BackgroundColor", 0, 0, 0)}
          {Color("TextColor", 255, 255, 255)}
          <TextLabels>{labels}</TextLabels>
          <CircularSBs>{circulars}</CircularSBs>
          <UpdateInterval>1500</UpdateInterval>
          <Opacity>0.9</Opacity>
          <RGBBackground>true</RGBBackground>
          <RGBTransform>Rotate</RGBTransform>
          <RGBPattern>Cyberpunk</RGBPattern>
          {extra}
        </ThemeData>
        """.TrimStart());

    private static ConversionResult Convert(XDocument doc, IAssetSink? sink = null) =>
        LegacyThemeConverter.Convert(doc, sink ?? new DataUriAssetSink());

    [Fact]
    public void Theme_properties_carry_over()
    {
        var sink = new DataUriAssetSink();
        var t = Convert(Theme(), sink).Document;

        Assert.Equal("Test", t.Name);
        Assert.Equal("me", t.Author);
        Assert.Equal("2.1", t.Version);
        Assert.Equal(["1920x480", "green"], t.Tags);
        Assert.Equal((1920, 480), (t.Width, t.Height));
        Assert.Equal(ThemeColor.Black, t.BackgroundColor);
        Assert.Equal(ThemeColor.White, t.TextColor);
        Assert.Equal(1500, t.UpdateInterval);
        Assert.Equal(0.9, t.Opacity);
        Assert.True(t.Rgb is { Enabled: true });

        Assert.Equal(1, sink.Count);
        Assert.StartsWith("data:image/png;base64,", t.BackgroundImage);
        Assert.Equal(Png1x1, t.BackgroundImage![22..]);
    }

    [Fact]
    public void Text_label_fields_convert()
    {
        var r = Convert(Theme(Label("GPUClock", 1241, 45, "GPUClockSpeed")));
        var e = Assert.IsType<TextElement>(Assert.Single(r.Document.Elements));

        Assert.Equal("GPUClock", e.Name);
        Assert.Equal((1241, 45, 145, 30), (e.X, e.Y, e.Width, e.Height));
        // a hardware sensor can't be converted by itself: it is pending until the user picks one
        Assert.Null(e.Sensor);
        var pending = Assert.Single(r.Pending);
        Assert.Equal((LegacySensorKind.GpuClockSpeed, "0"), (pending.Kind, pending.Param));
        Assert.Same(e, pending.Element);
        Assert.Equal(ContentAlign.MiddleRight, e.TextAlign);
        Assert.Equal(new ThemeColor(255, 10, 20, 30), e.ForeColor);
        Assert.True(e.BackColor.IsTransparent);
        Assert.Equal("GeForce", e.Font.Family);
        Assert.Equal(19, e.Font.Size);
        Assert.True(e.Font.Bold);
        Assert.Contains("GeForce", r.FontFamilies);
    }

    [Fact]
    public void Text_of_a_sensor_bound_label_is_dropped_but_static_text_is_kept()
    {
        var r = Convert(Theme(
            Label("Bound", 0, 0, "CPULoadPercent", text: "17 %") +
            Label("Static", 0, 40, "None", text: "CPU")));
        var elements = r.Document.Elements.OfType<TextElement>().ToDictionary(e => e.Name);

        Assert.Equal("", elements["Bound"].Text);
        Assert.Equal("CPU", elements["Static"].Text);
        Assert.Null(elements["Static"].Sensor);
    }

    [Fact]
    public void Font_sizes_in_other_units_become_points()
    {
        string FontXml(string unit, double size) =>
            Label("L", 0, 0).Replace("<Size>19</Size><Unit>Point</Unit>", $"<Size>{size}</Size><Unit>{unit}</Unit>");

        double Pt(string unit, double size) =>
            ((TextElement)Convert(Theme(FontXml(unit, size))).Document.Elements.Single()).Font.Size;

        Assert.Equal(12, Pt("Point", 12));
        Assert.Equal(12, Pt("Pixel", 16));
        Assert.Equal(72, Pt("Inch", 1));
    }

    [Fact]
    public void First_added_control_is_in_front_so_json_lists_it_last()
    {
        // 1.x added later controls behind earlier ones: labels, then circular bars.
        var r = Convert(Theme(
            Label("LabelA", 0, 0) + Label("LabelB", 0, 40),
            Circular("BarA", 0, 100) + Circular("BarB", 0, 200)));

        // back-to-front: BarB, BarA, LabelB, LabelA
        Assert.Equal(["BarB", "BarA", "LabelB", "LabelA"], r.Document.Elements.Select(e => e.Name));
    }

    [Fact]
    public void Parent_links_become_children_with_relative_positions()
    {
        var r = Convert(Theme(
            Label("Caption", 260, 270, parent: "Gauge"),
            Circular("Gauge", 217, 120)));

        var gauge = Assert.IsType<CircularBarElement>(Assert.Single(r.Document.Elements));
        var caption = Assert.IsType<TextElement>(Assert.Single(gauge.Children));

        Assert.Equal((217, 120), (gauge.X, gauge.Y));
        Assert.Equal((43, 150), (caption.X, caption.Y));            // 1.x stored absolute positions
    }

    [Fact]
    public void Nested_parents_use_true_absolute_positions_at_every_level()
    {
        var r = Convert(Theme(
            Label("Leaf", 300, 300, parent: "Mid"),
            Circular("Mid", 200, 200, parent: "Outer") + Circular("Outer", 100, 100)));

        var outer = Assert.IsType<CircularBarElement>(Assert.Single(r.Document.Elements));
        var mid = Assert.IsType<CircularBarElement>(Assert.Single(outer.Children));
        var leaf = Assert.Single(mid.Children);

        Assert.Equal((100, 100), (mid.X, mid.Y));
        Assert.Equal((100, 100), (leaf.X, leaf.Y));
    }

    [Fact]
    public void Missing_or_circular_parents_fall_back_to_top_level_with_a_warning()
    {
        var r = Convert(Theme(
            Label("Orphan", 5, 5, parent: "Nope") +
            Label("A", 1, 1, parent: "B") + Label("B", 2, 2, parent: "A")));

        Assert.Equal(3, r.Document.Flatten().Count());
        Assert.Contains(r.Warnings, w => w.Contains("Orphan") && w.Contains("Nope"));
        Assert.Equal((5, 5), (r.Document.Elements.First(e => e.Name == "Orphan").X, r.Document.Elements.First(e => e.Name == "Orphan").Y));
        Assert.All(r.Document.Flatten(), e => Assert.True(e.Children.Count <= 1));
    }

    [Fact]
    public void Duplicate_names_are_made_unique()
    {
        var r = Convert(Theme(Label("Same", 0, 0) + Label("Same", 0, 40)));

        var names = r.Document.Flatten().Select(e => e.Name).ToList();
        Assert.Equal(2, names.Distinct().Count());
        Assert.Contains(r.Warnings, w => w.Contains("Same"));
    }

    [Fact]
    public void Unknown_sensor_is_unbound_with_a_warning()
    {
        var r = Convert(Theme(Label("L", 0, 0, "TotallyNewSensor")));

        Assert.Null(((TextElement)r.Document.Elements.Single()).Sensor);
        Assert.Contains(r.Warnings, w => w.Contains("TotallyNewSensor"));
    }

    [Fact]
    public void Circular_bar_maps_the_track_and_progress_colours()
    {
        var bar = (CircularBarElement)Convert(Theme(circulars: Circular("c", 0, 0))).Document.Elements.Single();

        Assert.Equal(new ThemeColor(255, 40, 40, 40), bar.TrackColor);
        Assert.Equal(new ThemeColor(255, 255, 0, 0), bar.ProgressColor);
        Assert.Equal(new ThemeColor(255, 0, 0, 255), bar.ProgressColor2);
        Assert.Equal(GradientDirection.ForwardDiagonal, bar.GradientMode);
        Assert.Equal(CircularStyle.Style2, bar.Style);
        Assert.Equal(ProgressShape.Round, bar.ProgressShape);
        Assert.Equal((270, 12, 135), (bar.BarWidth, bar.Thickness, bar.StartAngle));
        Assert.Equal("CPU", bar.SubText);
    }

    [Fact]
    public void Identical_images_are_stored_once()
    {
        var label = Label("L", 0, 0).Replace("<TextAlign>", $"<Image>{Png1x1}</Image><TextAlign>");
        var sink = new DataUriAssetSink();
        var r = Convert(Theme(label + label.Replace("\"L\"", "\"M\"").Replace(">L<", ">M<")), sink);

        Assert.Equal(1, sink.Count);
        Assert.Equal(2, r.Document.Elements.OfType<TextElement>().Count(e => e.Image == r.Document.BackgroundImage));
    }

    [Fact]
    public void Non_theme_xml_is_rejected()
        => Assert.Throws<InvalidDataException>(() => Convert(XDocument.Parse("<Other/>")));

    [Fact]
    public void Converted_theme_survives_a_json_round_trip()
    {
        var original = Convert(Theme(
            Label("Caption", 260, 270, "LongTime", parent: "Gauge"),
            Circular("Gauge", 217, 120)));
        LegacyThemeConverter.ApplySensors(original.Pending, (_, _) => "PC/CPU/Load/CPU Total");
        var doc = original.Document;
        original = null!;
        var json = ThemeJson.Serialize(doc);
        var back = ThemeJson.Deserialize(json);

        Assert.Equal(json, ThemeJson.Serialize(back));
        var gauge = Assert.IsType<CircularBarElement>(back.Elements.Single());
        var caption = Assert.IsType<TextElement>(gauge.Children.Single());
        Assert.Equal(SensorKind.LongTime, caption.Sensor!.Kind);
        Assert.Equal(SensorKind.Lhm, gauge.Sensor!.Kind);
        Assert.Equal("PC/CPU/Load/CPU Total", gauge.Sensor.Param);
        Assert.Contains("\"type\": \"circularBar\"", json);
        Assert.Contains("\"kind\": \"lhm\"", json);
        Assert.Contains("\"backgroundColor\": \"#FF000000\"", json);
    }

    [Fact]
    public void Json_accepts_hand_edited_files()
    {
        // type not first, comments, trailing commas, #RRGGBB colours
        const string json = """
            {
              "name": "Hand made", "width": 100, "height": 50,
              "backgroundColor": "#102030",
              // a comment
              "elements": [
                { "x": 1, "y": 2, "text": "hi", "type": "text", "foreColor": "#80FF0000", },
              ],
            }
            """;
        var t = ThemeJson.Deserialize(json);
        var e = Assert.IsType<TextElement>(t.Elements.Single());

        Assert.Equal(new ThemeColor(255, 0x10, 0x20, 0x30), t.BackgroundColor);
        Assert.Equal(new ThemeColor(0x80, 255, 0, 0), e.ForeColor);
        Assert.Equal((1, 2), (e.X, e.Y));
    }

    [Fact]
    public void Newer_format_versions_are_refused_with_a_clear_message()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bigbro-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "formatVersion": 99, "name": "future" }""");
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => ThemeJson.Load(path));
            Assert.Contains("99", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ConvertFile_writes_theme_json_and_assets_and_avoids_overwriting_in_a_batch()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bigbro-{Guid.NewGuid():N}");
        var xml = Path.Combine(dir, "in.xml");
        var outDir = Path.Combine(dir, "out");
        Directory.CreateDirectory(dir);
        Theme(Label("L", 0, 0, "CPULoadPercent")).Save(xml);
        try
        {
            var used = new HashSet<string>();
            var a = LegacyThemeConverter.ConvertFile(xml, outDir, used);
            var b = LegacyThemeConverter.ConvertFile(xml, outDir, used);

            Assert.NotEqual(a.OutputDirectory, b.OutputDirectory);
            Assert.True(File.Exists(Path.Combine(a.OutputDirectory!, "theme.json")));
            Assert.False(Directory.Exists(Path.Combine(a.OutputDirectory!, "assets")));      // images are inline now
            Assert.Equal(1, a.AssetCount);

            var loaded = ThemeJson.Load(Path.Combine(a.OutputDirectory!, "theme.json"));
            Assert.Equal(a.OutputDirectory, loaded.Directory);
            Assert.NotNull(ImageData.TryGetBytes(loaded.BackgroundImage, loaded));
            Assert.Null(ThemeStore.ResolveAsset(loaded, "../../in.xml"));         // can't escape the theme folder
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Binary_theme_gives_a_helpful_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bigbro-{Guid.NewGuid():N}.xml");
        File.WriteAllBytes(path, [0, 1, 0, 0, 0, 255, 255, 255, 255]);
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => LegacyThemeConverter.ConvertFile(path, Path.GetTempPath()));
            Assert.Contains("binary", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Image_values_are_read_in_every_supported_form()
    {
        var bytes = Convert64(Png1x1);
        var uri = ImageData.ToDataUri(bytes);
        Assert.StartsWith("data:image/png;base64,", uri);
        Assert.Equal(bytes, ImageData.TryGetBytes(uri, null));
        Assert.Null(ImageData.TryGetBytes("data:image/png;base64,@@@", null));
        Assert.Null(ImageData.TryGetBytes("assets/missing.png", new ThemeDocument { Directory = Path.GetTempPath() }));
        Assert.Null(ImageData.TryGetBytes(null, null));
        Assert.True(ImageData.IsGif("data:image/gif;base64,R0lG"));
        Assert.True(ImageData.IsGif("assets/a.gif"));
        Assert.False(ImageData.IsGif(uri));
    }

    private static byte[] Convert64(string s) => System.Convert.FromBase64String(s);

    [Fact]
    public void Editor_snapshots_reference_images_instead_of_copying_them()
    {
        var big = ImageData.ToDataUri(new byte[200_000].Select((_, i) => (byte)(i * 7)).Prepend((byte)0x89).Prepend((byte)0x50).ToArray());
        var doc = new ThemeDocument { Name = "T", BackgroundImage = big, Elements = [new ImageElement { Name = "I", Image = big }] };
        var images = new ImageTable();

        var snapshot = ThemeJson.Snapshot(doc, images);
        Assert.True(snapshot.Length < 2000, $"snapshot is {snapshot.Length} chars");
        Assert.Contains("@img:", snapshot);

        var back = ThemeJson.FromSnapshot(snapshot, images);
        Assert.Equal(big, back.BackgroundImage);
        Assert.Equal(big, ((ImageElement)back.Elements[0]).Image);

        // the real file keeps the images inline
        Assert.Contains(";base64,", ThemeJson.Serialize(doc));
    }

    [Fact]
    public void Special_sensors_convert_directly_and_hardware_ones_are_pending_once_per_element()
    {
        var r = Convert(Theme(
            Label("Clock", 0, 0, "CustomDateTime") +
            Label("A", 0, 40, "CPULoadPercent") + Label("B", 0, 80, "CPULoadPercent")));

        var clock = r.Document.Elements.OfType<TextElement>().Single(e => e.Name == "Clock");
        Assert.Equal(SensorKind.CustomDateTime, clock.Sensor!.Kind);
        Assert.Equal("0", clock.Sensor.Param);

        Assert.Equal(2, r.Pending.Count);
        Assert.Single(r.Pending.Select(p => p.Key).Distinct());          // one answer covers both
        Assert.All(r.Pending, p => Assert.Null(p.Element.Sensor));
    }

    [Fact]
    public void Applying_sensors_binds_pending_elements_and_null_leaves_them_unbound()
    {
        var r = Convert(Theme(Label("A", 0, 0, "CPULoadPercent") + Label("B", 0, 40, "GPULoadPercent")));

        LegacyThemeConverter.ApplySensors(r.Pending, (kind, _) => kind == LegacySensorKind.CpuLoadPercent ? "PC/CPU/Load/Total" : null);

        var byName = r.Document.Elements.ToDictionary(e => e.Name);
        Assert.Equal(("PC/CPU/Load/Total", SensorKind.Lhm), (byName["A"].Sensor!.Param, byName["A"].Sensor!.Kind));
        Assert.Null(byName["B"].Sensor);
        Assert.Equal("", ((TextElement)byName["A"]).Text);                 // the stale value that was saved with the theme is gone
    }

    [Fact]
    public void A_theme_file_with_a_1x_sensor_loads_unbound_and_remembers_what_it_was()
    {
        const string json = """{ "name": "old", "elements": [ { "type": "text", "name": "t", "sensor": { "kind": "cpuTemperatureC", "param": "1" } } ] }""";

        var doc = ThemeJson.Deserialize(json);

        var sensor = doc.Elements.Single().Sensor!;
        Assert.Equal(SensorKind.None, sensor.Kind);
        Assert.Equal("cpuTemperatureC", sensor.LegacyKind);
        Assert.Equal("1", sensor.Param);
        Assert.True(doc.NeedsSensors);
        Assert.Contains("\"legacyKind\": \"cpuTemperatureC\"", ThemeJson.Serialize(doc));      // survives a save until the user chooses
    }

    [Fact]
    public void Unknown_sensor_kinds_load_unbound()
    {
        var doc = ThemeJson.Deserialize("""{ "elements": [ { "type": "text", "sensor": { "kind": "somethingNew" } } ] }""");
        Assert.Equal(SensorKind.None, doc.Elements.Single().Sensor!.Kind);
        Assert.False(doc.NeedsSensors);
    }

    [Fact]
    public void Sensor_bindings_round_trip()
    {
        var doc = new ThemeDocument { Elements = [new TextElement { Name = "a", Sensor = new SensorBinding { Kind = SensorKind.Lhm, Param = "PC/CPU/Load/Total" } }] };
        var back = ThemeJson.Deserialize(ThemeJson.Serialize(doc));
        Assert.Equal((SensorKind.Lhm, "PC/CPU/Load/Total", null), (back.Elements[0].Sensor!.Kind, back.Elements[0].Sensor!.Param, back.Elements[0].Sensor!.LegacyKind));
    }
}
