using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BigBro.Core.Sensors;
using BigBro.Core.Settings;
using BigBro.Core.Theming;

namespace BigBro.App.Editor;

/// <summary>
/// A button that shows the chosen sensor and opens a searchable list of every sensor in the LibreHardwareMonitor data
/// (by tree path, with its current value) plus, optionally, the few special values (date, ping, weather...).
/// </summary>
public sealed class SensorPicker : Button
{
    private readonly Func<Task<SensorSnapshot>> _snapshot;
    private readonly bool _includeSpecials;
    private readonly string _noneText;
    private SensorBinding? _value;

    public SensorPicker(SensorSnapshot snapshot, bool includeSpecials, string noneText = "(none - shows the text you type)")
        : this(() => Task.FromResult(snapshot), includeSpecials, noneText) { }

    /// <param name="snapshot">Read each time the list is opened, so a late connection still shows its sensors.</param>
    public SensorPicker(Func<Task<SensorSnapshot>> snapshot, bool includeSpecials, string noneText = "(none - shows the text you type)")
    {
        _snapshot = snapshot;
        _includeSpecials = includeSpecials;
        _noneText = noneText;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Click += async (_, _) => await ChooseAsync();
        Describe();
    }

    /// <summary>Raised when the user picks something (not when <see cref="Value"/> is set from code).</summary>
    public event Action<SensorBinding?>? Picked;

    public SensorBinding? Value
    {
        get => _value;
        set
        {
            _value = value;
            Describe();
        }
    }

    private async Task ChooseAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = new SensorPickerWindow(await _snapshot(), _includeSpecials, _noneText, _value);
        var result = await dialog.ShowDialog<SensorPickerWindow.Choice?>(owner);
        if (result is not { } choice) return;

        Value = choice.Kind == SensorKind.None
            ? null
            : new SensorBinding { Kind = choice.Kind, Param = choice.Param ?? (choice.Kind == SensorKind.Lhm ? null : _value?.Param) };
        Picked?.Invoke(Value);
    }

    private void Describe()
    {
        var text = _value is null || _value.Kind == SensorKind.None ? _noneText
            : _value.Kind == SensorKind.Lhm ? (string.IsNullOrEmpty(_value.Param) ? "(choose a sensor)" : _value.Param)
            : $"Special: {_value.Kind}";
        Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(this, text);
    }
}

/// <summary>The list behind <see cref="SensorPicker"/>: search box, sensors with name, tree path and current value.</summary>
internal sealed class SensorPickerWindow : Window
{
    public sealed record Choice(string Name, string Path, string Value, SensorKind Kind, string? Param);

    public SensorPickerWindow(SensorSnapshot snapshot, bool includeSpecials, string noneText, SensorBinding? current)
    {
        Title = Loc.T("picker.title", "Choose a sensor");
        Width = 720;
        Height = 600;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var choices = new List<Choice> { new(noneText, "", "", SensorKind.None, null) };
        choices.AddRange(snapshot.Sensors.Select(s =>
        {
            var cut = s.Path.LastIndexOf('/');
            return new Choice(cut < 0 ? s.Path : s.Path[(cut + 1)..], cut < 0 ? "" : s.Path[..cut], s.DisplayValue, SensorKind.Lhm, s.Path);
        }));
        if (includeSpecials)
            choices.AddRange(SensorKinds.Specials.Select(k => new Choice(k.ToString(), Loc.T("picker.special", "Special"), "", k, null)));

        var search = new TextBox { PlaceholderText = Loc.T("picker.search", "Search: e.g. cpu load, gpu temperature, fan") };
        var count = new TextBlock { Opacity = 0.6, Margin = new Thickness(2, 6, 0, 0) };
        var list = new ListBox { ItemsSource = choices };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        list.ItemTemplate = new FuncDataTemplate<Choice>((c, _) =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, Margin = new Thickness(0, 3) };
            var left = new StackPanel { Spacing = 1 };
            left.Children.Add(new TextBlock { Text = c?.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(c?.Path))
                left.Children.Add(new TextBlock { Text = c.Path, FontSize = 12, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(left);
            var value = new TextBlock { Text = c?.Value, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            return row;
        });

        void Filter()
        {
            var words = (search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var shown = words.Length == 0
                ? choices
                : choices.Where(c => words.All(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || c.Path.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
            list.ItemsSource = shown;
            if (shown.Count > 0 && words.Length > 0) list.SelectedIndex = 0;
            count.Text = Loc.T("picker.count", "{0} of {1}", shown.Count, choices.Count);
        }
        search.TextChanged += (_, _) => Filter();
        Filter();

        var ok = new Button { Content = Loc.T("common.ok", "OK"), MinWidth = 90, IsDefault = false };
        ok.Classes.Add("accent");
        var cancel = new Button { Content = Loc.T("common.cancel", "Cancel"), MinWidth = 90, IsCancel = true };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);
        list.DoubleTapped += (_, _) => Accept();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
            else if (e.Key is Key.Down or Key.Up && !list.IsFocused && list.ItemCount > 0)
            {
                list.Focus();
            }
        };

        void Accept()
        {
            if (list.SelectedItem is Choice choice) Close(choice);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        var header = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 0, 8), Children = { search, count } };
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(header);
        root.Children.Add(buttons);
        root.Children.Add(list);
        Content = root;

        Opened += (_, _) =>
        {
            if (current is { Kind: SensorKind.Lhm, Param: { Length: > 0 } path })
            {
                var match = choices.FirstOrDefault(c => c.Param == path);
                if (match is not null) { list.SelectedItem = match; list.ScrollIntoView(match); }
            }
            search.Focus();
        };
    }
}
