using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;

namespace PuJiang.Tests;

public class BackupRestorePreflightTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Preflight_display_reports_coverage_and_does_not_claim_restoration()
    {
        var report = new BackupRestorePreflightReport(1, new[] { "migration" }, true, false, false);
        var message = ZupuPaiBan.Views.DatabaseBackupDialog.FormatPreflightReport(report);
        Assert.Contains("数据库：包含", message);
        Assert.Contains("照片和附件：不包含", message);
        Assert.Contains("配置：不包含", message);
        Assert.Contains("未恢复或替换数据库", message);
        Assert.Contains("不证明数据内容正确或备份来源真实性", message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Valid_package_reports_scope_and_leaves_source_and_package_untouched()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Directory.CreateDirectory(database.BackupDirectory);
        var package = Path.Combine(database.BackupDirectory, "backup.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
        var originalDb = await File.ReadAllBytesAsync(database.DatabasePath);
        var originalPackage = await File.ReadAllBytesAsync(package);
        var result = await new BackupRestorePreflightService(database.BackupDirectory).CheckAsync(package);
        Assert.True(result.IncludesDatabase);
        Assert.False(result.IncludesAttachments);
        Assert.False(result.IncludesConfiguration);
        Assert.Equal(1, result.FormatVersion);
        await using (var expected = database.CreateContext())
            Assert.Equal(await expected.Database.GetAppliedMigrationsAsync(), result.Migrations);
        Assert.Equal(originalDb, await File.ReadAllBytesAsync(database.DatabasePath));
        Assert.Equal(originalPackage, await File.ReadAllBytesAsync(package));
        Assert.Single(Directory.GetFiles(database.BackupDirectory));
    }

    [Theory]
    [InlineData("DROP TABLE FamilyUnions")]
    [InlineData("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('99999999999999_Unknown', '10.0.0')")]
    [Trait("Category", "Integration")]
    public async Task Unexpected_structure_or_migration_is_rejected(string sql)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using (var context = database.CreateContext()) await context.Database.ExecuteSqlRawAsync(sql);
        Directory.CreateDirectory(database.BackupDirectory);
        var package = Path.Combine(database.BackupDirectory, "backup.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupRestorePreflightService(database.BackupDirectory).CheckAsync(package));
        Assert.Single(Directory.GetFiles(database.BackupDirectory));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Corrupt_sqlite_with_matching_hash_and_unsupported_package_version_are_rejected()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Directory.CreateDirectory(database.BackupDirectory);
        var package = Path.Combine(database.BackupDirectory, "backup.zip");
        var bytes = new byte[512];
        bytes[18] = bytes[19] = 1;
        var manifest = new OfflineBackupManifest(1, "database/pujiang.db", bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)), DateTimeOffset.UtcNow, false, false);
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using (var entry = zip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(entry, manifest);
            using var db = zip.CreateEntry("database/pujiang.db").Open();
            db.Write(bytes);
        }
        await Assert.ThrowsAsync<SqliteException>(() => new BackupRestorePreflightService(database.BackupDirectory).CheckAsync(package));
        Assert.Single(Directory.GetFiles(database.BackupDirectory));
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            zip.GetEntry("manifest.json")!.Delete();
            using var entry = zip.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(entry, manifest with { FormatVersion = 99 });
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupRestorePreflightService(database.BackupDirectory).CheckAsync(package));
        Assert.Single(Directory.GetFiles(database.BackupDirectory));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cleanup_failure_is_reported_and_does_not_modify_source()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Directory.CreateDirectory(database.BackupDirectory);
        var package = Path.Combine(database.BackupDirectory, "backup.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
        var original = await File.ReadAllBytesAsync(database.DatabasePath);
        var service = new BackupRestorePreflightService(database.BackupDirectory, _ => throw new IOException("simulated cleanup failure"));
        var error = await Assert.ThrowsAsync<AggregateException>(() => service.CheckAsync(package));
        Assert.Contains("清理", error.Message);
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Equal(original, await File.ReadAllBytesAsync(database.DatabasePath));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cleanup_failure_preserves_original_preflight_error()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('99999999999999_Unknown', '10.0.0')");
        Directory.CreateDirectory(database.BackupDirectory);
        var package = Path.Combine(database.BackupDirectory, "backup.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
        var service = new BackupRestorePreflightService(database.BackupDirectory, _ => throw new IOException("simulated cleanup failure"));
        var error = await Assert.ThrowsAsync<AggregateException>(() => service.CheckAsync(package));
        Assert.Contains(error.InnerExceptions, e => e is InvalidDataException && e.Message.Contains("迁移版本"));
        Assert.Equal(3, error.InnerExceptions.Count);
    }
}
