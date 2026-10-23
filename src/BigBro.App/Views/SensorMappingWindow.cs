using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BigBro.App.Editor;
using BigBro.Core.Legacy;
using BigBro.Core.Sensors;
using BigBro.Core.Settings;
using BigBro.Core.Theming;

namespace BigBro.App.Views;

/// <summary>One 1.x sensor that imported themes use, with how many elements and themes depend on it.</summary>
internal sealed record PendingGroup(string Key, LegacySensorKind Kind, string? Param, int Elements, int Themes);

/// <summary>
/// Shown when 1.x themes are imported: 1.x bound elements to hardware sensors by name ("CPU load"), 2.0 binds them to
/// LibreHardwareMonitor sensor paths, so the user confirms or changes the sensor for each one.
/// </summary>
internal sealed class SensorMappingWindow : Window
{
    private readonly Dictionary<string, SensorPicker> _pickers = new();
    private Dictionary<string, string?>? _result;

    internal SensorMappingWindow(IReadOnlyList<PendingGroup> groups, SensorSnapshot snapshot, string sourceText, bool sampleData)
    {
        Title = Loc.T("mapping.title", "Choose the sensors");
        Width = 920;
        Height = 700;
        MinWidth = 700;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = AppIcon.Load();

        var resolver = new SensorResolver(snapshot, new LocalInfo());

        var header = new StackPanel { Spacing = 8, Margin = new Thickness(24, 20, 24, 12) };
        header.Children.Add(new TextBlock { Text = Loc.T("mapping.heading", "Choose the sensors"), Classes = { "heading" } });
        header.Children.Add(new TextBlock
        {
            Text = Loc.T("mapping.intro",
                "1.x themes read hardware sensors by name (for example \"CPU load\"). Overlook reads them from LibreHardwareMonitor sensors instead, " +
                "and which sensor that is depends on your hardware. Check the suggestion for each one, or pick another sensor."),
            Classes = { "hint" },
        });
        header.Children.Add(new TextBlock { Text = sourceText, Foreground = sampleData ? Brushes.Orange : Brushes.LightGreen, TextWrapping = TextWrapping.Wrap });

        var rows = new StackPanel { Spacing = 8, Margin = new Thickness(24, 0, 24, 12) };
        rows.Children.Add(ColumnHeader());
        foreach (var g in groups.OrderByDescending(g => g.Elements))
        {
            var picker = new SensorPicker(snapshot, includeSpecials: false, Loc.T("mapping.unbound", "(leave unbound)"))
            {
                Value = resolver.SensorFor(g.Kind, g.Param) is { } s ? new SensorBinding { Kind = SensorKind.Lhm, Param = s.Path } : null,
            };
            _pickers[g.Key] = picker;
            rows.Children.Add(Row(g, picker, matched: picker.Value is not null));
        }

        var import = new Button { Content = Loc.T("mapping.import", "Import"), Classes = { "accent" }, MinWidth = 110, IsDefault = true };
        var unbound = new Button { Content = Loc.T("mapping.skip", "Import without sensors") };
        var cancel = new Button { Content = Loc.T("common.cancel", "Cancel"), MinWidth = 90, IsCancel = true };
        import.Click += (_, _) => { _result = Collect(); Close(); };
        unbound.Click += (_, _) => { _result = groups.ToDictionary(g => g.Key, _ => (string?)null); Close(); };
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24, 10, 24, 16), Children = { unbound, import, cancel },
        };

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = rows });
        Content = dock;
    }

    private static Control ColumnHeader()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*"), ColumnSpacing = 16 };
        grid.Children.Add(new TextBlock { Text = Loc.T("mapping.colOld", "Used by your 1.x themes"), Opacity = 0.7 });
        var right = new TextBlock { Text = Loc.T("mapping.colNew", "LibreHardwareMonitor sensor"), Opacity = 0.7 };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static Control Row(PendingGroup g, SensorPicker picker, bool matched)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*"), ColumnSpacing = 16 };
        var left = new StackPanel { Spacing = 2 };
        left.Children.Add(new TextBlock { Text = LegacySensors.Describe(g.Kind, g.Param), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        left.Children.Add(new TextBlock
        {
            Text = Loc.T("mapping.usedBy", "{0} element(s) in {1} theme(s)", g.Elements, g.Themes) +
                   (matched ? "" : "  ·  " + Loc.T("mapping.noMatch", "no matching sensor found")),
            Classes = { "hint" }, FontSize = 12,
        });
        grid.Children.Add(left);
        Grid.SetColumn(picker, 1);
        grid.Children.Add(picker);
        return grid;
    }

    private Dictionary<string, string?> Collect() =>
        _pickers.ToDictionary(kv => kv.Key, kv => kv.Value.Value is { Kind: SensorKind.Lhm, Param: { } p } ? p : null);

    /// <returns>The chosen sensor path per <see cref="PendingGroup.Key"/> (null = leave unbound), or null if cancelled.</returns>
    public static async Task<Dictionary<string, string?>?> AskAsync(Window owner, IReadOnlyList<PendingGroup> groups,
        SensorSnapshot snapshot, string sourceText, bool sampleData)
    {
        var window = new SensorMappingWindow(groups, snapshot, sourceText, sampleData);
        await window.ShowDialog(owner);
        return window._result;
    }
}
