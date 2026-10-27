using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ZupuPaiBan.Data;

/// <summary>在升级已有 SQLite 数据库前创建并验证数据库快照。</summary>
public sealed class DatabaseMigrationService
{
    private readonly ZupuDbContext _context;
    private readonly string _backupDirectory;

    public DatabaseMigrationService(ZupuDbContext context, string backupDirectory)
    {
        _context = context;
        _backupDirectory = backupDirectory;
    }

    public async Task<string?> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var sourceConnection = _context.Database.GetDbConnection() as SqliteConnection
            ?? throw new InvalidOperationException("迁移前备份仅支持 SQLite 数据库。");
        var sourcePath = new SqliteConnectionStringBuilder(sourceConnection.ConnectionString).DataSource;
        if (!Path.IsPathFullyQualified(sourcePath))
            throw new InvalidOperationException("数据库必须使用绝对文件路径。");

        string? backupPath = null;
        if (File.Exists(sourcePath) && (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_backupDirectory);
            backupPath = Path.Combine(_backupDirectory, $"pre-migration-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
            await DatabaseSnapshotService.CreateAsync(sourcePath, backupPath, cancellationToken);
        }

        try
        {
            await _context.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception ex) when (backupPath != null && ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"数据库迁移失败。迁移前备份保存在：{backupPath}", ex);
        }
        return backupPath;
    }
}
