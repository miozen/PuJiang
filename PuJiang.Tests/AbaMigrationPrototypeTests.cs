using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

// Test-only design: this is not an EF migration and is never installed by the application.
public class AbaMigrationPrototypeTests
{
    private static readonly string[] Tables = ["People", "PersonRelationships", "FamilyUnions"];
    private static readonly string[] Operations = ["INSERT", "UPDATE", "DELETE"];

    private static string TriggerSql(string table, string operation) =>
        $"CREATE TRIGGER AbaPrototype{table}{operation} AFTER {operation} ON {table} BEGIN SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM AbaPrototypeGeneration WHERE Id = 1 AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807) THEN RAISE(ABORT, 'invalid generation') END; UPDATE AbaPrototypeGeneration SET Value = Value + 1 WHERE Id = 1; END";

    private static async Task InstallPrototypeAsync(ZupuDbContext context, bool injectFailure = false)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE AbaPrototypeGeneration (Id INTEGER PRIMARY KEY CHECK (Id = 1), Value INTEGER NOT NULL CHECK (typeof(Value) = 'integer' AND Value >= 0)); INSERT INTO AbaPrototypeGeneration VALUES (1, 0);");
        foreach (var table in Tables)
        foreach (var operation in Operations)
        {
            // RAISE(ABORT) prevents the data write if the singleton is absent, malformed or cannot be incremented.
            await context.Database.ExecuteSqlRawAsync(TriggerSql(table, operation));
            if (injectFailure) throw new InvalidOperationException("simulated migration failure");
        }
        await transaction.CommitAsync();
    }

    private static Task<long> ReadGenerationAsync(ZupuDbContext context) => context.Database
        .SqlQueryRaw<long>("SELECT Value AS Value FROM AbaPrototypeGeneration WHERE Id = 1 AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807")
        .SingleAsync();

    private static Task<int> DeleteRelationshipAsync(ZupuDbContext context, Guid id, long generation)
    {
        var sql = "DELETE FROM PersonRelationships WHERE Id = @id AND EXISTS (SELECT 1 FROM AbaPrototypeGeneration WHERE Id = 1 AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807 AND Value = @generation)";
        var parameters = new List<object> { new SqliteParameter("@id", id.ToString().ToUpperInvariant()), new SqliteParameter("@generation", generation) };
        var index = 0;
        foreach (var table in Tables)
        foreach (var operation in Operations)
        {
            sql += $" AND EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'trigger' AND name = @name{index} AND sql = @sql{index})";
            parameters.Add(new SqliteParameter($"@name{index}", $"AbaPrototype{table}{operation}"));
            parameters.Add(new SqliteParameter($"@sql{index}", TriggerSql(table, operation)));
            index++;
        }
        return context.Database.ExecuteSqlRawAsync(sql, parameters.ToArray());
    }

    private static async Task<(Genealogy Genealogy, Person First, Person Second, PersonRelationship Relationship)> SeedAsync(ZupuDbContext context)
    {
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "虚构旧谱" });
        var first = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        var second = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "乙" });
        var relationship = await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id,
            PersonId = first.Id, RelatedPersonId = second.Id, Kind = PersonRelationshipKind.Spouse });
        return (genealogy, first, second, relationship);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_database_data_survives_prototype_install_and_failure_rolls_back_ddl()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var original = await SeedAsync(context);
        Directory.CreateDirectory(database.BackupDirectory);
        var backupPath = Path.Combine(database.BackupDirectory, "pre-prototype.db");
        await DatabaseSnapshotService.CreateAsync(database.DatabasePath, backupPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InstallPrototypeAsync(context, injectFailure: true));
        Assert.Equal(0L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name LIKE 'AbaPrototype%'").SingleAsync());
        Assert.Equal(2, await context.People.CountAsync());
        await InstallPrototypeAsync(context);
        Assert.Equal(0, await ReadGenerationAsync(context));
        Assert.Equal(original.Relationship.Id, (await context.PersonRelationships.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(10L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name LIKE 'AbaPrototype%'").SingleAsync());
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={backupPath};Mode=ReadOnly;Pooling=False").Options;
        await using var backup = new ZupuDbContext(options);
        Assert.Equal(0L, await backup.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name LIKE 'AbaPrototype%'").SingleAsync());
        Assert.Equal(original.Relationship.Id, (await backup.PersonRelationships.AsNoTracking().SingleAsync()).Id);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("negative")]
    [InlineData("text")]
    [InlineData("overflow")]
    [InlineData("missing-trigger")]
    [InlineData("altered-trigger")]
    [Trait("Category", "Integration")]
    public async Task Broken_generation_or_missing_trigger_blocks_guarded_delete(string fault)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var original = await SeedAsync(context);
        await InstallPrototypeAsync(context);
        var captured = await ReadGenerationAsync(context);
        if (fault == "missing") await context.Database.ExecuteSqlRawAsync("DELETE FROM AbaPrototypeGeneration");
        if (fault == "missing-trigger") await context.Database.ExecuteSqlRawAsync("DROP TRIGGER AbaPrototypePeopleUPDATE");
        if (fault == "altered-trigger")
        {
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER AbaPrototypePeopleUPDATE");
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER AbaPrototypePeopleUPDATE AFTER UPDATE ON People BEGIN SELECT 1; END");
        }
        if (fault is "negative" or "text" or "overflow")
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
            if (fault == "negative") await context.Database.ExecuteSqlRawAsync("UPDATE AbaPrototypeGeneration SET Value = -1");
            if (fault == "text") await context.Database.ExecuteSqlRawAsync("UPDATE AbaPrototypeGeneration SET Value = 'broken'");
            if (fault == "overflow") await context.Database.ExecuteSqlRawAsync("UPDATE AbaPrototypeGeneration SET Value = 9223372036854775807");
        }
        var attempted = fault == "overflow" ? long.MaxValue : fault == "negative" ? -1 : captured;
        if (fault is "missing" or "negative" or "text" or "overflow")
            await Assert.ThrowsAsync<InvalidOperationException>(() => ReadGenerationAsync(context));
        Assert.Equal(0, await DeleteRelationshipAsync(context, original.Relationship.Id, attempted));
        Assert.Equal(1, await context.PersonRelationships.CountAsync());
        if (fault is not ("missing-trigger" or "altered-trigger"))
        {
            var failure = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"外部"} WHERE Id = {original.First.Id}"));
            Assert.Contains("invalid generation", failure.Message);
            Assert.Equal("甲", (await context.People.AsNoTracking().SingleAsync(item => item.Id == original.First.Id)).Name);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task External_aba_and_delete_reinsert_invalidate_guard_but_new_generation_succeeds()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var original = await SeedAsync(context);
        await InstallPrototypeAsync(context);
        var captured = await ReadGenerationAsync(context);
        await using (var external = database.CreateContext())
        {
            await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"临时"} WHERE Id = {original.Second.Id}");
            await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"乙"} WHERE Id = {original.Second.Id}");
        }
        Assert.Equal(0, await DeleteRelationshipAsync(context, original.Relationship.Id, captured));
        captured = await ReadGenerationAsync(context);
        await using (var external = database.CreateContext())
        {
            var record = await external.PersonRelationships.AsNoTracking().SingleAsync();
            await external.PersonRelationships.Where(item => item.Id == record.Id).ExecuteDeleteAsync();
            external.PersonRelationships.Add(record);
            await external.SaveChangesAsync();
        }
        Assert.Equal(0, await DeleteRelationshipAsync(context, original.Relationship.Id, captured));
        Assert.Equal(1, await DeleteRelationshipAsync(context, original.Relationship.Id, await ReadGenerationAsync(context)));
        Assert.Equal(2, await context.People.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Concurrent_writer_lock_and_retry_never_delete_using_stale_generation()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var original = await SeedAsync(context);
        await InstallPrototypeAsync(context);
        var captured = await ReadGenerationAsync(context);
        await using var writer = database.CreateContext();
        await using var writeTransaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"乙新"} WHERE Id = {original.Second.Id}");

        // An independent connection with a bounded timeout encounters the writer's real SQLite lock.
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False;Default Timeout=1").Options;
        await using (var competitor = new ZupuDbContext(options))
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => DeleteRelationshipAsync(competitor, original.Relationship.Id, captured));
            Assert.Equal(5, error.SqliteErrorCode);
        }
        await writeTransaction.CommitAsync();
        Assert.Equal(0, await DeleteRelationshipAsync(context, original.Relationship.Id, captured));
        Assert.Single(await context.PersonRelationships.AsNoTracking().ToListAsync());
        Assert.Equal("乙新", (await context.People.AsNoTracking().SingleAsync(person => person.Id == original.Second.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_preflight_accepts_old_backup_and_rejects_prototype_schema_without_replacing_active_database()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Directory.CreateDirectory(database.BackupDirectory);
        await using (var context = database.CreateContext()) await SeedAsync(context);
        var legacyPackage = Path.Combine(database.BackupDirectory, "legacy.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, legacyPackage);
        var preflight = new BackupRestorePreflightService(database.BackupDirectory);
        Assert.True((await preflight.CheckAsync(legacyPackage)).IncludesDatabase);
        await using (var context = database.CreateContext()) await InstallPrototypeAsync(context);
        var prototypePackage = Path.Combine(database.BackupDirectory, "prototype.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, prototypePackage);
        var before = await File.ReadAllBytesAsync(database.DatabasePath);
        var packageBefore = await File.ReadAllBytesAsync(prototypePackage);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => preflight.CheckAsync(prototypePackage));
        Assert.Contains("结构", error.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(database.DatabasePath));
        Assert.Equal(packageBefore, await File.ReadAllBytesAsync(prototypePackage));
        Assert.Empty(Directory.EnumerateFiles(database.BackupDirectory, ".pujiang-preflight-*.db"));
        Assert.Empty(Directory.EnumerateFiles(database.BackupDirectory, ".pujiang-reference-*.db"));
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing", false)]
    [InlineData("invalid", false)]
    [InlineData("altered-trigger", false)]
    [Trait("Category", "Integration")]
    public async Task Test_only_backup_validation_checks_generation_and_trigger_contents(string fault, bool expected)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Directory.CreateDirectory(database.BackupDirectory);
        await using (var context = database.CreateContext())
        {
            await SeedAsync(context);
            await InstallPrototypeAsync(context);
            if (fault == "missing") await context.Database.ExecuteSqlRawAsync("DELETE FROM AbaPrototypeGeneration");
            if (fault == "invalid")
            {
                await context.Database.OpenConnectionAsync();
                await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
                await context.Database.ExecuteSqlRawAsync("UPDATE AbaPrototypeGeneration SET Value = -1");
            }
            if (fault == "altered-trigger")
            {
                await context.Database.ExecuteSqlRawAsync("DROP TRIGGER AbaPrototypePeopleUPDATE");
                await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER AbaPrototypePeopleUPDATE AFTER UPDATE ON People BEGIN SELECT 1; END");
            }
        }
        var package = Path.Combine(database.BackupDirectory, "candidate.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
        var before = await File.ReadAllBytesAsync(database.DatabasePath);
        var packageBefore = await File.ReadAllBytesAsync(package);
        Assert.Equal(expected, await ValidatePrototypePackageAsync(package, database.BackupDirectory));
        Assert.Equal(before, await File.ReadAllBytesAsync(database.DatabasePath));
        Assert.Equal(packageBefore, await File.ReadAllBytesAsync(package));
        Assert.Empty(Directory.EnumerateFiles(database.BackupDirectory, ".aba-prototype-*.db"));
    }

    // Narrow, test-only extension of package validation; it is not the production restore preflight.
    private static async Task<bool> ValidatePrototypePackageAsync(string package, string scratchDirectory)
    {
        var manifest = await OfflineBackupPackageService.ValidateAsync(package);
        var copyPath = Path.Combine(scratchDirectory, $".aba-prototype-{Guid.NewGuid():N}.db");
        var ownsCopy = false;
        try
        {
            using (var archive = ZipFile.OpenRead(package))
            await using (var output = new FileStream(copyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsCopy = true;
                await using var input = archive.GetEntry(manifest.DatabaseEntry)!.Open();
                await input.CopyToAsync(output);
            }
            var options = new DbContextOptionsBuilder<ZupuDbContext>()
                .UseSqlite($"Data Source={copyPath};Mode=ReadOnly;Pooling=False").Options;
            await using var copy = new ZupuDbContext(options);
            if (await copy.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM AbaPrototypeGeneration WHERE Id = 1 AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807").SingleAsync() != 1)
                return false;
            foreach (var table in Tables)
            foreach (var operation in Operations)
            {
                var name = $"AbaPrototype{table}{operation}";
                var expectedSql = TriggerSql(table, operation);
                var count = await copy.Database.SqlQuery<long>($"SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = {"trigger"} AND name = {name} AND sql = {expectedSql}").SingleAsync();
                if (count != 1) return false;
            }
            return true;
        }
        finally { if (ownsCopy) File.Delete(copyPath); }
    }
}
