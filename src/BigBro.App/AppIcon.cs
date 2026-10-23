using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace BigBro.App;

internal static class AppIcon
{
    private static WindowIcon? _icon;

    public static WindowIcon? Load()
    {
        if (_icon is not null) return _icon;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Overlook/Assets/app.png"));
            return _icon = new WindowIcon(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or ArgumentException)
        {
            return null;
        }
    }
}
