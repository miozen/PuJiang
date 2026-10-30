using System.IO;
using Microsoft.Data.Sqlite;

namespace ZupuPaiBan.Data;

/// <summary>生成并检查 SQLite 在线一致性快照；调用者负责指定新路径及清理。</summary>
public static class DatabaseSnapshotService
{
    public static Task CreateAsync(string sourcePath, string snapshotPath, CancellationToken cancellationToken = default) =>
        CreateAsync(sourcePath, snapshotPath, 15_000, cancellationToken);

    public static async Task CreateAsync(string sourcePath, string snapshotPath, int busyTimeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(sourcePath) || !Path.IsPathFullyQualified(snapshotPath))
            throw new InvalidOperationException("数据库和快照必须使用绝对路径。");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("源数据库不存在。", sourcePath);
        // 以 CreateNew 保证不会覆盖已有文件，也避免源与目标相同。
        await using (var reservation = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
        var timeoutSeconds = Math.Max(1, (int)Math.Ceiling(busyTimeoutMilliseconds / 1000d));
        var sourceOptions = new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = timeoutSeconds };
        var targetOptions = new SqliteConnectionStringBuilder { DataSource = snapshotPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = timeoutSeconds };
        await using (var source = new SqliteConnection(sourceOptions.ToString()))
        await using (var target = new SqliteConnection(targetOptions.ToString()))
        {
            await source.OpenAsync(cancellationToken);
            await target.OpenAsync(cancellationToken);
            await SetBusyTimeoutAsync(source, busyTimeoutMilliseconds, cancellationToken);
            await SetBusyTimeoutAsync(target, busyTimeoutMilliseconds, cancellationToken);
            source.BackupDatabase(target);
            await using var journal = target.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode=DELETE";
            await journal.ExecuteScalarAsync(cancellationToken);
        }
        var verifyOptions = new SqliteConnectionStringBuilder { DataSource = snapshotPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = timeoutSeconds };
        await using var verification = new SqliteConnection(verifyOptions.ToString());
        await verification.OpenAsync(cancellationToken);
        await SetBusyTimeoutAsync(verification, busyTimeoutMilliseconds, cancellationToken);
        await using var check = verification.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        var result = await check.ExecuteScalarAsync(cancellationToken);
        if (!string.Equals(result as string, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"数据库快照完整性检查失败：{result}。快照路径：{snapshotPath}");
        }
        catch { File.Delete(snapshotPath); throw; }
    }

    private static async Task SetBusyTimeoutAsync(SqliteConnection connection, int busyTimeoutMilliseconds, CancellationToken cancellationToken)
    {
        if (busyTimeoutMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(busyTimeoutMilliseconds));
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout = {busyTimeoutMilliseconds}";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
