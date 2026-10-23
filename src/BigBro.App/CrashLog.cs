using System.Text;
using Avalonia.Threading;
using BigBro.Core.Settings;

namespace BigBro.App;

/// <summary>
/// Writes unhandled exceptions to <c>Logs/overlook.log</c> in the data folder, so a crash outside the debugger leaves a trace.
/// The log is trimmed to a fresh file once it passes 1 MB.
/// </summary>
internal static class CrashLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();
    private static string _path = "";

    public static string Directory { get; private set; } = "";

    public static void Install(string dataDirectory)
    {
        Directory = Path.Combine(dataDirectory, "Logs");
        _path = Path.Combine(Directory, "overlook.log");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(e.IsTerminating ? "Unhandled exception (app terminated)" : "Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Unobserved task exception", e.Exception);
    }

    /// <summary>Call once Avalonia is up; reports exceptions thrown on the UI thread, then lets them take their normal course.</summary>
    public static void HookUiThread() =>
        Dispatcher.UIThread.UnhandledException += (_, e) => Write("UI thread exception", e.Exception);

    public static void Write(string what, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes) File.Move(_path, _path + ".old", overwrite: true);

                var text = new StringBuilder()
                    .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {what}")
                    .AppendLine($"Overlook {BigBro.Core.Updates.UpdateChecker.CurrentVersion.ToString(3)}, {Environment.OSVersion}, .NET {Environment.Version}")
                    .AppendLine(ex?.ToString() ?? "(no exception object)")
                    .AppendLine();
                File.AppendAllText(_path, text.ToString());
            }
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
        {
            // nowhere to write: nothing more can be done
        }
    }
}
