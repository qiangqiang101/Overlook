using Avalonia;
using Avalonia.Controls;
using BigBro.Core.Settings;

namespace BigBro.App.Editor;

/// <summary>The editor's side panels: resizable with a splitter, and each can be lifted into its own window and put back.</summary>
public sealed partial class ThemeEditorWindow
{
    private const double MinPanelWidth = 180;

    private sealed class DockSlot
    {
        public required Border Host;
        public required Control Content;
        public required ColumnDefinition Column;
        public required GridSplitter Splitter;
        public required Button Toggle;
        public required string Title;
        public double Width;
        public Window? Floating;
    }

    private DockSlot _leftDock = null!, _rightDock = null!;

    private void InitDocking()
    {
        var s = _services.Settings;
        _leftDock = new DockSlot
        {
            Host = LeftHost, Content = LeftPanel, Column = BodyGrid.ColumnDefinitions[0], Splitter = LeftSplitter,
            Toggle = LeftDockButton, Title = "Elements", Width = Math.Max(MinPanelWidth, s.EditorLeftWidth),
        };
        _rightDock = new DockSlot
        {
            Host = RightHost, Content = RightPanel, Column = BodyGrid.ColumnDefinitions[4], Splitter = RightSplitter,
            Toggle = RightDockButton, Title = "Properties", Width = Math.Max(MinPanelWidth, s.EditorRightWidth),
        };

        foreach (var slot in new[] { _leftDock, _rightDock })
        {
            slot.Column.MinWidth = MinPanelWidth;
            slot.Column.Width = new GridLength(slot.Width);
            var captured = slot;
            slot.Toggle.Click += (_, _) => Toggle(captured);
        }
        // the middle column takes whatever is left, but never collapses
        BodyGrid.ColumnDefinitions[2].MinWidth = 240;
    }

    internal void UndockForScreenshot(string side)
    {
        if (side is "left" or "both") Undock(_leftDock);
        if (side is "right" or "both") Undock(_rightDock);
    }

    private void Toggle(DockSlot slot)
    {
        if (slot.Floating is { } window) window.Close();       // its Closed handler docks the panel again
        else Undock(slot);
    }

    private void Undock(DockSlot slot)
    {
        if (slot.Column.ActualWidth >= MinPanelWidth) slot.Width = slot.Column.ActualWidth;
        var height = Math.Max(420, Bounds.Height * 0.75);
        var window = new Window
        {
            Title = $"{Loc.T(slot == _leftDock ? "editor.elements" : "editor.properties", slot.Title)} - {Title}",
            Width = slot.Width + 16,
            Height = height,
            MinWidth = MinPanelWidth,
            MinHeight = 240,
            ShowInTaskbar = false,
            Icon = Icon,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(
                slot == _leftDock ? Position.X + 40 : Position.X + (int)((Bounds.Width - slot.Width - 40) * RenderScaling),
                Position.Y + 90),
        };

        slot.Host.Child = null;
        window.Content = slot.Content;
        slot.Floating = window;
        slot.Toggle.Content = Loc.T("editor.dock", "Dock");

        slot.Column.MinWidth = 0;
        slot.Column.Width = new GridLength(0);
        slot.Splitter.IsVisible = false;

        window.Closed += (_, _) =>
        {
            if (slot.Floating != window) return;
            window.Content = null;
            slot.Floating = null;
            slot.Host.Child = slot.Content;
            slot.Toggle.Content = Loc.T("editor.undock", "Undock");
            slot.Column.MinWidth = MinPanelWidth;
            slot.Column.Width = new GridLength(slot.Width);
            slot.Splitter.IsVisible = true;
        };
        window.Show(this);
    }

    /// <summary>Remembers the panel widths for the next time the editor opens.</summary>
    private void SaveDockLayout()
    {
        if (_leftDock is null) return;
        var s = _services.Settings;
        s.EditorLeftWidth = _leftDock.Floating is null && _leftDock.Column.ActualWidth >= MinPanelWidth ? _leftDock.Column.ActualWidth : _leftDock.Width;
        s.EditorRightWidth = _rightDock.Floating is null && _rightDock.Column.ActualWidth >= MinPanelWidth ? _rightDock.Column.ActualWidth : _rightDock.Width;
        try { _services.SaveSettings(); }
        catch (IOException) { }
    }

    private void ApplyDockLanguage()
    {
        ElementsHeading.Text = Loc.T("editor.elements", "Elements");
        PropertiesHeading.Text = Loc.T("editor.properties", "Properties");
        LeftDockButton.Content = Loc.T(_leftDock.Floating is null ? "editor.undock" : "editor.dock", _leftDock.Floating is null ? "Undock" : "Dock");
        RightDockButton.Content = Loc.T(_rightDock.Floating is null ? "editor.undock" : "editor.dock", _rightDock.Floating is null ? "Undock" : "Dock");
    }
}
