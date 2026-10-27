using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;

namespace PuJiang.Tests;

public class DatabaseMigrationTests
{
    private static async Task CreateExistingDatabaseAsync(TestDatabase database)
    {
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE TestMarker (Value TEXT NOT NULL)");
        await context.Database.ExecuteSqlRawAsync("INSERT INTO TestMarker (Value) VALUES ('before migration')");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_database_is_backed_up_before_migration()
    {
        await using var database = new TestDatabase();
        await CreateExistingDatabaseAsync(database);
        await using var context = database.CreateContext();
        var backup = await new DatabaseMigrationService(context, database.BackupDirectory).MigrateAsync();

        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));
        Assert.Contains("20261025000100_InitialPuJiang", await context.Database.GetAppliedMigrationsAsync());
        await using var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await snapshot.OpenAsync();
        await using var command = snapshot.CreateCommand();
        command.CommandText = "SELECT Value FROM TestMarker";
        Assert.Equal("before migration", await command.ExecuteScalarAsync());
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Genealogies'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Backup_includes_committed_wal_data()
    {
        await using var database = new TestDatabase();
        await using var writer = database.CreateContext();
        await writer.Database.OpenConnectionAsync();
        await writer.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
        await writer.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint=0");
        await writer.Database.ExecuteSqlRawAsync("CREATE TABLE TestMarker (Value TEXT NOT NULL)");
        await writer.Database.ExecuteSqlRawAsync("INSERT INTO TestMarker (Value) VALUES ('wal data')");
        await using var context = database.CreateContext();
        var backup = await new DatabaseMigrationService(context, database.BackupDirectory).MigrateAsync();

        await using var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup!, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await snapshot.OpenAsync();
        await using var command = snapshot.CreateCommand();
        command.CommandText = "SELECT Value FROM TestMarker";
        Assert.Equal("wal data", await command.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Migration_failure_keeps_the_pre_migration_backup()
    {
        await using var database = new TestDatabase();
        await CreateExistingDatabaseAsync(database);
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE Genealogies (Value TEXT)");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseMigrationService(context, database.BackupDirectory).MigrateAsync());
        var backup = Assert.Single(Directory.GetFiles(database.BackupDirectory, "pre-migration-*.db"));
        Assert.Contains(backup, error.Message);
        await using var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await snapshot.OpenAsync();
        await using var command = snapshot.CreateCommand();
        command.CommandText = "SELECT Value FROM TestMarker";
        Assert.Equal("before migration", await command.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Backup_failure_prevents_migration_and_keeps_original_data()
    {
        await using var database = new TestDatabase();
        await CreateExistingDatabaseAsync(database);
        await File.WriteAllTextAsync(database.BackupDirectory, "not a directory");
        await using var context = database.CreateContext();
        await Assert.ThrowsAnyAsync<IOException>(() => new DatabaseMigrationService(context, database.BackupDirectory).MigrateAsync());

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Database.GetAppliedMigrationsAsync());
        await verification.Database.OpenConnectionAsync();
        await using var command = verification.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT Value FROM TestMarker";
        Assert.Equal("before migration", await command.ExecuteScalarAsync());
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Genealogies'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task New_and_up_to_date_database_do_not_create_backups()
    {
        await using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var service = new DatabaseMigrationService(context, database.BackupDirectory);
        Assert.Null(await service.MigrateAsync());
        Assert.Null(await service.MigrateAsync());
        Assert.False(Directory.Exists(database.BackupDirectory));
    }
}
