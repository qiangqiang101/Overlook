using Microsoft.Win32;

namespace BigBro.Core.Platform;

/// <summary>Starts the app at sign-in: the per-user Run key on Windows, an autostart .desktop file on Linux.</summary>
public static class AutoStart
{
    private const string AppName = "Overlook";

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public static bool IsEnabled()
    {
        if (OperatingSystem.IsWindows()) return WindowsKey(writable: false)?.GetValue(AppName) is not null;
        if (OperatingSystem.IsLinux()) return File.Exists(DesktopFile());
        return false;
    }

    public static void Set(bool enabled, string? executable = null)
    {
        executable ??= Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the program path.");

        if (OperatingSystem.IsWindows())
        {
            using var key = WindowsKey(writable: true) ?? throw new InvalidOperationException("Cannot open the Run registry key.");
            key.DeleteValue("BigBroMonitor", throwOnMissingValue: false);   // entry from before the rename
            if (enabled) key.SetValue(AppName, $"\"{executable}\"");
            else key.DeleteValue(AppName, throwOnMissingValue: false);
        }
        else if (OperatingSystem.IsLinux())
        {
            var file = DesktopFile();
            if (!enabled)
            {
                File.Delete(file);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file,
                "[Desktop Entry]\nType=Application\nName=Overlook\n" +
                $"Exec=\"{executable}\"\nX-GNOME-Autostart-enabled=true\n");
        }
    }

    private static RegistryKey? WindowsKey(bool writable) =>
        OperatingSystem.IsWindows()
            ? Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable)
            : null;

    private static string DesktopFile()
    {
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(config))
            config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(config, "autostart", "overlook.desktop");
    }
}
