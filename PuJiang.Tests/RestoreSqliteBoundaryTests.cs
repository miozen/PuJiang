using System.Data;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class RestoreSqliteBoundaryTests
{
    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(long Busy, long Log, long Checkpointed)> CheckpointAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async Task<string> CreatePackageAsync(TestDatabase source)
    {
        await source.MigrateAsync();
        await using (var context = source.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "备份数据" });
        Directory.CreateDirectory(source.BackupDirectory);
        var package = Path.Combine(source.BackupDirectory, "source.zip");
        await OfflineBackupPackageService.CreateAsync(source.DatabasePath, package);
        return package;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Pooled_open_connection_with_real_wal_blocks_restore_until_closed_and_pool_cleared()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await target.MigrateAsync();
        var package = await CreatePackageAsync(source);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = target.DatabasePath, Pooling = true }.ToString();
        await using var writer = new SqliteConnection(connectionString);
        try
        {
            await writer.OpenAsync();
            await ExecuteAsync(writer, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;");
            await ExecuteAsync(writer, "INSERT INTO Genealogies (Id, Name, CreatedAt) VALUES ('00000000-0000-0000-0000-000000000001', 'WAL已提交', '2026-10-27')");
            Assert.True(File.Exists(target.DatabasePath + "-wal"));
            SqliteConnection.ClearPool(writer);
            Assert.Equal(ConnectionState.Open, writer.State);
            var prototype = new IsolatedRestorePrototype(target, () => Task.CompletedTask);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => prototype.RunAsync(package));
            Assert.Contains("WAL", error.Message);
            await using (var command = writer.CreateCommand())
            {
                command.CommandText = "SELECT Name FROM Genealogies WHERE Id='00000000-0000-0000-0000-000000000001'";
                Assert.Equal("WAL已提交", await command.ExecuteScalarAsync());
            }
            var checkpoint = await CheckpointAsync(writer);
            Assert.Equal(0, checkpoint.Busy);
            await writer.CloseAsync();
            SqliteConnection.ClearPool(writer);
            Assert.False(File.Exists(target.DatabasePath + "-wal"));
            Assert.False(File.Exists(target.DatabasePath + "-shm"));
            await prototype.RunAsync(package);
            var snapshot = Assert.Single(Directory.GetFiles(target.BackupDirectory, "before-*.db"));
            var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={snapshot};Mode=ReadOnly;Pooling=False").Options;
            await using var original = new ZupuDbContext(options);
            Assert.Equal("WAL已提交", (await original.Genealogies.SingleAsync()).Name);
            await using var restored = target.CreateContext();
            Assert.Equal("备份数据", (await restored.Genealogies.SingleAsync()).Name);
        }
        finally { await writer.CloseAsync(); SqliteConnection.ClearPool(writer); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Active_reader_prevents_full_wal_checkpoint_until_reader_transaction_ends()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var cs = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = false, DefaultTimeout = 0 }.ToString();
        await using var writer = new SqliteConnection(cs);
        await using var reader = new SqliteConnection(cs);
        await writer.OpenAsync();
        await reader.OpenAsync();
        await ExecuteAsync(writer, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; PRAGMA busy_timeout=0;");
        await ExecuteAsync(writer, "INSERT INTO Genealogies (Id, Name, CreatedAt) VALUES ('00000000-0000-0000-0000-000000000001', '第一条', '2026-10-27')");
        await ExecuteAsync(reader, "BEGIN; SELECT count(*) FROM Genealogies;");
        await ExecuteAsync(writer, "INSERT INTO Genealogies (Id, Name, CreatedAt) VALUES ('00000000-0000-0000-0000-000000000002', '第二条', '2026-10-27')");
        var blocked = await CheckpointAsync(writer);
        Assert.Equal(1, blocked.Busy);
        Assert.True(blocked.Log > blocked.Checkpointed);
        Assert.True(File.Exists(database.DatabasePath + "-wal"));
        await ExecuteAsync(reader, "ROLLBACK");
        var completed = await CheckpointAsync(writer);
        Assert.Equal(0, completed.Busy);
        await reader.CloseAsync();
        await writer.CloseAsync();
        await using var context = database.CreateContext();
        Assert.Equal(2, await context.Genealogies.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Active_sqlite_reader_in_delete_journal_mode_blocks_target_replacement()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await target.MigrateAsync();
        await using (var context = target.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "原数据" });
        var package = await CreatePackageAsync(source);
        var cs = new SqliteConnectionStringBuilder { DataSource = target.DatabasePath, Pooling = false }.ToString();
        await using (var active = new SqliteConnection(cs))
        {
            await active.OpenAsync();
            await ExecuteAsync(active, "PRAGMA journal_mode=DELETE; BEGIN; SELECT count(*) FROM Genealogies;");
            var service = new IsolatedRestorePrototype(target, () => Task.CompletedTask);
            await Assert.ThrowsAnyAsync<IOException>(() => service.RunAsync(package));
            await ExecuteAsync(active, "ROLLBACK");
        }
        await using var verify = target.CreateContext();
        Assert.Equal("原数据", (await verify.Genealogies.SingleAsync()).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Other_process_exclusive_file_handle_blocks_snapshot_and_preserves_original_database()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await target.MigrateAsync();
        await using (var context = target.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "原数据" });
        var package = await CreatePackageAsync(source);
        var before = await File.ReadAllBytesAsync(target.DatabasePath);
        var quotedPath = target.DatabasePath.Replace("'", "''");
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"$handle = [System.IO.File]::Open('{quotedPath}', [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None); try {{ [Console]::Out.WriteLine('READY'); [Console]::Out.Flush(); [Console]::In.ReadLine() | Out-Null }} finally {{ $handle.Dispose() }}");
        using var holder = Process.Start(start) ?? throw new InvalidOperationException("无法启动文件占用测试进程。");
        try
        {
            var ready = await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("READY", ready);
            var service = new IsolatedRestorePrototype(target, () => Task.CompletedTask);
            await Assert.ThrowsAsync<SqliteException>(() => service.RunAsync(package));
        }
        finally
        {
            try
            {
                if (!holder.HasExited)
                {
                    await holder.StandardInput.WriteLineAsync("release");
                    holder.StandardInput.Close();
                    await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                }
            }
            finally
            {
                if (!holder.HasExited) { holder.Kill(); await holder.WaitForExitAsync(); }
            }
        }
        Assert.Equal(0, holder.ExitCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(target.DatabasePath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target.DatabasePath)!, "rollback-*.db"));
        await using var verify = target.CreateContext();
        Assert.Equal("原数据", (await verify.Genealogies.SingleAsync()).Name);
    }
}
