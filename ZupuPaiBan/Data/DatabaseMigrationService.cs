using System.IO;
using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ZupuPaiBan.Data;

/// <summary>在升级已有 SQLite 数据库前创建并验证数据库快照。</summary>
public sealed class DatabaseMigrationService
{
    internal const int MigrationBusyTimeoutMilliseconds = 15_000;
    private readonly ZupuDbContext _context;
    private readonly string _backupDirectory;
    private readonly int _busyTimeoutMilliseconds;

    public DatabaseMigrationService(ZupuDbContext context, string backupDirectory) : this(context, backupDirectory, MigrationBusyTimeoutMilliseconds) { }

    internal DatabaseMigrationService(ZupuDbContext context, string backupDirectory, int busyTimeoutMilliseconds)
    {
        if (busyTimeoutMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(busyTimeoutMilliseconds));
        _context = context;
        _backupDirectory = backupDirectory;
        _busyTimeoutMilliseconds = busyTimeoutMilliseconds;
    }

    public async Task<string?> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var sourceConnection = _context.Database.GetDbConnection() as SqliteConnection
            ?? throw new InvalidOperationException("迁移前备份仅支持 SQLite 数据库。");
        var sourcePath = new SqliteConnectionStringBuilder(sourceConnection.ConnectionString).DataSource;
        if (!Path.IsPathFullyQualified(sourcePath))
            throw new InvalidOperationException("数据库必须使用绝对文件路径。");

        var databaseExisted = File.Exists(sourcePath);
        string? backupPath = null;
        var openedByService = false;
        try
        {
            if (sourceConnection.State != ConnectionState.Open)
            {
                var connectionOptions = new SqliteConnectionStringBuilder(sourceConnection.ConnectionString)
                {
                    DefaultTimeout = Math.Max(1, (int)Math.Ceiling(_busyTimeoutMilliseconds / 1000d))
                };
                sourceConnection.ConnectionString = connectionOptions.ToString();
                await _context.Database.OpenConnectionAsync(cancellationToken);
                openedByService = true;
            }
            await using (var busyTimeout = sourceConnection.CreateCommand())
            {
                busyTimeout.CommandText = "PRAGMA busy_timeout = " + _busyTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture);
                await busyTimeout.ExecuteNonQueryAsync(cancellationToken);
            }
            if (databaseExisted && (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(_backupDirectory);
                var candidateBackupPath = Path.Combine(_backupDirectory, $"pre-migration-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
                await DatabaseSnapshotService.CreateAsync(sourcePath, candidateBackupPath, _busyTimeoutMilliseconds, cancellationToken);
                backupPath = candidateBackupPath;
            }

            await _context.Database.MigrateAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new DatabaseMigrationBlockedException(sourcePath, backupPath, ex);
        }
        catch (Exception ex) when (backupPath != null && ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"数据库迁移失败。迁移前备份保存在：{backupPath}", ex);
        }
        finally
        {
            if (openedByService) await _context.Database.CloseConnectionAsync();
        }
        return backupPath;
    }
}

public sealed class DatabaseMigrationBlockedException : InvalidOperationException
{
    public DatabaseMigrationBlockedException(string databasePath, string? snapshotPath, SqliteException innerException)
        : base($"数据库正被另一写入占用，升级未完成；关闭占用程序后重试。数据库：{databasePath}" +
               (snapshotPath is null ? string.Empty : $"；迁移前备份：{snapshotPath}"), innerException)
    {
        DatabasePath = databasePath;
        SnapshotPath = snapshotPath;
        SqliteErrorCode = innerException.SqliteErrorCode;
        SqliteExtendedErrorCode = innerException.SqliteExtendedErrorCode;
    }

    public string DatabasePath { get; }
    public string? SnapshotPath { get; }
    public int SqliteErrorCode { get; }
    public int SqliteExtendedErrorCode { get; }
}
