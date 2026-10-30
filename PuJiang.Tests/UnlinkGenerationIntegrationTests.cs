using System.Diagnostics;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class UnlinkGenerationIntegrationTests
{
    private const string PreviousMigration = "20261028154821_RecordGenealogyImportMode";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Old_database_migrates_with_snapshot_and_old_backup_is_not_silently_accepted()
    {
        await using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "旧谱" });
        var person = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        Directory.CreateDirectory(database.BackupDirectory);
        var oldPackage = Path.Combine(database.BackupDirectory, "old.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, oldPackage);
        var snapshot = await new DatabaseMigrationService(context, database.BackupDirectory).MigrateAsync();
        Assert.NotNull(snapshot);
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={snapshot};Mode=ReadOnly;Pooling=False").Options;
        await using (var before = new ZupuDbContext(options))
        {
            Assert.Equal("甲", (await before.People.AsNoTracking().SingleAsync()).Name);
            Assert.Equal(0L, await before.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name = 'UnlinkGeneration'").SingleAsync());
        }
        Assert.Equal(person.Id, (await context.People.AsNoTracking().SingleAsync()).Id);
        Assert.Single(await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").ToListAsync());
        var preflight = new BackupRestorePreflightService(database.BackupDirectory);
        var oldError = await Assert.ThrowsAsync<InvalidDataException>(() => preflight.CheckAsync(oldPackage));
        Assert.Contains("迁移版本", oldError.Message);
        var newPackage = Path.Combine(database.BackupDirectory, "new.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, newPackage);
        var beforeDb = await File.ReadAllBytesAsync(database.DatabasePath);
        Assert.True((await preflight.CheckAsync(newPackage)).IncludesDatabase);
        Assert.Equal(beforeDb, await File.ReadAllBytesAsync(database.DatabasePath));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("negative")]
    [InlineData("trigger")]
    [Trait("Category", "Integration")]
    public async Task Real_preflight_and_snapshot_reject_corrupt_generation_or_trigger(string fault)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Directory.CreateDirectory(database.BackupDirectory);
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "隔离谱" });
        await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        if (fault == "missing") await context.Database.ExecuteSqlRawAsync("DELETE FROM UnlinkGeneration");
        if (fault == "negative")
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
            await context.Database.ExecuteSqlRawAsync("UPDATE UnlinkGeneration SET Value = -1");
            await context.Database.CloseConnectionAsync();
        }
        if (fault == "trigger") await context.Database.ExecuteSqlRawAsync("DROP TRIGGER UnlinkGenerationPeopleUPDATE");
        await Assert.ThrowsAnyAsync<Exception>(() => service.GetGraphSnapshotAsync(genealogy.Id));
        var package = Path.Combine(database.BackupDirectory, "invalid.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
        var dbBefore = await File.ReadAllBytesAsync(database.DatabasePath);
        var packageBefore = await File.ReadAllBytesAsync(package);
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => new BackupRestorePreflightService(database.BackupDirectory).CheckAsync(package));
        Assert.Equal(dbBefore, await File.ReadAllBytesAsync(database.DatabasePath));
        Assert.Equal(packageBefore, await File.ReadAllBytesAsync(package));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Other_process_rename_aba_invalidates_confirmed_relationship()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "跨进程谱" };
        await vm.CreateAsync();
        foreach (var name in new[] { "甲", "乙" }) { vm.NewPersonName = name; await vm.AddPersonAsync(); }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        var candidate = Assert.Single(await vm.GetTargetLinkRemovalCandidatesAsync());
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(RestoreWorkerProgram).Assembly.Location);
        start.ArgumentList.Add("--aba-rename");
        start.ArgumentList.Add(Path.GetDirectoryName(database.DatabasePath)!);
        start.ArgumentList.Add(second.Id.ToString());
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动隔离工作进程。");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        Assert.True(process.ExitCode == 0, $"隔离进程失败：{await stderr} {await stdout}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(candidate));
        await using var verify = database.CreateContext();
        Assert.Single(await verify.PersonRelationships.ToListAsync());
        Assert.Equal("乙", (await verify.People.SingleAsync(person => person.Id == second.Id)).Name);
    }

    [Theory]
    [InlineData("delete-reinsert")]
    [InlineData("missing-generation")]
    [InlineData("altered-trigger")]
    [Trait("Category", "Integration")]
    public async Task Confirmed_relationship_rejects_reinsert_or_generation_schema_tampering(string fault)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "防护谱" };
        await vm.CreateAsync();
        foreach (var name in new[] { "甲", "乙" }) { vm.NewPersonName = name; await vm.AddPersonAsync(); }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        var candidate = Assert.Single(await vm.GetTargetLinkRemovalCandidatesAsync());
        await using (var external = database.CreateContext())
        {
            if (fault == "delete-reinsert")
            {
                var original = await external.PersonRelationships.AsNoTracking().SingleAsync();
                await external.PersonRelationships.Where(item => item.Id == original.Id).ExecuteDeleteAsync();
                external.PersonRelationships.Add(original);
                await external.SaveChangesAsync();
            }
            if (fault == "missing-generation") await external.Database.ExecuteSqlRawAsync("DELETE FROM UnlinkGeneration");
            if (fault == "altered-trigger")
            {
                await external.Database.ExecuteSqlRawAsync("DROP TRIGGER UnlinkGenerationPeopleUPDATE");
                await external.Database.ExecuteSqlRawAsync("CREATE TRIGGER UnlinkGenerationPeopleUPDATE AFTER UPDATE ON People BEGIN SELECT 1; END");
            }
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(candidate));
        await using var verify = database.CreateContext();
        Assert.Single(await verify.PersonRelationships.AsNoTracking().ToListAsync());
        Assert.Equal(2, await verify.People.CountAsync());
        if (fault != "delete-reinsert")
        {
            Assert.Empty(vm.TargetTree);
            Assert.Empty(vm.People);
            Assert.Contains("刷新失败", vm.Status);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Graph_and_generation_are_read_in_one_transaction_during_external_write()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var seed = database.CreateContext();
        var service = new GenealogyDataService(seed);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "事务谱" });
        var person = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        var initial = (await service.GetGraphSnapshotAsync(genealogy.Id)).UnlinkGeneration;
        var pause = new PausePeopleRead();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False;Default Timeout=10").AddInterceptors(pause).Options;
        await using var reader = new ZupuDbContext(options);
        var snapshotTask = new GenealogyDataService(reader).GetGraphSnapshotAsync(genealogy.Id);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var writer = Task.Run(async () =>
        {
            await using var external = database.CreateContext();
            var selected = await external.People.SingleAsync(item => item.Id == person.Id);
            selected.Name = "乙";
            await external.SaveChangesAsync();
        });
        try { await Task.WhenAny(writer, Task.Delay(250)); }
        finally { pause.Release.TrySetResult(); }
        var snapshot = await snapshotTask;
        await writer.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(initial, snapshot.UnlinkGeneration);
        Assert.Equal("甲", snapshot.People.Single().Name);
        var after = await service.GetGraphSnapshotAsync(genealogy.Id);
        Assert.Equal(initial + 1, after.UnlinkGeneration);
        Assert.Equal("乙", after.People.Single().Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Migration_reapplies_generation_triggers_after_explicit_downgrade()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(0L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'UnlinkGeneration%'").SingleAsync());
        await migrator.MigrateAsync();
        Assert.Equal(9L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'UnlinkGeneration%'").SingleAsync());
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "重建谱" });
        await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        Assert.Equal(1, (await service.GetGraphSnapshotAsync(genealogy.Id)).UnlinkGeneration);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Rebuilt_people_table_drops_triggers_and_snapshot_fails_closed()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "重建检测谱" });
        await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        await context.Database.OpenConnectionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            var tableSql = await context.Database.SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE type = 'table' AND name = 'People'").SingleAsync();
            var rebuildSql = tableSql.Replace("\"People\"", "\"PeopleRebuilt\"", StringComparison.Ordinal);
            Assert.NotEqual(tableSql, rebuildSql);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync(rebuildSql);
            await context.Database.ExecuteSqlRawAsync("INSERT INTO PeopleRebuilt SELECT * FROM People");
            await context.Database.ExecuteSqlRawAsync("DROP TABLE People");
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE PeopleRebuilt RENAME TO People");
            await transaction.CommitAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
            Assert.Equal(0L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'UnlinkGenerationPeople%'").SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetGraphSnapshotAsync(genealogy.Id));
            Assert.Equal("甲", (await context.People.AsNoTracking().SingleAsync()).Name);
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    private sealed class PausePeopleRead : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _used;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"People\" AS", StringComparison.Ordinal) && Interlocked.Exchange(ref _used, 1) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
