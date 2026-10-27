using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Data;

namespace PuJiang.Tests;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _directory;
    public TestDatabase() => _directory = Path.Combine(Path.GetTempPath(), "PuJiang.Tests", Guid.NewGuid().ToString("N"));

    // 子进程只能附着测试框架生成的临时根目录，不允许指定用户数据。
    internal TestDatabase(string existingDirectory)
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PuJiang.Tests"));
        var directory = Path.GetFullPath(existingDirectory);
        var relative = Path.GetRelativePath(parent, directory);
        if (Path.IsPathRooted(relative) || relative.Contains(Path.DirectorySeparatorChar) ||
            !Guid.TryParseExact(relative, "N", out _) || !Directory.Exists(directory) ||
            (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("只允许已存在的独立测试目录。");
        foreach (var child in new[] { Path.Combine(directory, "test.db"), Path.Combine(directory, "backups") })
            if ((File.Exists(child) || Directory.Exists(child)) && (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("测试数据库和备份目录不得是链接。");
        _directory = directory;
    }
    public string DatabasePath => Path.Combine(_directory, "test.db");
    public string BackupDirectory => Path.Combine(_directory, "backups");

    public ZupuDbContext CreateContext()
    {
        Directory.CreateDirectory(_directory);
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            // Each test owns this file; pooling would retain a native handle past context disposal.
            .UseSqlite($"Data Source={DatabasePath};Pooling=False")
            .EnableSensitiveDataLogging()
            .Options;
        return new ZupuDbContext(options);
    }

    public async Task MigrateAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}
