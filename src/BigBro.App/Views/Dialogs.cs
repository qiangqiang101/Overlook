using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BigBro.Core.Settings;

namespace BigBro.App.Views;

/// <summary>Minimal modal dialogs (Avalonia ships none): message, choice and text prompt.</summary>
internal static class Dialogs
{
    public static Task ShowAsync(Window owner, string title, string message) =>
        ChoiceAsync(owner, title, message, Loc.T("common.ok", "OK"));

    /// <summary>Shows buttons and returns the index of the one pressed (-1 if the window was closed).</summary>
    public static async Task<int> ChoiceAsync(Window owner, string title, string message, params string[] buttons)
    {
        var result = -1;
        var window = Create(title);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 84, IsDefault = i == 0 };
            button.Click += (_, _) => { result = index; window.Close(); };
            row.Children.Add(button);
        }
        window.Content = Layout(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, row);
        await window.ShowDialog(owner);
        return result;
    }

    /// <summary>Asks for a line of text; null if cancelled.</summary>
    public static async Task<string?> AskAsync(Window owner, string title, string prompt, string initial)
    {
        string? result = null;
        var window = Create(title);
        var box = new TextBox { Text = initial, MinWidth = 320 };
        var ok = new Button { Content = Loc.T("common.ok", "OK"), MinWidth = 84, IsDefault = true };
        var cancel = new Button { Content = Loc.T("common.cancel", "Cancel"), MinWidth = 84 };
        ok.Click += (_, _) => { result = box.Text; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { result = box.Text; window.Close(); } };
        window.Opened += (_, _) => { box.Focus(); box.SelectAll(); };

        window.Content = Layout(
            new StackPanel { Spacing = 6, Children = { new TextBlock { Text = prompt }, box } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } });
        await window.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }

    private static Window Create(string title) => new()
    {
        Title = title,
        Width = 460,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Icon = AppIcon.Load(),
    };

    private static Control Layout(Control body, Control buttons) => new StackPanel
    {
        Margin = new Thickness(20),
        Spacing = 18,
        Children = { body, buttons },
    };
}
