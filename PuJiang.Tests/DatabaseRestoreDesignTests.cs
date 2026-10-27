using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

/// <summary>隔离验证恢复方案的文件和锁假设；不代表已实现生产恢复流程。</summary>
public class DatabaseRestoreDesignTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Unacceptable_package_does_not_touch_existing_database()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using (var context = database.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "原族谱" });
        Directory.CreateDirectory(database.BackupDirectory);
        var invalidPackage = Path.Combine(database.BackupDirectory, "invalid.zip");
        await File.WriteAllTextAsync(invalidPackage, "invalid");
        var original = await File.ReadAllBytesAsync(database.DatabasePath);
        using (var lease = DataDirectoryLease.Acquire(Path.GetDirectoryName(database.DatabasePath)!))
            await Assert.ThrowsAnyAsync<InvalidDataException>(() => new BackupRestorePreflightService(database.BackupDirectory).CheckAsync(invalidPackage));
        Assert.Equal(original, await File.ReadAllBytesAsync(database.DatabasePath));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Before_replace_snapshot_failure_leaves_destination_unchanged()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using (var context = database.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "原族谱" });
        Directory.CreateDirectory(database.BackupDirectory);
        var existingSnapshot = Path.Combine(database.BackupDirectory, "existing.db");
        await File.WriteAllTextAsync(existingSnapshot, "do not overwrite");
        var original = await File.ReadAllBytesAsync(database.DatabasePath);
        using (var lease = DataDirectoryLease.Acquire(Path.GetDirectoryName(database.DatabasePath)!))
            await Assert.ThrowsAnyAsync<IOException>(() => DatabaseSnapshotService.CreateAsync(database.DatabasePath, existingSnapshot));
        Assert.Equal(original, await File.ReadAllBytesAsync(database.DatabasePath));
        Assert.Equal("do not overwrite", await File.ReadAllTextAsync(existingSnapshot));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Isolated_same_volume_replace_and_rollback_preserve_both_versions()
    {
        await using var target = new TestDatabase();
        await using var incoming = new TestDatabase();
        await target.MigrateAsync();
        await incoming.MigrateAsync();
        await using (var context = target.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "替换前" });
        await using (var context = incoming.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "备份版本" });
        var targetRoot = Path.GetDirectoryName(target.DatabasePath)!;
        var staged = Path.Combine(targetRoot, "staged.db");
        var rollback = Path.Combine(targetRoot, "rollback.db");
        var failedVersion = Path.Combine(targetRoot, "failed-version.db");
        using (var lease = DataDirectoryLease.Acquire(targetRoot))
        {
            await DatabaseSnapshotService.CreateAsync(incoming.DatabasePath, staged);
            File.Replace(staged, target.DatabasePath, rollback);
            // 模拟替换后的检查失败；保留被替换版本以供诊断。
            File.Replace(rollback, target.DatabasePath, failedVersion);
        }
        await using (var context = target.CreateContext())
            Assert.Equal("替换前", (await context.Genealogies.SingleAsync()).Name);
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={failedVersion};Mode=ReadOnly;Pooling=False").Options;
        await using var failed = new ZupuDbContext(options);
        Assert.Equal("备份版本", (await failed.Genealogies.SingleAsync()).Name);
    }
}
