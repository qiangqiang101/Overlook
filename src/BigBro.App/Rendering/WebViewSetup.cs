using Avalonia.Controls;
using Avalonia.Platform;

namespace BigBro.App.Rendering;

/// <summary>
/// Web views keep a browser profile on disk. By default WebView2 puts it next to the executable, which fails with "Access is
/// denied" (and takes the whole app down) when Overlook is installed under Program Files, so it goes in the data folder.
/// </summary>
internal static class WebViewSetup
{
    public static void Configure(NativeWebView web, string dataDirectory)
    {
        var profile = Path.Combine(dataDirectory, "WebView");
        web.EnvironmentRequested += (_, e) =>
        {
            try
            {
                Directory.CreateDirectory(profile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashLog.Write("Cannot create the web view data folder", ex);
                return;
            }

            switch (e)
            {
                case WindowsWebView2EnvironmentRequestedEventArgs windows:
                    windows.UserDataFolder = profile;
                    break;
                case GtkWebViewEnvironmentRequestedEventArgs gtk:
                    gtk.BaseDataDirectory = profile;
                    gtk.BaseCacheDirectory = Path.Combine(profile, "cache");
                    break;
                case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                    wpe.DataDirectory = profile;
                    wpe.CacheDirectory = Path.Combine(profile, "cache");
                    break;
            }
        };
    }
}
