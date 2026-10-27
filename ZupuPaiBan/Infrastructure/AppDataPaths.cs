using System.IO;

namespace ZupuPaiBan.Infrastructure;

public static class AppDataPaths
{
    public const string TestRootEnvironmentVariable = "PUJIANG_TEST_DATA_ROOT";

    public static string RootDirectory => ResolveRootDirectory(
        Environment.GetEnvironmentVariable(TestRootEnvironmentVariable),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PuJiang"));

    public static string ResolveRootDirectory(string? testRoot, string defaultRoot)
    {
        if (testRoot is null) return defaultRoot;
        if (string.IsNullOrWhiteSpace(testRoot) || !Path.IsPathFullyQualified(testRoot))
            throw new InvalidOperationException($"{TestRootEnvironmentVariable} 必须是独立测试目录的绝对路径。");

        var testPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(testRoot));
        var productionPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(defaultRoot));
        if (IsWithin(testPath, productionPath) || IsWithin(productionPath, testPath))
            throw new InvalidOperationException("测试目录不得与正式数据目录重叠。");
        return testPath;
    }

    private static bool IsWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
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
