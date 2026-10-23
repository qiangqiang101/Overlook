using System.Collections;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BigBro.Core.Sensors;
using BigBro.Core.Settings;
using BigBro.Core.Theming;

namespace BigBro.App.Editor;

/// <summary>What the property editor needs from its host window.</summary>
public sealed class EditorContext
{
    public required Window Owner { get; init; }
    /// <summary>Applies a change to the document (the host records undo and repaints).</summary>
    public required Action<Action> Apply { get; init; }
    /// <summary>Copies an image file into the theme and returns the path to store.</summary>
    public required Func<string, string> ImportImage { get; init; }
    public required Func<IReadOnlyList<string>> FontFamilies { get; init; }
    /// <summary>The sensors to offer: the live LibreHardwareMonitor data, or a sample machine.</summary>
    public required Func<SensorSnapshot> Sensors { get; init; }
}

/// <summary>
/// Builds an editing UI for any theme object by reflection: bool, numbers, text, enums, colours, fonts, the sensor binding,
/// image paths, nested style objects and simple lists. New model properties show up without touching the editor.
/// </summary>
public static class PropertyEditor
{
    private static readonly HashSet<string> Hidden = ["Children", "Elements", "Directory", "FormatVersion", "SizePx"];
    private static readonly HashSet<string> ImageProperties = ["Image", "BackgroundImage", "Texture", "Preview"];

    /// <summary>Builds the editor for <paramref name="target"/>; <paramref name="rebuild"/> is called when the set of rows must change.</summary>
    public static StackPanel Build(object target, EditorContext ctx, Action rebuild, bool skipSensor = false)
    {
        var panel = new StackPanel { Spacing = 6 };
        AddRows(panel, target, ctx, rebuild, skipSensor);
        return panel;
    }

    private static void AddRows(Panel panel, object target, EditorContext ctx, Action rebuild, bool skipSensor)
    {
        foreach (var p in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && !Hidden.Contains(p.Name)
                                 && p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is null)
                     .OrderBy(p => p.MetadataToken))
        {
            if (skipSensor && p.PropertyType == typeof(SensorBinding)) continue;
            var control = EditorFor(target, p, ctx, rebuild);
            if (control is not null) panel.Children.Add(control);
        }
    }

    private static string Label(PropertyInfo p) => UiText.Property(p.Name);

    private static readonly NullabilityInfoContext Nullability = new();

    private static bool AllowsNull(PropertyInfo p) => Nullability.Create(p).WriteState == NullabilityState.Nullable;

    private static Control? EditorFor(object target, PropertyInfo p, EditorContext ctx, Action rebuild)
    {
        var type = p.PropertyType;
        var label = Label(p);

        if (type == typeof(bool))
        {
            var box = new CheckBox { Content = label, IsChecked = (bool)p.GetValue(target)! };
            box.IsCheckedChanged += (_, _) => ctx.Apply(() => p.SetValue(target, box.IsChecked == true));
            return box;
        }
        if (type == typeof(string))
        {
            var value = (string?)p.GetValue(target);
            if (ImageProperties.Contains(p.Name))
                return Row(label, ImageField(value, v => ctx.Apply(() => p.SetValue(target, v)), ctx, rebuild));
            var text = new TextBox { Text = value };
            Commit(text, () => ctx.Apply(() => p.SetValue(target, string.IsNullOrEmpty(text.Text) ? (AllowsNull(p) ? null : "") : text.Text)));
            return Row(label, text);
        }
        if (type == typeof(int) || type == typeof(double))
            return Row(label, Number(type, p.GetValue(target)!, v => ctx.Apply(() => p.SetValue(target, v))));
        if (type.IsEnum)
        {
            var choices = Enum.GetNames(type).Select(n => new EnumChoice(n, UiText.EnumValue(type, n))).ToList();
            var combo = new ComboBox
            {
                ItemsSource = choices, SelectedItem = choices.First(c => c.Name == p.GetValue(target)!.ToString()),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is EnumChoice chosen && chosen.Name != p.GetValue(target)!.ToString())
                    ctx.Apply(() => p.SetValue(target, Enum.Parse(type, chosen.Name)));
            };
            return Row(label, combo);
        }
        if (type == typeof(ThemeColor))
            return Row(label, ColorField((ThemeColor)p.GetValue(target)!, c => ctx.Apply(() => p.SetValue(target, c))));
        if (type == typeof(Offset))
        {
            var o = (Offset)p.GetValue(target)!;
            var x = Number(typeof(int), o.X, _ => { });
            var y = Number(typeof(int), o.Y, _ => { });
            void Set() => ctx.Apply(() => p.SetValue(target, new Offset((int)((NumericUpDown)x).Value!, (int)((NumericUpDown)y).Value!)));
            ((NumericUpDown)x).ValueChanged += (_, _) => Set();
            ((NumericUpDown)y).ValueChanged += (_, _) => Set();
            return Row(label, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = "x", VerticalAlignment = VerticalAlignment.Center }, x, new TextBlock { Text = "y", VerticalAlignment = VerticalAlignment.Center }, y } });
        }
        if (type == typeof(ThemeFont))
            return Group(label, BuildFont((ThemeFont)p.GetValue(target)!, ctx), expanded: false);
        if (type == typeof(SensorBinding))
            return Group(Loc.T("editor.sensor", "Sensor"), BuildSensor(target, p, ctx, rebuild), expanded: true);
        if (type == typeof(RgbBackground))
            return BuildOptionalNested(target, p, label, ctx, rebuild, () => new RgbBackground());
        if (type == typeof(List<string>))
        {
            var list = (List<string>)p.GetValue(target)!;
            var tags = new TextBox { Text = string.Join(", ", list) };
            Commit(tags, () => ctx.Apply(() =>
            {
                list.Clear();
                list.AddRange(tags.Text?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? []);
            }));
            return Row(label, tags);
        }
        if (type == typeof(List<ThemeColor>))
        {
            var colors = (List<ThemeColor>)p.GetValue(target)!;
            var inner = new StackPanel { Spacing = 4 };
            for (var i = 0; i < colors.Count; i++)
            {
                var index = i;
                inner.Children.Add(Row(Loc.T("editor.colorN", "Color {0}", i + 1), ColorField(colors[i], c => ctx.Apply(() => colors[index] = c))));
            }
            return Group(label, inner, expanded: false);
        }
        if (type == typeof(List<DynamicImage>))
            return Group(label, BuildDynamicImages((List<DynamicImage>)p.GetValue(target)!, ctx, rebuild), expanded: true);
        if (type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type) && p.GetValue(target) is { } nested)
            return Group(label, Build(nested, ctx, rebuild), expanded: false);
        return null;
    }

    /// <summary>An enum value in a drop-down: <see cref="ToString"/> is the translated text, <see cref="Name"/> the value.</summary>
    public sealed record EnumChoice(string Name, string Text)
    {
        public override string ToString() => Text;
    }

    // ------------------------------------------------------------------ pieces

    private static Control Row(string label, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("128,*"), ColumnSpacing = 8 };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.85 };
        ToolTip.SetTip(text, label);
        grid.Children.Add(text);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private static Control Group(string title, Control content, bool expanded) => new Expander
    {
        Header = title,
        IsExpanded = expanded,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Content = new Border { Padding = new Thickness(8, 4, 0, 4), Child = content },
    };

    private static void Commit(TextBox box, Action apply)
    {
        box.LostFocus += (_, _) => apply();
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) apply(); };
    }

    private static Control Number(Type type, object value, Action<object> set)
    {
        var isInt = type == typeof(int);
        var box = new NumericUpDown
        {
            Value = Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            Increment = isInt ? 1 : 0.1m,
            FormatString = isInt ? "0" : "0.##",
            Minimum = -100000,
            Maximum = 100000,
            ShowButtonSpinner = true,
            ButtonSpinnerLocation = Location.Right,
            MinWidth = 70,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is not { } v) return;
            set(isInt ? (object)(int)Math.Round(v) : (double)v);
        };
        return box;
    }

    private static Control ColorField(ThemeColor value, Action<ThemeColor> set)
    {
        var swatch = new Border
        {
            Width = 28, Height = 22, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray, Background = ToBrush(value),
        };
        var hex = new TextBox { Text = value.ToString(), MinWidth = 100 };

        var picker = new ColorView { Color = ToColor(value), IsAlphaVisible = true };
        var button = new Button { Content = swatch, Padding = new Thickness(2), Flyout = new Flyout { Content = picker } };

        var current = value;
        void Update(ThemeColor c, bool fromHex)
        {
            if (c == current) return;
            current = c;
            swatch.Background = ToBrush(c);
            if (!fromHex) hex.Text = c.ToString();
            else picker.Color = ToColor(c);
            set(c);
        }
        picker.ColorChanged += (_, e) =>
        {
            var c = new ThemeColor(e.NewColor.A, e.NewColor.R, e.NewColor.G, e.NewColor.B);
            // A fully transparent colour stays invisible whatever hue is picked; picking a hue means "I want to see it".
            if (c.A == 0 && current.A == 0 && (c.R != current.R || c.G != current.G || c.B != current.B))
            {
                c = c with { A = 255 };
                picker.Color = ToColor(c);
            }
            Update(c, fromHex: false);
        };
        Commit(hex, () =>
        {
            if (ThemeColor.TryParse(hex.Text, out var c)) Update(c, fromHex: true);
            else hex.Text = current.ToString();
        });

        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { button, hex } };
    }

    private static IBrush ToBrush(ThemeColor c) => new SolidColorBrush(ToColor(c));
    private static Color ToColor(ThemeColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static Control ImageField(string? value, Action<string?> set, EditorContext ctx, Action rebuild)
    {
        var name = new TextBlock
        {
            Text = DescribeImage(value),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var choose = new Button { Content = Loc.T("main.choose", "Choose...") };
        var clear = new Button { Content = "x" };
        ToolTip.SetTip(clear, Loc.T("editor.removeImage", "Remove image"));

        choose.Click += async (_, _) =>
        {
            var files = await ctx.Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("editor.chooseImage", "Choose an image"),
                FileTypeFilter = [FilePickerFileTypes.ImageAll],
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } local) return;
            string rel;
            try { rel = ctx.ImportImage(local); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { name.Text = ex.Message; return; }
            set(rel);
            name.Text = DescribeImage(rel);
        };
        clear.Click += (_, _) => { set(null); name.Text = DescribeImage(null); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        grid.Children.Add(name);
        Grid.SetColumn(choose, 1);
        Grid.SetColumn(clear, 2);
        grid.Children.Add(choose);
        grid.Children.Add(clear);
        return grid;
    }

    private static string DescribeImage(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Loc.T("editor.imageNone", "(none)");
        if (!ImageData.IsEmbedded(value)) return Path.GetFileName(value);
        var mime = value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? value[5..Math.Max(5, value.IndexOf(';'))] : "image";
        return Loc.T("editor.imageEmbedded", "{0} image, {1} KB (embedded)", mime.Replace("image/", "").ToUpperInvariant(), (value.Length * 3 / 4 / 1024).ToString("N0"));
    }

    private static Control BuildFont(ThemeFont font, EditorContext ctx)
    {
        var panel = new StackPanel { Spacing = 6 };

        var families = ctx.FontFamilies().ToList();
        if (!families.Contains(font.Family, StringComparer.OrdinalIgnoreCase)) families.Insert(0, font.Family);   // keep a font the theme needs even if it isn't installed here
        var family = new ComboBox
        {
            ItemsSource = families, SelectedItem = families.First(f => f.Equals(font.Family, StringComparison.OrdinalIgnoreCase)),
            HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 420,
        };
        family.SelectionChanged += (_, _) =>
        {
            if (family.SelectedItem is string chosen && chosen != font.Family) ctx.Apply(() => font.Family = chosen);
        };
        panel.Children.Add(Row(Loc.T("editor.family", "Family"), family));

        panel.Children.Add(Row(UiText.Property("Size"), Number(typeof(double), font.Size, v => ctx.Apply(() => font.Size = Math.Max(1, (double)v)))));

        CheckBox Flag(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = text, IsChecked = value };
            c.IsCheckedChanged += (_, _) => ctx.Apply(() => set(c.IsChecked == true));
            return c;
        }
        panel.Children.Add(new WrapPanel
        {
            Children =
            {
                Flag(UiText.Property("Bold"), font.Bold, v => font.Bold = v), Flag(UiText.Property("Italic"), font.Italic, v => font.Italic = v),
                Flag(UiText.Property("Underline"), font.Underline, v => font.Underline = v), Flag(UiText.Property("Strikeout"), font.Strikeout, v => font.Strikeout = v),
            },
        });
        return panel;
    }

    private static Control BuildSensor(object target, PropertyInfo p, EditorContext ctx, Action rebuild)
    {
        var snapshot = ctx.Sensors();
        var picker = new SensorPicker(snapshot, includeSpecials: true) { Value = (SensorBinding?)p.GetValue(target) };
        var param = new TextBox { PlaceholderText = Loc.T("editor.optional", "optional") };
        var hint = new TextBlock { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var paramRow = Row(Loc.T("editor.parameter", "Parameter"), param);

        void Refresh()
        {
            var b = (SensorBinding?)p.GetValue(target);
            var special = b is not null && SensorKinds.IsSpecial(b.Kind);
            paramRow.IsVisible = special;
            param.Text = b?.Param;
            hint.Text = b?.Kind == SensorKind.Lhm ? LhmHint(snapshot, b) : HintFor(b?.Kind);
        }

        picker.Picked += binding =>
        {
            ctx.Apply(() => p.SetValue(target, binding));
            Refresh();
        };

        Commit(param, () =>
        {
            if (p.GetValue(target) is not SensorBinding b) return;
            var text = string.IsNullOrWhiteSpace(param.Text) ? null : param.Text.Trim();
            if (text != b.Param) ctx.Apply(() => b.Param = text);
        });

        Refresh();
        return new StackPanel { Spacing = 6, Children = { Row(Loc.T("editor.source", "Source"), picker), paramRow, hint } };
    }

    private static string LhmHint(SensorSnapshot snapshot, SensorBinding b)
    {
        // a portable sensor ("@CpuLoadPercent") is looked up by role, so ask the resolver what it finds here
        if (PortableSensors.IsAlias(b.Param))
        {
            var reading = new SensorResolver(snapshot, new LocalInfo()).Resolve(b);
            return string.IsNullOrEmpty(reading.Text)
                ? Loc.T("editor.noPortable", "This PC has no matching sensor in the data shown here; it will read as empty.")
                : Loc.T("editor.currentValue", "Current value: {0}", reading.Text);
        }

        var sensor = snapshot.ByPath(b.Param ?? "") ?? snapshot.ById(b.Param ?? "");
        return sensor is null
            ? Loc.T("editor.noSuchSensor", "No sensor with this path in the data shown here; it will read as empty.")
            : Loc.T("editor.currentValue", "Current value: {0}", sensor.DisplayValue);
    }

    private static string HintFor(SensorKind? kind) => kind switch
    {
        null or SensorKind.None => Loc.T("editor.hintNone", "Not bound: shows the text you type."),
        SensorKind.HddTotalSize or SensorKind.HddTotalFreeSpace or SensorKind.HddUsage => Loc.T("editor.hintDrive", @"Parameter: drive of this computer, e.g. C:\ or /."),
        SensorKind.Ping => Loc.T("editor.hintPing", "Parameter: host to ping (default 8.8.8.8)."),
        SensorKind.CustomDateTime => Loc.T("editor.hintDate", "Parameter: .NET date format, e.g. dddd dd MMMM yyyy HH:mm:ss."),
        SensorKind.SystemUptime => Loc.T("editor.hintUptime", @"Parameter: time format, e.g. d\.hh\:mm\:ss."),
        _ => Loc.T("editor.hintNoParam", "No parameter needed."),
    };

    private static Control BuildOptionalNested(object target, PropertyInfo p, string label, EditorContext ctx, Action rebuild, Func<object> create)
    {
        var current = p.GetValue(target);
        var enable = new CheckBox { Content = label, IsChecked = current is not null };
        enable.IsCheckedChanged += (_, _) =>
        {
            ctx.Apply(() => p.SetValue(target, enable.IsChecked == true ? create() : null));
            rebuild();
        };
        var panel = new StackPanel { Spacing = 6, Children = { enable } };
        if (current is not null)
            panel.Children.Add(new Border { Padding = new Thickness(16, 0, 0, 0), Child = Build(current, ctx, rebuild) });
        return panel;
    }

    private static Control BuildDynamicImages(List<DynamicImage> images, EditorContext ctx, Action rebuild)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("editor.dynamicHint", "The sensor's value (0-100 %) picks the image: index 0 for the lowest, the highest index for the top."),
            Opacity = 0.65, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });

        foreach (var image in images.OrderBy(i => i.Index).ToList())
        {
            var remove = new Button { Content = Loc.T("editor.remove", "Remove") };
            remove.Click += (_, _) => { ctx.Apply(() => images.Remove(image)); rebuild(); };
            var inner = Build(image, ctx, rebuild);
            inner.Children.Add(remove);
            panel.Children.Add(Group($"[{image.Index}] {image.Name}", inner, expanded: false));
        }

        var add = new Button { Content = Loc.T("editor.addImage", "Add image") };
        add.Click += (_, _) =>
        {
            ctx.Apply(() => images.Add(new DynamicImage { Index = images.Count == 0 ? 0 : images.Max(i => i.Index) + 1, Name = $"Image {images.Count + 1}" }));
            rebuild();
        };
        panel.Children.Add(add);
        return panel;
    }
}
