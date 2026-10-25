using System.IO;

namespace ZupuPaiBan.Infrastructure;

public enum AppLogLevel { Trace, Information, Warning, Error, Critical }

public static class FileAppLogger
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private static readonly object Sync = new();

    public static void Write(AppLogLevel level, string message, Exception? exception = null)
    {
        try
        {
            AppDataPaths.EnsureDirectories();
            var path = Path.Combine(AppDataPaths.LogsDirectory, "application.log");
            lock (Sync)
            {
                RotateIfNeeded(path);
                var detail = exception == null ? string.Empty : $"{Environment.NewLine}{exception}";
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} [{level}] {message}{detail}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never make the desktop application unavailable.
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxLogBytes) return;
        var archived = path + ".1";
        if (File.Exists(archived)) File.Delete(archived);
        File.Move(path, archived);
    }
}
