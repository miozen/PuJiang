using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

[Collection("RestoreProcessExclusive")]
public class RestoreCombinedBoundaryTests
{
    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CheckpointBusyAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return reader.GetInt64(0);
    }

    private static async Task<string> PackageAsync(TestDatabase source)
    {
        await source.MigrateAsync();
        await using (var context = source.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "备份谱" });
        Directory.CreateDirectory(source.BackupDirectory);
        var package = Path.Combine(source.BackupDirectory, "source.zip");
        await OfflineBackupPackageService.CreateAsync(source.DatabasePath, package);
        return package;
    }

    private static async Task<string[]> NamesAsync(string path)
    {
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()).Options;
        await using var context = new ZupuDbContext(options);
        return await context.Genealogies.OrderBy(x => x.Name).Select(x => x.Name).ToArrayAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Busy_reader_stops_before_journal_then_closed_connections_preserve_wal_and_pending_record()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await target.MigrateAsync();
        var package = await PackageAsync(source);
        var root = Path.GetDirectoryName(target.DatabasePath)!;
        var cs = new SqliteConnectionStringBuilder { DataSource = target.DatabasePath, Pooling = true, DefaultTimeout = 0 }.ToString();
        await using var writer = new SqliteConnection(cs);
        await using var reader = new SqliteConnection(cs);
        try
        {
            await writer.OpenAsync();
            await reader.OpenAsync();
            await ExecuteAsync(writer, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; PRAGMA busy_timeout=0;");
            await ExecuteAsync(writer, "INSERT INTO Genealogies (Id, Name, CreatedAt) VALUES ('00000000-0000-0000-0000-000000000001', '甲', '2026-10-27')");
            await ExecuteAsync(reader, "BEGIN; SELECT count(*) FROM Genealogies;");
            await ExecuteAsync(writer, "INSERT INTO Genealogies (Id, Name, CreatedAt) VALUES ('00000000-0000-0000-0000-000000000002', '乙', '2026-10-27')");
            var blocked = new IsolatedRestorePrototype(target, async () =>
            {
                SqliteConnection.ClearPool(writer);
                Assert.Equal(ConnectionState.Open, writer.State);
                if (await CheckpointBusyAsync(writer) != 0) throw new InvalidOperationException("检查点繁忙，停止恢复。");
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => blocked.RunAsync(package));
            Assert.True(File.Exists(target.DatabasePath + "-wal"));
            Assert.Equal("none", RestoreJournalPrototype.Inspect(target).Status);
            Assert.Empty(Directory.GetFiles(root, "staged-*.db"));
            Assert.Empty(Directory.GetFiles(target.BackupDirectory, "before-*.db"));
            Assert.Equal(new[] { "乙", "甲" }, (await NamesAsync(target.DatabasePath)).OrderBy(x => x, StringComparer.Ordinal).ToArray());

            await ExecuteAsync(reader, "ROLLBACK");
            var stopped = new IsolatedRestorePrototype(target, async () =>
            {
                Assert.Equal(0, await CheckpointBusyAsync(writer));
                await reader.CloseAsync();
                await writer.CloseAsync();
                SqliteConnection.ClearPool(writer);
                Assert.Equal(ConnectionState.Closed, writer.State);
                Assert.Equal(ConnectionState.Closed, reader.State);
            }, stage => { if (stage == "pending-written") throw new IOException("模拟中断"); });
            await Assert.ThrowsAsync<IOException>(() => stopped.RunAsync(package));
            var inspection = RestoreJournalPrototype.Inspect(target);
            Assert.Equal("uncertain", inspection.Status);
            Assert.Contains("人工检查", inspection.Message);
            var snapshot = Assert.Single(Directory.GetFiles(target.BackupDirectory, "before-*.db"));
            Assert.Equal((await NamesAsync(target.DatabasePath)).OrderBy(x => x, StringComparer.Ordinal),
                (await NamesAsync(snapshot)).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Single(Directory.GetFiles(root, "staged-*.db"));
            Assert.Empty(Directory.GetFiles(root, "rollback-*.db"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new IsolatedRestorePrototype(target, () => Task.CompletedTask).RunAsync(package));
        }
        finally { await reader.CloseAsync(); await writer.CloseAsync(); SqliteConnection.ClearPool(writer); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Real_connection_closed_then_post_replace_failure_rolls_back_and_record_requires_inspection()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await target.MigrateAsync();
        var package = await PackageAsync(source);
        var cs = new SqliteConnectionStringBuilder { DataSource = target.DatabasePath, Pooling = true }.ToString();
        await using var writer = new SqliteConnection(cs);
        try
        {
            await writer.OpenAsync();
            await ExecuteAsync(writer, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;");
            await ExecuteAsync(writer, "INSERT INTO Genealogies (Id, Name, CreatedAt) VALUES ('00000000-0000-0000-0000-000000000003', '原谱', '2026-10-27')");
            var prototype = new IsolatedRestorePrototype(target, async () =>
            {
                Assert.Equal(0, await CheckpointBusyAsync(writer));
                await writer.CloseAsync();
                SqliteConnection.ClearPool(writer);
            }, stage => { if (stage == "after-replace") throw new IOException("模拟检查失败"); });
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => prototype.RunAsync(package));
            Assert.Contains("已回滚", error.Message);
            Assert.Equal(new[] { "原谱" }, await NamesAsync(target.DatabasePath));
            var snapshot = Assert.Single(Directory.GetFiles(target.BackupDirectory, "before-*.db"));
            Assert.Equal(new[] { "原谱" }, await NamesAsync(snapshot));
            var rejected = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target.DatabasePath)!, "rejected-*.db"));
            Assert.Equal(new[] { "备份谱" }, await NamesAsync(rejected));
            var inspection = RestoreJournalPrototype.Inspect(target);
            Assert.Equal("unknown", inspection.Status);
            Assert.Contains("人工检查", inspection.Message);
            using var lease = DataDirectoryLease.Acquire(Path.GetDirectoryName(target.DatabasePath)!);
        }
        finally { await writer.CloseAsync(); SqliteConnection.ClearPool(writer); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task New_reader_after_pending_record_blocks_replace_and_preserves_original_and_journal()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await target.MigrateAsync();
        var package = await PackageAsync(source);
        var cs = new SqliteConnectionStringBuilder { DataSource = target.DatabasePath, Pooling = false }.ToString();
        await using (var initial = new SqliteConnection(cs))
        {
            await initial.OpenAsync();
            await ExecuteAsync(initial, "PRAGMA journal_mode=DELETE; INSERT INTO Genealogies (Id, Name, CreatedAt) VALUES ('00000000-0000-0000-0000-000000000004', '原谱', '2026-10-27')");
        }
        await using var owned = new SqliteConnection(cs);
        await owned.OpenAsync();
        SqliteConnection? lateReader = null;
        try
        {
            var prototype = new IsolatedRestorePrototype(target, async () => await owned.CloseAsync(), stage =>
            {
                if (stage != "pending-written") return;
                Assert.Equal(ConnectionState.Closed, owned.State);
                lateReader = new SqliteConnection(cs);
                lateReader.Open();
                using var command = lateReader.CreateCommand();
                command.CommandText = "BEGIN; SELECT count(*) FROM Genealogies;";
                command.ExecuteNonQuery();
            });
            await Assert.ThrowsAnyAsync<IOException>(() => prototype.RunAsync(package));
            Assert.NotNull(lateReader);
            Assert.Equal(ConnectionState.Open, lateReader!.State);
            Assert.Equal("uncertain", RestoreJournalPrototype.Inspect(target).Status);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(target.DatabasePath)!, "staged-*.db"));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target.DatabasePath)!, "rollback-*.db"));
            var snapshot = Assert.Single(Directory.GetFiles(target.BackupDirectory, "before-*.db"));
            Assert.Equal(new[] { "原谱" }, await NamesAsync(snapshot));
        }
        finally { if (lateReader is not null) await lateReader.DisposeAsync(); }
        Assert.Equal(new[] { "原谱" }, await NamesAsync(target.DatabasePath));
    }
}
