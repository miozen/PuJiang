using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;

namespace PuJiang.Tests;

public class OfflineBackupPackageTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Package_captures_committed_wal_and_explicitly_excludes_attachments_and_configuration()
    {
        await using var database = new TestDatabase();
        await using var writer = database.CreateContext();
        await writer.Database.OpenConnectionAsync();
        await writer.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
        await writer.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint=0");
        await writer.Database.ExecuteSqlRawAsync("CREATE TABLE TestMarker (Value TEXT NOT NULL)");
        await writer.Database.ExecuteSqlRawAsync("INSERT INTO TestMarker VALUES ('wal value')");
        Directory.CreateDirectory(database.BackupDirectory);
        var package = Path.Combine(database.BackupDirectory, "test.zip");
        var created = await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
        var verified = await OfflineBackupPackageService.ValidateAsync(package);
        Assert.Equal(created, verified);
        Assert.Equal(1, verified.FormatVersion);
        Assert.False(verified.IncludesAttachments);
        Assert.False(verified.IncludesConfiguration);
        using var archive = ZipFile.OpenRead(package);
        var extracted = Path.Combine(database.BackupDirectory, "check.db");
        archive.GetEntry("database/pujiang.db")!.ExtractToFile(extracted);
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={extracted};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM TestMarker";
        Assert.Equal("wal value", await command.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Invalid_package_or_changed_database_is_rejected_without_writing_package()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Directory.CreateDirectory(database.BackupDirectory);
        var package = Path.Combine(database.BackupDirectory, "test.zip");
        var manifest = await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            archive.GetEntry("database/pujiang.db")!.Delete();
            using var stream = archive.CreateEntry("database/pujiang.db").Open();
            stream.Write(new byte[checked((int)manifest.DatabaseBytes)]);
        }
        var before = await File.ReadAllBytesAsync(package);
        await Assert.ThrowsAsync<InvalidDataException>(() => OfflineBackupPackageService.ValidateAsync(package));
        Assert.Equal(before, await File.ReadAllBytesAsync(package));
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            archive.GetEntry("manifest.json")!.Delete();
            using var stream = archive.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(stream, manifest with { FormatVersion = 99 });
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => OfflineBackupPackageService.ValidateAsync(package));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_destination_and_missing_source_do_not_overwrite_or_create_data()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Directory.CreateDirectory(database.BackupDirectory);
        var destination = Path.Combine(database.BackupDirectory, "existing.zip");
        await File.WriteAllTextAsync(destination, "existing");
        await Assert.ThrowsAnyAsync<IOException>(() => OfflineBackupPackageService.CreateAsync(database.DatabasePath, destination));
        Assert.Equal("existing", await File.ReadAllTextAsync(destination));
        await Assert.ThrowsAsync<FileNotFoundException>(() => OfflineBackupPackageService.CreateAsync(Path.Combine(database.BackupDirectory, "missing.db"), Path.Combine(database.BackupDirectory, "new.zip")));
        Assert.False(File.Exists(Path.Combine(database.BackupDirectory, "new.zip")));
        Assert.Single(Directory.GetFiles(database.BackupDirectory));
        await using var verify = database.CreateContext();
        Assert.NotEmpty(await verify.Database.GetAppliedMigrationsAsync());
    }
}
