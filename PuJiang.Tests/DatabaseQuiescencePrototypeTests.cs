using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

[Collection("RestoreProcessExclusive")]
public class DatabaseQuiescencePrototypeTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Stop_rejects_new_operations_waits_for_inflight_then_disposes_real_context_and_pool()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var cs = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = true }.ToString();
        await using var connection = new SqliteConnection(cs);
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite(connection).Options;
        var context = new ZupuDbContext(options);
        await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "隔离族谱" });
        var gate = new DatabaseQuiescencePrototype();
        var closed = false;
        gate.RegisterOwner(async () =>
        {
            await context.DisposeAsync();
            await connection.CloseAsync();
            SqliteConnection.ClearPool(connection);
            closed = true;
        });
        var operation = gate.EnterOperation();
        var stop = gate.StopAsync();
        Assert.Equal("draining", gate.State);
        Assert.False(stop.IsCompleted);
        Assert.False(closed);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Throws<InvalidOperationException>(() => gate.EnterOperation());
        Assert.Throws<InvalidOperationException>(() => gate.RegisterOwner(() => ValueTask.CompletedTask));
        Assert.Equal("隔离族谱", (await context.Genealogies.SingleAsync()).Name);
        operation.Dispose();
        operation.Dispose();
        await stop;
        Assert.True(closed);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Equal("stopped", gate.State);
        Assert.Throws<InvalidOperationException>(() => gate.EnterOperation());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await context.Genealogies.ToListAsync());
        await using var verify = database.CreateContext();
        Assert.Equal("隔离族谱", (await verify.Genealogies.SingleAsync()).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Stop_failure_blocks_recovery_and_preserves_original_database()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await target.MigrateAsync();
        await source.MigrateAsync();
        await using (var context = target.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "原数据" });
        Directory.CreateDirectory(source.BackupDirectory);
        var package = Path.Combine(source.BackupDirectory, "source.zip");
        await OfflineBackupPackageService.CreateAsync(source.DatabasePath, package);
        var before = await File.ReadAllBytesAsync(target.DatabasePath);
        var gate = new DatabaseQuiescencePrototype();
        gate.RegisterOwner(() => ValueTask.FromException(new IOException("模拟关闭失败")));
        var prototype = new IsolatedRestorePrototype(target, () => gate.StopAsync());
        await Assert.ThrowsAsync<IOException>(() => prototype.RunAsync(package));
        Assert.Equal("failed", gate.State);
        Assert.Throws<InvalidOperationException>(() => gate.EnterOperation());
        Assert.Equal(before, await File.ReadAllBytesAsync(target.DatabasePath));
        Assert.Equal("none", RestoreJournalPrototype.Inspect(target).Status);
        Assert.Empty(Directory.GetFiles(target.BackupDirectory, "before-*.db"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target.DatabasePath)!, "rollback-*.db"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Cancelled_drain_stays_failed_and_rejects_new_operations()
    {
        var gate = new DatabaseQuiescencePrototype();
        using var operation = gate.EnterOperation();
        using var cancellation = new CancellationTokenSource();
        var stop = gate.StopAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);
        Assert.Equal("failed", gate.State);
        Assert.Throws<InvalidOperationException>(() => gate.EnterOperation());
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.StopAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Unregistered_context_can_bypass_test_gate_demonstrating_existing_application_gap()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var gate = new DatabaseQuiescencePrototype();
        await gate.StopAsync();
        await using var unregistered = database.CreateContext();
        await new GenealogyDataService(unregistered).CreateGenealogyAsync(new Genealogy { Name = "未接入闸门" });
        Assert.Single(await unregistered.Genealogies.ToListAsync());
        Assert.Throws<InvalidOperationException>(() => gate.EnterOperation());
    }
}
