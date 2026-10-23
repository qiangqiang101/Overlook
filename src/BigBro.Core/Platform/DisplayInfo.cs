using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace BigBro.Core.Platform;

/// <summary>Current refresh rate of the primary display of the machine the app runs on.</summary>
public static partial class DisplayInfo
{
    public static double RefreshRate()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsRefreshRate();
            if (OperatingSystem.IsLinux()) return LinuxRefreshRate();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // no xrandr / no display server: report "unknown"
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DevMode mode);

    private static double WindowsRefreshRate()
    {
        var mode = new DevMode { dmSize = (ushort)Marshal.SizeOf<DevMode>() };
        return EnumDisplaySettingsW(null, -1, ref mode) ? mode.dmDisplayFrequency : 0;
    }

    private static double LinuxRefreshRate()
    {
        using var p = Process.Start(new ProcessStartInfo("xrandr", "--current")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        });
        if (p is null) return 0;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(2000);
        var m = ActiveMode().Match(output);
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hz) ? hz : 0;
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\*")]
    private static partial Regex ActiveMode();
}
