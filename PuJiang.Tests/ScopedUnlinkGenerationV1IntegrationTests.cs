using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public sealed class ScopedUnlinkGenerationV1IntegrationTests
{
    private const string PreviousMigration = "20261030000100_AddUnlinkGeneration";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Old_database_upgrades_with_snapshot_and_legacy_writes_update_both_generations()
    {
        await using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "旧谱" });
        var person = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        Directory.CreateDirectory(database.BackupDirectory);

        var snapshot = await new DatabaseMigrationService(context, database.BackupDirectory).MigrateAsync();
        Assert.NotNull(snapshot);
        Assert.True(File.Exists(snapshot));
        var scopedBefore = await ScopedValueAsync(context, genealogy.Id);
        var globalBefore = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync();

        // This writer uses only the old business table and has no knowledge of the new shadow schema.
        await using (var legacyWriter = database.CreateContext())
            await legacyWriter.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"旧写入"} WHERE Id = {person.Id}");

        Assert.Equal(scopedBefore + 1, await ScopedValueAsync(context, genealogy.Id));
        Assert.Equal(globalBefore + 1, await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync());
        var package = Path.Combine(database.BackupDirectory, "v1.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
        Assert.True((await new BackupRestorePreflightService(database.BackupDirectory).CheckAsync(package)).IncludesDatabase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tombstone_survives_delete_and_same_id_recreation_and_downgrade_removes_only_scoped_schema()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var genealogy = new Genealogy { Id = Guid.NewGuid(), Name = "墓碑谱" };
        context.Genealogies.Add(genealogy);
        await context.SaveChangesAsync();
        Assert.Equal(0, await ScopedValueAsync(context, genealogy.Id));
        await context.Genealogies.Where(item => item.Id == genealogy.Id).ExecuteDeleteAsync();
        Assert.Equal(1L, await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM ScopedUnlinkGenerationTombstoneV1 WHERE GenealogyId = @id",
            new SqliteParameter("@id", genealogy.Id.ToString().ToUpperInvariant())).SingleAsync());

        context.ChangeTracker.Clear();
        context.Genealogies.Add(new Genealogy { Id = genealogy.Id, Name = "重建谱" });
        await context.SaveChangesAsync();
        Assert.Equal(2, await ScopedValueAsync(context, genealogy.Id));

        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(0L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name LIKE 'ScopedUnlinkGeneration%'").SingleAsync());
        Assert.Equal(9L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'UnlinkGeneration%'").SingleAsync());
        await migrator.MigrateAsync();
        Assert.Equal(11L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'ScopedUnlinkGenerationV1%'").SingleAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Reinstalling_rebuilt_people_table_restores_both_generation_trigger_families()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var genealogy = await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "重装谱" });
        var person = await new GenealogyDataService(context).AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        var tableSql = await context.Database.SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE type = 'table' AND name = 'People'").SingleAsync();
        var rebuiltSql = tableSql.Replace("\"People\"", "\"ScopedPeopleRebuilt\"", StringComparison.Ordinal);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await context.Database.ExecuteSqlRawAsync(rebuiltSql);
            await context.Database.ExecuteSqlRawAsync("INSERT INTO ScopedPeopleRebuilt SELECT * FROM People");
            await context.Database.ExecuteSqlRawAsync("DROP TABLE People");
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE ScopedPeopleRebuilt RENAME TO People");
            await transaction.CommitAsync();
        }
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
        Assert.Empty(await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).ToListAsync());
        Assert.Empty(await context.Database.SqlQueryRaw<long>(ScopedUnlinkGenerationV1.DatabaseGuardSql).ToListAsync());

        foreach (var sql in ScopedUnlinkGenerationV1.ReinstallTableTriggerSql("People"))
            await context.Database.ExecuteSqlRawAsync(sql);
        var scopedBefore = await ScopedValueAsync(context, genealogy.Id);
        var globalBefore = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"重装后"} WHERE Id = {person.Id}");
        Assert.Equal(scopedBefore + 1, await ScopedValueAsync(context, genealogy.Id));
        Assert.Equal(globalBefore + 1, await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync());
        await context.Database.CloseConnectionAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Migration_lock_timeout_is_reported_without_installing_scoped_schema()
    {
        await using var database = new TestDatabase();
        await using var seed = database.CreateContext();
        await seed.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await using var writer = database.CreateContext();
        await writer.Database.OpenConnectionAsync();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync("UPDATE UnlinkGeneration SET Value = Value WHERE Id = 1");
        await using var installing = database.CreateContext();
        Directory.CreateDirectory(database.BackupDirectory);
        var exception = await Assert.ThrowsAsync<DatabaseMigrationBlockedException>(() =>
            new DatabaseMigrationService(installing, database.BackupDirectory, busyTimeoutMilliseconds: 1).MigrateAsync());
        Assert.Contains("数据库正被另一写入占用", exception.Message);
        Assert.Equal(5, exception.SqliteErrorCode);
        await transaction.RollbackAsync();
        Assert.Equal(0L, await seed.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name = 'ScopedUnlinkGenerationV1'").SingleAsync());
    }

    private static Task<long> ScopedValueAsync(ZupuDbContext context, Guid genealogyId) =>
        context.Database.SqlQueryRaw<long>(ScopedUnlinkGenerationV1.GuardSql(),
            new SqliteParameter("@genealogy", genealogyId.ToString().ToUpperInvariant())).SingleAsync();
}
