using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class IsolatedRestorePrototypeTests
{
    private static async Task SetNameAsync(TestDatabase database, string name)
    {
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = name });
    }

    private static async Task<string> CreatePackageAsync(TestDatabase source)
    {
        Directory.CreateDirectory(source.BackupDirectory);
        var package = Path.Combine(source.BackupDirectory, "source.zip");
        await OfflineBackupPackageService.CreateAsync(source.DatabasePath, package);
        return package;
    }

    private static async Task<string> NameAsync(TestDatabase database)
    {
        await using var context = database.CreateContext();
        return (await context.Genealogies.SingleAsync()).Name;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Valid_backup_restores_only_after_stop_and_preserves_original_snapshot()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await SetNameAsync(target, "原数据");
        await SetNameAsync(source, "备份数据");
        var package = await CreatePackageAsync(source);
        var stopped = false;
        var service = new IsolatedRestorePrototype(target, () => { stopped = true; return Task.CompletedTask; }, stage =>
        {
            Assert.True(stopped);
            Assert.Throws<InvalidOperationException>(() => DataDirectoryLease.Acquire(Path.GetDirectoryName(target.DatabasePath)!));
        });
        await service.RunAsync(package);
        Assert.Equal("备份数据", await NameAsync(target));
        var snapshot = Assert.Single(Directory.GetFiles(target.BackupDirectory, "before-*.db"));
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={snapshot};Mode=ReadOnly;Pooling=False").Options;
        await using var verification = new ZupuDbContext(options);
        Assert.Equal("原数据", (await verification.Genealogies.SingleAsync()).Name);
    }

    [Theory]
    [InlineData("connections-stopped")]
    [InlineData("before-snapshot")]
    [InlineData("after-snapshot")]
    [InlineData("before-replace")]
    [InlineData("after-replace")]
    [Trait("Category", "Integration")]
    public async Task Injected_failures_leave_original_data_and_release_lock(string failureStage)
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await SetNameAsync(target, "原数据");
        await SetNameAsync(source, "备份数据");
        var package = await CreatePackageAsync(source);
        var service = new IsolatedRestorePrototype(target, () => Task.CompletedTask, stage =>
        {
            if (stage == failureStage) throw new IOException($"模拟故障：{stage}");
        });
        if (failureStage == "after-replace")
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(package));
        else
            await Assert.ThrowsAsync<IOException>(() => service.RunAsync(package));
        Assert.Equal("原数据", await NameAsync(target));
        using var lease = DataDirectoryLease.Acquire(Path.GetDirectoryName(target.DatabasePath)!);
        if (failureStage == "after-replace")
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target.DatabasePath)!, "rejected-*.db"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Invalid_package_and_stop_failure_do_not_replace_database()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await SetNameAsync(target, "原数据");
        await SetNameAsync(source, "备份数据");
        var root = Path.GetDirectoryName(source.DatabasePath)!;
        var invalid = Path.Combine(root, "invalid.zip");
        await File.WriteAllTextAsync(invalid, "invalid");
        var called = false;
        var service = new IsolatedRestorePrototype(target, () => { called = true; throw new IOException("停止失败"); });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(invalid));
        Assert.False(called);
        var valid = await CreatePackageAsync(source);
        await Assert.ThrowsAsync<IOException>(() => service.RunAsync(valid));
        Assert.True(called);
        Assert.Equal("原数据", await NameAsync(target));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Locked_target_and_existing_wal_stop_before_snapshot_and_replace()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await SetNameAsync(target, "原数据");
        await SetNameAsync(source, "备份数据");
        var package = await CreatePackageAsync(source);
        var stopped = false;
        var service = new IsolatedRestorePrototype(target, () => { stopped = true; return Task.CompletedTask; });
        using (var lease = DataDirectoryLease.Acquire(Path.GetDirectoryName(target.DatabasePath)!))
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(package));
        Assert.False(stopped);
        var wal = target.DatabasePath + "-wal";
        await File.WriteAllTextAsync(wal, "模拟未知 WAL");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(package));
        Assert.Contains("WAL", error.Message);
        Assert.Equal("模拟未知 WAL", await File.ReadAllTextAsync(wal));
        Assert.Empty(Directory.GetFiles(target.BackupDirectory, "before-*.db"));
        File.Delete(wal);
        Assert.Equal("原数据", await NameAsync(target));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Rollback_failure_reports_both_errors_and_retains_original_copy()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await SetNameAsync(target, "原数据");
        await SetNameAsync(source, "备份数据");
        var package = await CreatePackageAsync(source);
        var service = new IsolatedRestorePrototype(target, () => Task.CompletedTask, stage =>
        {
            if (stage is "after-replace" or "before-rollback") throw new IOException(stage);
        });
        var error = await Assert.ThrowsAsync<AggregateException>(() => service.RunAsync(package));
        Assert.Equal(2, error.InnerExceptions.Count);
        var original = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target.DatabasePath)!, "rollback-*.db"));
        Assert.Contains(original, error.Message);
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={original};Mode=ReadOnly;Pooling=False").Options;
        await using var verify = new ZupuDbContext(options);
        Assert.Equal("原数据", (await verify.Genealogies.SingleAsync()).Name);
    }
}
