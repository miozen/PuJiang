using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Data;

namespace PuJiang.Tests;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PuJiang.Tests", Guid.NewGuid().ToString("N"));
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
