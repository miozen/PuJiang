using System.IO;

namespace ZupuPaiBan.Infrastructure;

public static class AppDataPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PuJiang");
    public static string DatabasePath => Path.Combine(RootDirectory, "pujiang.db");
    public static string AttachmentsDirectory => Path.Combine(RootDirectory, "Attachments");
    public static string BackupsDirectory => Path.Combine(RootDirectory, "Backups");
    public static string ExportsDirectory => Path.Combine(RootDirectory, "Exports");
    public static string LogsDirectory => Path.Combine(RootDirectory, "Logs");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
