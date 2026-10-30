using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ZupuPaiBan.Data;

public sealed record BackupRestorePreflightReport(int FormatVersion, IReadOnlyList<string> Migrations,
    bool IncludesDatabase, bool IncludesAttachments, bool IncludesConfiguration);

/// <summary>只检查隔离副本；不对当前数据库执行任何操作。</summary>
public sealed class BackupRestorePreflightService
{
    private readonly string _scratchDirectory;
    private readonly Action<string> _deleteFile;

    public BackupRestorePreflightService(string scratchDirectory) : this(scratchDirectory, File.Delete) { }

    internal BackupRestorePreflightService(string scratchDirectory, Action<string> deleteFile)
    {
        if (!Path.IsPathFullyQualified(scratchDirectory)) throw new ArgumentException("临时目录必须是绝对路径。", nameof(scratchDirectory));
        _scratchDirectory = scratchDirectory;
        _deleteFile = deleteFile;
    }

    public async Task<BackupRestorePreflightReport> CheckAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_scratchDirectory)) throw new DirectoryNotFoundException("预检临时目录不存在。");
        await using var package = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var manifest = await OfflineBackupPackageService.ValidateStreamAsync(package, cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        var copyPath = Path.Combine(_scratchDirectory, $".pujiang-preflight-{id}.db");
        var referencePath = Path.Combine(_scratchDirectory, $".pujiang-reference-{id}.db");
        var ownsCopy = false;
        var ownsReference = false;
        Exception? primaryError = null;
        try
        {
            package.Position = 0;
            using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
            await using (var copy = new FileStream(copyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsCopy = true;
                await using var input = archive.GetEntry(manifest.DatabaseEntry)!.Open();
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    written += read;
                    if (written > manifest.DatabaseBytes) throw new InvalidDataException("数据库快照超出清单大小。");
                    await copy.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (written != manifest.DatabaseBytes) throw new InvalidDataException("数据库快照大小与清单不一致。");
            }
            // 仅接受单文件快照，避免只读检查依赖外部 WAL/SHM 文件。
            await using (var header = File.OpenRead(copyPath))
            {
                var bytes = new byte[20];
                if (header.Length < bytes.Length) throw new InvalidDataException("数据库快照文件过短。");
                await header.ReadExactlyAsync(bytes, cancellationToken);
                if (bytes[18] != 1 || bytes[19] != 1)
                    throw new InvalidDataException("数据库不是可独立检查的单文件 SQLite 快照。");
            }
            await using (var reservation = new FileStream(referencePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { ownsReference = true; }
            var options = new DbContextOptionsBuilder<ZupuDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = referencePath, Pooling = false }.ToString()).Options;
            await using var referenceContext = new ZupuDbContext(options);
            var expectedMigrations = referenceContext.Database.GetMigrations().ToArray();
            if (expectedMigrations.Length == 0) throw new InvalidOperationException("程序未提供可验证的迁移版本。");
            await referenceContext.Database.MigrateAsync(cancellationToken);
            await using var reference = OpenReadOnly(referencePath);
            await using var candidate = OpenReadOnly(copyPath);
            await reference.OpenAsync(cancellationToken);
            await candidate.OpenAsync(cancellationToken);
            await using (var integrity = candidate.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check";
                if (!string.Equals(await integrity.ExecuteScalarAsync(cancellationToken) as string, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("数据库完整性检查失败。");
            }
            await using (var foreignKeys = candidate.CreateCommand())
            {
                foreignKeys.CommandText = "PRAGMA foreign_key_check";
                await using var violations = await foreignKeys.ExecuteReaderAsync(cancellationToken);
                if (await violations.ReadAsync(cancellationToken)) throw new InvalidDataException("数据库存在外键完整性问题。");
            }
            var migrations = new List<string>();
            await using (var command = candidate.CreateCommand())
            {
                command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) migrations.Add(reader.GetString(0));
            }
            if (!migrations.SequenceEqual(expectedMigrations, StringComparer.Ordinal))
                throw new InvalidDataException("数据库迁移版本与当前程序不兼容。");
            var actualSchema = await ReadSchemaAsync(candidate, cancellationToken);
            var expectedSchema = await ReadSchemaAsync(reference, cancellationToken);
            if (!actualSchema.SequenceEqual(expectedSchema, StringComparer.Ordinal))
                throw new InvalidDataException("数据库结构与当前程序不兼容。");
            await using (var generation = candidate.CreateCommand())
            {
                generation.CommandText = $"SELECT COUNT(*) FROM ({UnlinkGenerationV1.GuardSql})";
                if (Convert.ToInt64(await generation.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new InvalidDataException("解除操作代数缺失或损坏。");
            }
            await using (var scopedGeneration = candidate.CreateCommand())
            {
                scopedGeneration.CommandText = $"SELECT COUNT(*) FROM ({ScopedUnlinkGenerationV1.DatabaseGuardSql})";
                if (Convert.ToInt64(await scopedGeneration.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new InvalidDataException("按谱解除代数缺失或损坏。");
            }
            return new BackupRestorePreflightReport(manifest.FormatVersion, migrations.AsReadOnly(), true,
                manifest.IncludesAttachments, manifest.IncludesConfiguration);
        }
        catch (Exception ex) { primaryError = ex; throw; }
        finally
        {
            var errors = new List<Exception>();
            foreach (var path in new[] { ownsCopy ? copyPath : null, ownsReference ? referencePath : null })
            {
                if (path is null) continue;
                try { _deleteFile(path); }
                catch (Exception ex) { errors.Add(new IOException($"无法清理预检临时文件：{path}", ex)); }
            }
            if (errors.Count > 0)
            {
                if (primaryError is not null) errors.Insert(0, primaryError);
                throw new AggregateException("预检临时文件清理失败，请按提示处理。", errors);
            }
        }
    }

    private static SqliteConnection OpenReadOnly(string path) => new(new SqliteConnectionStringBuilder
    {
        DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
    }.ToString());

    private static async Task<List<string>> ReadSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT type, name, sql FROM sqlite_master WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' ORDER BY type, name";
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add($"{reader.GetString(0)}\n{reader.GetString(1)}\n{reader.GetString(2)}");
        return result;
    }
}
