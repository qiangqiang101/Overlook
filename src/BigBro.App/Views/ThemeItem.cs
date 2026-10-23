using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BigBro.Core.Theming;

namespace BigBro.App.Views;

/// <summary>One row of the theme list.</summary>
public sealed class ThemeItem : INotifyPropertyChanged
{
    private Bitmap? _preview;

    public ThemeItem(ThemeEntry entry)
    {
        Entry = entry;
        LoadPreviewAsync();
    }

    public ThemeEntry Entry { get; }
    public string Name => Entry.Name;
    public string Details => string.IsNullOrWhiteSpace(Entry.Document.Author)
        ? Entry.SizeLabel
        : $"{Entry.SizeLabel}  ·  {Entry.Document.Author}";

    public Bitmap? Preview
    {
        get => _preview;
        private set
        {
            _preview = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
        }
    }

    public bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var s = search.Trim();
        return Name.Contains(s, StringComparison.OrdinalIgnoreCase)
               || Entry.SizeLabel.Contains(s, StringComparison.OrdinalIgnoreCase)
               || (Entry.Document.Author?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
               || Entry.Document.Tags.Any(t => t.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadPreviewAsync()
    {
        var bytes = Entry.PreviewBytes;
        if (bytes is null) return;

        // Decoding a few dozen large screenshots would stall the window, so do it off the UI thread at thumbnail size.
        _ = Task.Run(() =>
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                var thumbnail = Bitmap.DecodeToWidth(stream, 220);
                Dispatcher.UIThread.Post(() => Preview = thumbnail);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
            {
                // A missing or broken preview just leaves the placeholder.
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
