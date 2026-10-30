using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Diagnostics;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

// Isolated design only. Existing production generation and migration remain untouched.
[Collection("ProcessWorkerExclusive")]
public class ScopedGenerationPrototypeTests(ITestOutputHelper output)
{
    private const string TableSql = "CREATE TABLE ScopedGenerationPrototype (GenealogyId TEXT PRIMARY KEY, Value INTEGER NOT NULL CHECK (typeof(Value) = 'integer' AND Value >= 0))";
    private static readonly string[] Tables = ["People", "PersonRelationships", "FamilyUnions"];
    private static readonly string[] Operations = ["INSERT", "UPDATE", "DELETE"];
    private const string InsertGenealogySql = "CREATE TRIGGER ScopedProtoGenealogyINSERT AFTER INSERT ON Genealogies BEGIN INSERT INTO ScopedGenerationPrototype (GenealogyId, Value) VALUES (NEW.Id, 0) ON CONFLICT(GenealogyId) DO UPDATE SET Value = Value + 1; END";
    private const string DeleteGenealogySql = "CREATE TRIGGER ScopedProtoGenealogyDELETE AFTER DELETE ON Genealogies BEGIN SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM ScopedGenerationPrototype WHERE GenealogyId = OLD.Id AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807) THEN RAISE(ABORT, 'invalid scoped generation') END; UPDATE ScopedGenerationPrototype SET Value = Value + 1 WHERE GenealogyId = OLD.Id; END";

    private static string TriggerSql(string table, string operation)
    {
        var primary = operation == "INSERT" ? "NEW.GenealogyId" : "OLD.GenealogyId";
        var validate = $"SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM ScopedGenerationPrototype WHERE GenealogyId = {primary} AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807) THEN RAISE(ABORT, 'invalid scoped generation') END;";
        if (operation == "UPDATE")
            validate += " SELECT CASE WHEN NEW.GenealogyId <> OLD.GenealogyId AND NOT EXISTS (SELECT 1 FROM ScopedGenerationPrototype WHERE GenealogyId = NEW.GenealogyId AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807) THEN RAISE(ABORT, 'invalid scoped generation') END;";
        var update = $" UPDATE ScopedGenerationPrototype SET Value = Value + 1 WHERE GenealogyId = {primary};";
        if (operation == "UPDATE")
            update += " UPDATE ScopedGenerationPrototype SET Value = Value + 1 WHERE GenealogyId = NEW.GenealogyId AND NEW.GenealogyId <> OLD.GenealogyId;";
        return $"CREATE TRIGGER ScopedProto{table}{operation} AFTER {operation} ON {table} BEGIN {validate}{update} END";
    }

    private static IEnumerable<(string Name, string Sql)> ExpectedTriggers()
    {
        yield return ("ScopedProtoGenealogyINSERT", InsertGenealogySql);
        yield return ("ScopedProtoGenealogyDELETE", DeleteGenealogySql);
        foreach (var table in Tables)
        foreach (var operation in Operations)
            yield return ($"ScopedProto{table}{operation}", TriggerSql(table, operation));
    }

    private static async Task InstallAsync(ZupuDbContext context, bool fail = false, Func<Task>? beforeCommit = null)
    {
        await using var tx = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync(TableSql);
        await context.Database.ExecuteSqlRawAsync("INSERT INTO ScopedGenerationPrototype (GenealogyId, Value) SELECT Id, 0 FROM Genealogies");
        foreach (var (_, sql) in ExpectedTriggers())
        {
            await context.Database.ExecuteSqlRawAsync(sql);
            if (fail) throw new InvalidOperationException("模拟迁移中断");
        }
        if (beforeCommit is not null) await beforeCommit();
        await tx.CommitAsync();
    }

    // Exact test-owned schema and generation are checked within the same SQL statement.
    private static string GuardSql()
    {
        static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var sql = "SELECT Value AS Value FROM ScopedGenerationPrototype WHERE GenealogyId = @genealogy AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807";
        sql += $" AND EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'ScopedGenerationPrototype' AND sql = {Literal(TableSql)})";
        foreach (var (name, definition) in ExpectedTriggers())
            sql += $" AND EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'trigger' AND name = {Literal(name)} AND sql = {Literal(definition)})";
        return sql;
    }

    private static Task<long> ReadAsync(ZupuDbContext context, Guid genealogyId) =>
        context.Database.SqlQueryRaw<long>(GuardSql(), new SqliteParameter("@genealogy", genealogyId.ToString().ToUpperInvariant())).SingleAsync();

    private static Task<int> DeleteAsync(ZupuDbContext context, Guid genealogyId, Guid relationshipId, long generation) =>
        context.Database.ExecuteSqlRawAsync($"DELETE FROM PersonRelationships WHERE Id = @record AND GenealogyId = @genealogy AND EXISTS (SELECT 1 FROM ({GuardSql()}) AS expected WHERE expected.Value = @generation)",
            new SqliteParameter("@record", relationshipId.ToString().ToUpperInvariant()),
            new SqliteParameter("@genealogy", genealogyId.ToString().ToUpperInvariant()),
            new SqliteParameter("@generation", generation));

    private static Task<int> DeleteUnionAsync(ZupuDbContext context, Guid genealogyId, Guid unionId, long generation) =>
        context.Database.ExecuteSqlRawAsync($"DELETE FROM FamilyUnions WHERE Id = @record AND GenealogyId = @genealogy AND EXISTS (SELECT 1 FROM ({GuardSql()}) AS expected WHERE expected.Value = @generation)",
            new SqliteParameter("@record", unionId.ToString().ToUpperInvariant()),
            new SqliteParameter("@genealogy", genealogyId.ToString().ToUpperInvariant()),
            new SqliteParameter("@generation", generation));

    private static async Task<(Genealogy A, Genealogy B, Person A1, Person A2, Person Moving, PersonRelationship Link)> SeedAsync(ZupuDbContext context)
    {
        var service = new GenealogyDataService(context);
        var a = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var b = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var a1 = await service.AddPersonAsync(new Person { GenealogyId = a.Id, Name = "甲" });
        var a2 = await service.AddPersonAsync(new Person { GenealogyId = a.Id, Name = "乙" });
        var moving = await service.AddPersonAsync(new Person { GenealogyId = a.Id, Name = "可移动人物" });
        var link = await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = a.Id, PersonId = a1.Id,
            RelatedPersonId = a2.Id, Kind = PersonRelationshipKind.Spouse });
        return (a, b, a1, a2, moving, link);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "Integration")]
    public async Task Independent_process_aba_invalidates_only_its_genealogy(bool sameGenealogy)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        var other = await new GenealogyDataService(context).AddPersonAsync(new Person { GenealogyId = data.B.Id, Name = "外谱" });
        await InstallAsync(context);
        var oldA = await ReadAsync(context, data.A.Id);
        var oldB = await ReadAsync(context, data.B.Id);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(RestoreWorkerProgram).Assembly.Location);
        start.ArgumentList.Add("--aba-rename");
        start.ArgumentList.Add(Path.GetDirectoryName(db.DatabasePath)!);
        start.ArgumentList.Add((sameGenealogy ? data.A2.Id : other.Id).ToString());
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动隔离工作进程。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        Assert.True(process.ExitCode == 0, $"隔离进程失败：{await stderr} {await stdout}");
        Assert.Equal(oldA + (sameGenealogy ? 2 : 0), await ReadAsync(context, data.A.Id));
        Assert.Equal(oldB + (sameGenealogy ? 0 : 2), await ReadAsync(context, data.B.Id));
        Assert.Equal(sameGenealogy ? 0 : 1, await DeleteAsync(context, data.A.Id, data.Link.Id, oldA));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Genealogy_cascade_updates_tombstone_and_preserves_other_scope()
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        await new GenealogyDataService(context).AddPersonAsync(new Person { GenealogyId = data.B.Id, Name = "级联人物" });
        await InstallAsync(context);
        var beforeA = await ReadAsync(context, data.A.Id);
        var beforeB = await ReadAsync(context, data.B.Id);
        await context.Genealogies.Where(item => item.Id == data.B.Id).ExecuteDeleteAsync();
        Assert.Equal(beforeB + 2, await ReadAsync(context, data.B.Id));
        Assert.Equal(beforeA, await ReadAsync(context, data.A.Id));
        Assert.Empty(await context.People.AsNoTracking().Where(item => item.GenealogyId == data.B.Id).ToListAsync());
        Assert.Equal(1, await DeleteAsync(context, data.A.Id, data.Link.Id, beforeA));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Complex_genealogy_delete_restrictions_and_ordered_transaction_preserve_scope(bool commit)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        var family = await new GenealogyDataService(context).AddFamilyUnionAsync(new FamilyUnion { GenealogyId = data.A.Id,
            PartnerOneId = data.A1.Id, PartnerTwoId = data.A2.Id, Kind = FamilyUnionKind.Marriage });
        var parent = new FamilyBranch { GenealogyId = data.A.Id, Name = "主房" };
        var child = new FamilyBranch { GenealogyId = data.A.Id, ParentBranchId = parent.Id, Name = "支房" };
        context.Add(parent);
        await context.SaveChangesAsync();
        context.Add(child);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET BranchId = {child.Id} WHERE Id = {data.A1.Id}");
        await InstallAsync(context);
        var oldA = await ReadAsync(context, data.A.Id);
        var oldB = await ReadAsync(context, data.B.Id);
        var global = await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
        // RESTRICT dependencies prevent both individual and whole-genealogy implicit deletion.
        await Assert.ThrowsAsync<SqliteException>(() => context.People.Where(p => p.Id == data.A1.Id).ExecuteDeleteAsync());
        await Assert.ThrowsAsync<SqliteException>(() => context.Genealogies.Where(g => g.Id == data.A.Id).ExecuteDeleteAsync());
        Assert.Equal(oldA, await ReadAsync(context, data.A.Id));
        Assert.Equal(global, await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync());
        await using (var tx = await context.Database.BeginTransactionAsync())
        {
            await context.PersonRelationships.Where(r => r.GenealogyId == data.A.Id).ExecuteDeleteAsync();
            await context.FamilyUnions.Where(u => u.GenealogyId == data.A.Id).ExecuteDeleteAsync();
            await context.People.Where(p => p.GenealogyId == data.A.Id).ExecuteDeleteAsync();
            await context.FamilyBranches.Where(b => b.Id == child.Id).ExecuteDeleteAsync();
            await context.Genealogies.Where(g => g.Id == data.A.Id).ExecuteDeleteAsync();
            if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        }
        Assert.Equal(oldA + (commit ? 6 : 0), await ReadAsync(context, data.A.Id));
        Assert.Equal(oldB, await ReadAsync(context, data.B.Id));
        Assert.Equal(global + (commit ? 5 : 0), await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync());
        Assert.Equal(commit ? 0 : 3, await context.People.CountAsync(p => p.GenealogyId == data.A.Id));
        Assert.Equal(commit ? 0 : 1, await context.PersonRelationships.CountAsync(r => r.Id == data.Link.Id));
        Assert.Equal(commit ? 0 : 1, await context.FamilyUnions.CountAsync(u => u.Id == family.Id));
        Assert.Equal(commit ? 0 : 2, await context.FamilyBranches.CountAsync(b => b.GenealogyId == data.A.Id));
        Assert.Equal(commit ? 0 : 1, await DeleteAsync(context, data.A.Id, data.Link.Id, oldA));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Compare_generation_guard_read_cost()
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        await InstallAsync(context);
        await context.Database.OpenConnectionAsync();
        var samples = new Dictionary<bool, List<double>> { [false] = [], [true] = [] };
        for (var iteration = -1; iteration < 3; iteration++)
        foreach (var scoped in iteration % 2 == 0 ? new[] { false, true } : new[] { true, false })
        {
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < 100; i++)
            {
                var value = scoped ? await ReadAsync(context, data.A.Id) :
                    await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync();
                Assert.True(value >= 0);
            }
            stopwatch.Stop();
            if (iteration >= 0) samples[scoped].Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        foreach (var scoped in new[] { false, true })
            output.WriteLine($"{(scoped ? "scoped" : "global")} guard, 100 reads: {string.Join(", ", samples[scoped].Select(x => x.ToString("F1")))} ms; median {samples[scoped].Order().ElementAt(1):F1} ms");
    }

    [Theory]
    [InlineData("success")]
    [InlineData("provenance")]
    [InlineData("union")]
    [InlineData("generation-overflow")]
    [InlineData("source-changed")]
    [Trait("Category", "Integration")]
    public async Task Bulk_import_commits_or_rolls_back_scoped_generations_and_provenance(string scenario)
    {
        const int count = 50;
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var seed = db.CreateContext();
        var original = await SeedAsync(seed);
        await InstallAsync(seed);
        if (scenario == "generation-overflow")
            await seed.Database.ExecuteSqlRawAsync("CREATE TRIGGER TestImportOverflow BEFORE INSERT ON People WHEN NEW.Name LIKE '虚构%' BEGIN UPDATE ScopedGenerationPrototype SET Value = 9223372036854775807 WHERE GenealogyId = NEW.GenealogyId; END");
        var oldA = await ReadAsync(seed, original.A.Id);
        var oldB = await ReadAsync(seed, original.B.Id);
        var oldGlobal = await seed.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
        var path = Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, "fictional.ged");
        var gedcom = new StringBuilder("0 HEAD\n1 GEDC\n2 VERS 5.5.1\n");
        for (var i = 1; i <= count; i++) gedcom.Append($"0 @I{i}@ INDI\n1 NAME 虚构{i} /氏/\n");
        gedcom.Append("0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I2@\n0 TRLR\n");
        await File.WriteAllTextAsync(path, gedcom.ToString());
        var interceptor = new FailImportInsert(scenario);
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={db.DatabasePath};Pooling=False").AddInterceptors(interceptor).Options;
        ExternalGenealogyExchangeImportResult? result = null;
        await using (var importing = new ZupuDbContext(options))
        {
            var importer = new ExternalGenealogyExchangeImportService(importing);
            if (scenario == "source-changed")
                importer.AfterPreflightForTestingAsync = token => File.WriteAllTextAsync(path, "changed", token);
            if (scenario is "provenance" or "union" or "generation-overflow")
            {
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => importer.ImportGedcomAsync(path, "导入谱", true));
                if (scenario == "generation-overflow") Assert.IsType<SqliteException>(error.InnerException);
                else { Assert.IsType<IOException>(error.InnerException); Assert.True(interceptor.Triggered); }
            }
            else result = await importer.ImportGedcomAsync(path, "导入谱", true);
        }
        await using var verify = db.CreateContext();
        var success = scenario == "success";
        if (success)
        {
            Assert.True(result!.Imported);
            var id = Assert.IsType<Guid>(result.GenealogyId);
            Assert.Equal(51, await ReadAsync(verify, id));
            Assert.Equal(count, await verify.People.CountAsync(p => p.GenealogyId == id));
            Assert.Single(await verify.FamilyUnions.Where(u => u.GenealogyId == id).ToListAsync());
            Assert.Single(await verify.GenealogyImportRecords.Where(r => r.GenealogyId == id).ToListAsync());
        }
        if (scenario == "source-changed")
        {
            Assert.False(result!.Imported);
            Assert.Contains(result.Report.Errors, e => e.Code == "SourceChangedAfterPreflight");
        }
        Assert.Equal(success ? 3 : 2, await verify.Genealogies.CountAsync());
        Assert.Equal(success ? 3L : 2L, await verify.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM ScopedGenerationPrototype").SingleAsync());
        Assert.Equal(success ? count + 3 : 3, await verify.People.CountAsync());
        Assert.Equal(success ? 1 : 0, await verify.FamilyUnions.CountAsync());
        Assert.Equal(success ? 1 : 0, await verify.GenealogyImportRecords.CountAsync());
        Assert.Equal(oldGlobal + (success ? 51 : 0), await verify.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync());
        Assert.Equal(oldA, await ReadAsync(verify, original.A.Id));
        Assert.Equal(oldB, await ReadAsync(verify, original.B.Id));
        Assert.Equal(1, await DeleteAsync(verify, original.A.Id, original.Link.Id, oldA));
    }

    private sealed class FailImportInsert(string scenario) : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var table = scenario == "provenance" ? "GenealogyImportRecords" : scenario == "union" ? "FamilyUnions" : null;
            if (table is not null && command.CommandText.Contains($"INSERT INTO \"{table}\"", StringComparison.Ordinal))
            {
                Triggered = true;
                throw new IOException("隔离测试：模拟导入写入失败。");
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Scoped_generation_and_people_share_read_transaction_during_external_write()
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var seed = db.CreateContext();
        var data = await SeedAsync(seed);
        await InstallAsync(seed);
        await using var reader = db.CreateContext();
        await using var tx = await reader.Database.BeginTransactionAsync();
        var generation = await ReadAsync(reader, data.A.Id);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = Task.Run(async () =>
        {
            await using var external = db.CreateContext();
            started.SetResult();
            await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"已修改"} WHERE Id = {data.A2.Id}");
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAny(writer, Task.Delay(250));
            Assert.Equal("乙", (await reader.People.AsNoTracking().SingleAsync(item => item.Id == data.A2.Id)).Name);
            Assert.Equal(generation, await ReadAsync(reader, data.A.Id));
        }
        finally { await tx.CommitAsync(); }
        await writer.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(generation + 1, await ReadAsync(seed, data.A.Id));
        Assert.Equal(0, await DeleteAsync(seed, data.A.Id, data.Link.Id, generation));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Compare_global_and_scoped_generation_graph_insert_cost()
    {
        var samples = new Dictionary<bool, List<double>> { [false] = [], [true] = [] };
        for (var iteration = -1; iteration < 3; iteration++)
        foreach (var scoped in iteration % 2 == 0 ? new[] { false, true } : new[] { true, false })
        {
            await using var db = new TestDatabase();
            await db.MigrateAsync();
            await using var context = db.CreateContext();
            var genealogy = await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "虚构基准谱" });
            var people = Enumerable.Range(0, 1000).Select(i => new Person { GenealogyId = genealogy.Id, Name = $"虚构{i}" }).ToArray();
            context.People.AddRange(people);
            await context.SaveChangesAsync();
            if (scoped)
            {
                await InstallAsync(context);
                await context.Database.ExecuteSqlRawAsync("DROP TRIGGER UnlinkGenerationPersonRelationshipsINSERT");
                await context.Database.ExecuteSqlRawAsync("DROP TRIGGER UnlinkGenerationFamilyUnionsINSERT");
            }
            var relationships = Enumerable.Range(0, 999).Select(i => new PersonRelationship { GenealogyId = genealogy.Id,
                PersonId = people[i].Id, RelatedPersonId = people[i + 1].Id, Kind = PersonRelationshipKind.Spouse }).ToArray();
            var unions = Enumerable.Range(0, 500).Select(i => new FamilyUnion { GenealogyId = genealogy.Id,
                PartnerOneId = people[i * 2].Id, PartnerTwoId = people[i * 2 + 1].Id, Kind = FamilyUnionKind.Marriage }).ToArray();
            var before = scoped ? await ReadAsync(context, genealogy.Id) :
                await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
            context.PersonRelationships.AddRange(relationships);
            context.FamilyUnions.AddRange(unions);
            var stopwatch = Stopwatch.StartNew();
            await context.SaveChangesAsync();
            stopwatch.Stop();
            var after = scoped ? await ReadAsync(context, genealogy.Id) :
                await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
            Assert.Equal(before + 1499, after);
            Assert.Equal(999, await context.PersonRelationships.CountAsync());
            Assert.Equal(500, await context.FamilyUnions.CountAsync());
            if (iteration >= 0) samples[scoped].Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        foreach (var scoped in new[] { false, true })
            output.WriteLine($"{(scoped ? "scoped" : "global")}: {string.Join(", ", samples[scoped].Select(x => x.ToString("F1")))} ms; median {samples[scoped].Order().ElementAt(1):F1} ms");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "Integration")]
    public async Task Cross_genealogy_move_changes_both_generations_only_on_commit(bool commit)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        await InstallAsync(context);
        var beforeA = await ReadAsync(context, data.A.Id);
        var beforeB = await ReadAsync(context, data.B.Id);
        await using (var external = db.CreateContext())
        {
            await using var tx = await external.Database.BeginTransactionAsync();
            // EF treats GenealogyId as a key and disallows tracked changes; emulate a third-party SQL writer.
            await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET GenealogyId = {data.B.Id} WHERE Id = {data.Moving.Id}");
            if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        }
        Assert.Equal(beforeA + (commit ? 1 : 0), await ReadAsync(context, data.A.Id));
        Assert.Equal(beforeB + (commit ? 1 : 0), await ReadAsync(context, data.B.Id));
        Assert.Equal(commit ? 0 : 1, await DeleteAsync(context, data.A.Id, data.Link.Id, beforeA));
        await using var verify = db.CreateContext();
        Assert.Equal(commit ? data.B.Id : data.A.Id, (await verify.People.SingleAsync(person => person.Id == data.Moving.Id)).GenealogyId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Other_genealogy_write_does_not_invalidate_unrelated_candidate()
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        await InstallAsync(context);
        var oldA = await ReadAsync(context, data.A.Id);
        var oldB = await ReadAsync(context, data.B.Id);
        await using (var external = db.CreateContext())
            await new GenealogyDataService(external).AddPersonAsync(new Person { GenealogyId = data.B.Id, Name = "乙谱新人物" });
        Assert.Equal(oldA, await ReadAsync(context, data.A.Id));
        Assert.Equal(oldB + 1, await ReadAsync(context, data.B.Id));
        Assert.Equal(1, await DeleteAsync(context, data.A.Id, data.Link.Id, oldA));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cross_genealogy_move_with_missing_destination_generation_rolls_back_everything()
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        await InstallAsync(context);
        var beforeA = await ReadAsync(context, data.A.Id);
        var beforeGlobal = await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ScopedGenerationPrototype WHERE GenealogyId = {data.B.Id}");
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE People SET GenealogyId = {data.B.Id} WHERE Id = {data.Moving.Id}"));
        Assert.Equal(beforeA, await ReadAsync(context, data.A.Id));
        Assert.Equal(beforeGlobal, await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync());
        Assert.Equal(data.A.Id, (await context.People.AsNoTracking().SingleAsync(person => person.Id == data.Moving.Id)).GenealogyId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task External_move_of_relationship_or_union_advances_old_and_new_genealogies(bool union)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        var service = new GenealogyDataService(context);
        var b1 = await service.AddPersonAsync(new Person { GenealogyId = data.B.Id, Name = "丙" });
        var b2 = await service.AddPersonAsync(new Person { GenealogyId = data.B.Id, Name = "丁" });
        var originalUnion = await service.AddFamilyUnionAsync(new FamilyUnion { GenealogyId = data.A.Id,
            PartnerOneId = data.A1.Id, PartnerTwoId = data.A2.Id, Kind = FamilyUnionKind.Marriage });
        await InstallAsync(context);
        var beforeA = await ReadAsync(context, data.A.Id);
        var beforeB = await ReadAsync(context, data.B.Id);
        await using (var external = db.CreateContext())
        {
            if (union)
                await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE FamilyUnions SET GenealogyId = {data.B.Id}, PartnerOneId = {b1.Id}, PartnerTwoId = {b2.Id} WHERE Id = {originalUnion.Id}");
            else
                await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE PersonRelationships SET GenealogyId = {data.B.Id}, PersonId = {b1.Id}, RelatedPersonId = {b2.Id} WHERE Id = {data.Link.Id}");
        }
        Assert.Equal(beforeA + 1, await ReadAsync(context, data.A.Id));
        Assert.Equal(beforeB + 1, await ReadAsync(context, data.B.Id));
        await using var verify = db.CreateContext();
        if (union) Assert.Equal(data.B.Id, (await verify.FamilyUnions.SingleAsync()).GenealogyId);
        else Assert.Equal(data.B.Id, (await verify.PersonRelationships.SingleAsync()).GenealogyId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Deleted_genealogy_recreated_with_same_ids_keeps_tombstone_and_rejects_old_candidate()
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        await InstallAsync(context);
        var service = new GenealogyDataService(context);
        var b1 = await service.AddPersonAsync(new Person { GenealogyId = data.B.Id, Name = "丙" });
        var b2 = await service.AddPersonAsync(new Person { GenealogyId = data.B.Id, Name = "丁" });
        var link = await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = data.B.Id, PersonId = b1.Id,
            RelatedPersonId = b2.Id, Kind = PersonRelationshipKind.Spouse });
        var old = await ReadAsync(context, data.B.Id);
        await using (var external = db.CreateContext())
        {
            await using var tx = await external.Database.BeginTransactionAsync();
            await external.PersonRelationships.Where(item => item.Id == link.Id).ExecuteDeleteAsync();
            await external.People.Where(item => item.GenealogyId == data.B.Id).ExecuteDeleteAsync();
            await external.Genealogies.Where(item => item.Id == data.B.Id).ExecuteDeleteAsync();
            external.Genealogies.Add(new Genealogy { Id = data.B.Id, Name = "乙谱" });
            await external.SaveChangesAsync();
            external.People.AddRange(new Person { Id = b1.Id, GenealogyId = data.B.Id, Name = "丙" },
                new Person { Id = b2.Id, GenealogyId = data.B.Id, Name = "丁" });
            await external.SaveChangesAsync();
            external.PersonRelationships.Add(new PersonRelationship { Id = link.Id, GenealogyId = data.B.Id,
                PersonId = b1.Id, RelatedPersonId = b2.Id, Kind = PersonRelationshipKind.Spouse });
            await external.SaveChangesAsync();
            await tx.CommitAsync();
        }
        var current = await ReadAsync(context, data.B.Id);
        Assert.True(current > old);
        Assert.Equal(0, await DeleteAsync(context, data.B.Id, link.Id, old));
        Assert.Equal(1, await DeleteAsync(context, data.B.Id, link.Id, current));
        Assert.Single(await context.PersonRelationships.AsNoTracking().Where(item => item.GenealogyId == data.A.Id).ToListAsync());
    }

    [Theory]
    [InlineData("name-aba", false)]
    [InlineData("record-aba", false)]
    [InlineData("delete-reinsert", false)]
    [InlineData("name-aba", true)]
    [InlineData("record-aba", true)]
    [InlineData("delete-reinsert", true)]
    [Trait("Category", "Integration")]
    public async Task Same_genealogy_aba_and_reinsert_reject_old_candidate(string scenario, bool union)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        var family = await new GenealogyDataService(context).AddFamilyUnionAsync(new FamilyUnion { GenealogyId = data.A.Id,
            PartnerOneId = data.A1.Id, PartnerTwoId = data.A2.Id, Kind = FamilyUnionKind.Marriage });
        await InstallAsync(context);
        var captured = await ReadAsync(context, data.A.Id);
        await using (var external = db.CreateContext())
        {
            if (scenario == "name-aba")
            {
                await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"改名"} WHERE Id = {data.A2.Id}");
                await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"乙"} WHERE Id = {data.A2.Id}");
            }
            if (scenario == "record-aba" && !union)
            {
                await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE PersonRelationships SET Note = {"临时"} WHERE Id = {data.Link.Id}");
                await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE PersonRelationships SET Note = NULL WHERE Id = {data.Link.Id}");
            }
            if (scenario == "record-aba" && union)
            {
                await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE FamilyUnions SET Note = {"临时"} WHERE Id = {family.Id}");
                await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE FamilyUnions SET Note = NULL WHERE Id = {family.Id}");
            }
            if (scenario == "delete-reinsert" && !union)
            {
                var original = await external.PersonRelationships.AsNoTracking().SingleAsync(item => item.Id == data.Link.Id);
                await external.PersonRelationships.Where(item => item.Id == data.Link.Id).ExecuteDeleteAsync();
                external.PersonRelationships.Add(original);
                await external.SaveChangesAsync();
            }
            if (scenario == "delete-reinsert" && union)
            {
                var original = await external.FamilyUnions.AsNoTracking().SingleAsync(item => item.Id == family.Id);
                await external.FamilyUnions.Where(item => item.Id == family.Id).ExecuteDeleteAsync();
                external.FamilyUnions.Add(original);
                await external.SaveChangesAsync();
            }
        }
        Assert.Equal(captured + 2, await ReadAsync(context, data.A.Id));
        Assert.Equal(0, union ? await DeleteUnionAsync(context, data.A.Id, family.Id, captured) :
            await DeleteAsync(context, data.A.Id, data.Link.Id, captured));
        Assert.Single(await context.PersonRelationships.AsNoTracking().ToListAsync());
        Assert.Single(await context.FamilyUnions.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("negative")]
    [InlineData("text")]
    [InlineData("overflow")]
    [InlineData("trigger")]
    [InlineData("altered-trigger")]
    [Trait("Category", "Integration")]
    public async Task Missing_damaged_or_untrusted_generation_fails_closed(string fault)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        await InstallAsync(context);
        var captured = await ReadAsync(context, data.A.Id);
        if (fault == "missing") await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ScopedGenerationPrototype WHERE GenealogyId = {data.A.Id}");
        if (fault is "negative" or "text" or "overflow")
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
            if (fault == "negative") await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ScopedGenerationPrototype SET Value = -1 WHERE GenealogyId = {data.A.Id}");
            else if (fault == "text") await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ScopedGenerationPrototype SET Value = {"broken"} WHERE GenealogyId = {data.A.Id}");
            else await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ScopedGenerationPrototype SET Value = 9223372036854775807 WHERE GenealogyId = {data.A.Id}");
        }
        if (fault == "trigger") await context.Database.ExecuteSqlRawAsync("DROP TRIGGER ScopedProtoPeopleUPDATE");
        if (fault == "altered-trigger")
        {
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER ScopedProtoPeopleUPDATE");
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER ScopedProtoPeopleUPDATE AFTER UPDATE ON People BEGIN SELECT 1; END");
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(context, data.A.Id));
        Assert.Equal(0, await DeleteAsync(context, data.A.Id, data.Link.Id, captured));
        if (fault is not ("trigger" or "altered-trigger"))
            await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"拒绝"} WHERE Id = {data.A1.Id}"));
        Assert.Equal("甲", (await context.People.AsNoTracking().SingleAsync(person => person.Id == data.A1.Id)).Name);
        Assert.Single(await context.PersonRelationships.AsNoTracking().ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Test_migration_initializes_old_genealogies_and_failure_rolls_back_schema()
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        Directory.CreateDirectory(db.BackupDirectory);
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        var snapshotPath = Path.Combine(db.BackupDirectory, "before-scoped.db");
        await DatabaseSnapshotService.CreateAsync(db.DatabasePath, snapshotPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync(context, fail: true));
        Assert.Equal(0L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name LIKE 'Scoped%Prototype%' OR name LIKE 'ScopedProto%'").SingleAsync());
        await InstallAsync(context);
        Assert.Equal(0, await ReadAsync(context, data.A.Id));
        Assert.Equal(0, await ReadAsync(context, data.B.Id));
        Assert.Equal(12L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name LIKE 'Scoped%Prototype%' OR name LIKE 'ScopedProto%'").SingleAsync());
        var backupOptions = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={snapshotPath};Mode=ReadOnly;Pooling=False").Options;
        await using var old = new ZupuDbContext(backupOptions);
        Assert.Equal(0L, await old.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name = 'ScopedGenerationPrototype'").SingleAsync());
        Assert.Equal(2, await old.Genealogies.CountAsync());
        Assert.Single(await old.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Old_model_writer_waits_for_scoped_install_and_is_counted_after_commit()
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        Guid genealogyId;
        Guid personId;
        await using (var seed = db.CreateContext())
        {
            var data = await SeedAsync(seed);
            genealogyId = data.A.Id;
            personId = data.A1.Id;
        }

        // This context represents an already-running old application: it has no knowledge of the test-only table.
        await using var oldModelWriter = db.CreateContext();
        await using var installing = db.CreateContext();
        var installed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInstall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var install = InstallAsync(installing, beforeCommit: async () =>
        {
            installed.TrySetResult();
            await releaseInstall.Task;
        });
        await installed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var writer = Task.Run(async () =>
        {
            var person = await oldModelWriter.People.SingleAsync(item => item.Id == personId);
            person.Name = "迁移外写";
            await oldModelWriter.SaveChangesAsync();
        });
        await Task.Delay(200);
        Assert.False(writer.IsCompleted);
        releaseInstall.TrySetResult();
        await install.WaitAsync(TimeSpan.FromSeconds(10));
        await writer.WaitAsync(TimeSpan.FromSeconds(10));

        await using var verify = db.CreateContext();
        Assert.Equal(1, await ReadAsync(verify, genealogyId));
        Assert.Equal("迁移外写", (await verify.People.AsNoTracking().SingleAsync(item => item.Id == personId)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Rebuilt_people_table_drops_scoped_triggers_and_scope_guard_fails_closed()
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var data = await SeedAsync(context);
        await InstallAsync(context);
        var captured = await ReadAsync(context, data.A.Id);
        await context.Database.OpenConnectionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            var tableSql = await context.Database.SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE type = 'table' AND name = 'People'").SingleAsync();
            var rebuiltSql = tableSql.Replace("\"People\"", "\"ScopedPeopleRebuilt\"", StringComparison.Ordinal);
            Assert.NotEqual(tableSql, rebuiltSql);
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await context.Database.ExecuteSqlRawAsync(rebuiltSql);
                await context.Database.ExecuteSqlRawAsync("INSERT INTO ScopedPeopleRebuilt SELECT * FROM People");
                await context.Database.ExecuteSqlRawAsync("DROP TABLE People");
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE ScopedPeopleRebuilt RENAME TO People");
                await transaction.CommitAsync();
            }
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
            Assert.Equal(0L, await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'ScopedProtoPeople%'").SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(context, data.A.Id));
            Assert.Equal(0, await DeleteAsync(context, data.A.Id, data.Link.Id, captured));
            Assert.Equal("甲", (await context.People.AsNoTracking().SingleAsync(item => item.Id == data.A1.Id)).Name);
            Assert.Single(await context.PersonRelationships.AsNoTracking().ToListAsync());
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing", false)]
    [InlineData("negative", false)]
    [InlineData("altered-trigger", false)]
    [Trait("Category", "Integration")]
    public async Task Scoped_backup_check_detects_generation_and_schema_faults_without_touching_active_database(string fault, bool expected)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        Directory.CreateDirectory(db.BackupDirectory);
        var oldPackage = Path.Combine(db.BackupDirectory, "old.zip");
        await using (var seed = db.CreateContext()) await SeedAsync(seed);
        await OfflineBackupPackageService.CreateAsync(db.DatabasePath, oldPackage);
        var productionPreflight = new BackupRestorePreflightService(db.BackupDirectory);
        Assert.True((await productionPreflight.CheckAsync(oldPackage)).IncludesDatabase);
        await using (var context = db.CreateContext())
        {
            await InstallAsync(context);
            if (fault == "missing") await context.Database.ExecuteSqlRawAsync("DELETE FROM ScopedGenerationPrototype WHERE GenealogyId = (SELECT Id FROM Genealogies LIMIT 1)");
            if (fault == "negative")
            {
                await context.Database.OpenConnectionAsync();
                await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
                await context.Database.ExecuteSqlRawAsync("UPDATE ScopedGenerationPrototype SET Value = -1 WHERE GenealogyId = (SELECT Id FROM Genealogies LIMIT 1)");
                await context.Database.CloseConnectionAsync();
            }
            if (fault == "altered-trigger")
            {
                await context.Database.ExecuteSqlRawAsync("DROP TRIGGER ScopedProtoPeopleUPDATE");
                await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER ScopedProtoPeopleUPDATE AFTER UPDATE ON People BEGIN SELECT 1; END");
            }
        }
        var package = Path.Combine(db.BackupDirectory, "scoped.zip");
        await OfflineBackupPackageService.CreateAsync(db.DatabasePath, package);
        var beforeDb = await File.ReadAllBytesAsync(db.DatabasePath);
        var beforePackage = await File.ReadAllBytesAsync(package);
        // Existing production preflight rejects the test-only schema even when the prototype data are valid.
        await Assert.ThrowsAsync<InvalidDataException>(() => productionPreflight.CheckAsync(package));
        Assert.Equal(expected, await CheckScopedPackageAsync(package, db.BackupDirectory));
        Assert.Equal(beforeDb, await File.ReadAllBytesAsync(db.DatabasePath));
        Assert.Equal(beforePackage, await File.ReadAllBytesAsync(package));
        Assert.Empty(Directory.EnumerateFiles(db.BackupDirectory, ".scoped-check-*.db"));
        Assert.Empty(Directory.EnumerateFiles(db.BackupDirectory, ".pujiang-preflight-*.db"));
        Assert.Empty(Directory.EnumerateFiles(db.BackupDirectory, ".pujiang-reference-*.db"));
    }

    // Narrow test-only check: does not replace the production integrity, FK and migration preflight.
    private static async Task<bool> CheckScopedPackageAsync(string package, string scratch)
    {
        var manifest = await OfflineBackupPackageService.ValidateAsync(package);
        var copyPath = Path.Combine(scratch, $".scoped-check-{Guid.NewGuid():N}.db");
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
            if (await copy.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM ScopedGenerationPrototype WHERE typeof(Value) <> 'integer' OR Value < 0 OR Value >= 9223372036854775807").SingleAsync() != 0)
                return false;
            var ids = await copy.Genealogies.AsNoTracking().Select(item => item.Id).ToListAsync();
            try { foreach (var id in ids) await ReadAsync(copy, id); }
            catch (InvalidOperationException) { return false; }
            return true;
        }
        finally { if (ownsCopy) File.Delete(copyPath); }
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("foreign-key", false)]
    [InlineData("migration", false)]
    [InlineData("schema", false)]
    [InlineData("missing-generation", false)]
    [Trait("Category", "Integration")]
    public async Task Full_test_only_preflight_checks_scoped_package_without_replacing_active_database(string fault, bool valid)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using (var context = db.CreateContext())
        {
            var data = await SeedAsync(context);
            await InstallAsync(context);
            if (fault == "foreign-key")
            {
                await context.Database.OpenConnectionAsync();
                await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE PersonRelationships SET RelatedPersonId = {Guid.NewGuid()} WHERE Id = {data.Link.Id}");
                await context.Database.CloseConnectionAsync();
            }
            if (fault == "migration") await context.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory");
            if (fault == "schema") await context.Database.ExecuteSqlRawAsync("DROP TRIGGER ScopedProtoPeopleUPDATE");
            if (fault == "missing-generation") await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ScopedGenerationPrototype WHERE GenealogyId = {data.A.Id}");
        }
        Directory.CreateDirectory(db.BackupDirectory);
        var package = Path.Combine(db.BackupDirectory, "full.zip");
        await OfflineBackupPackageService.CreateAsync(db.DatabasePath, package);
        var activeBefore = await File.ReadAllBytesAsync(db.DatabasePath);
        var packageBefore = await File.ReadAllBytesAsync(package);
        Assert.Equal(valid, await FullScopedPreflightAsync(package));
        Assert.Equal(activeBefore, await File.ReadAllBytesAsync(db.DatabasePath));
        Assert.Equal(packageBefore, await File.ReadAllBytesAsync(package));
    }

    // Isolated test reference and candidate; production preflight intentionally rejects this prototype schema.
    private static async Task<bool> FullScopedPreflightAsync(string path, Action<string, string>? checkpoint = null)
    {
        await using var referenceDb = new TestDatabase();
        await referenceDb.MigrateAsync();
        await using var reference = referenceDb.CreateContext();
        await InstallAsync(reference);
        checkpoint?.Invoke("reference-ready", Path.GetDirectoryName(referenceDb.DatabasePath)!);
        await using var candidateDb = new TestDatabase();
        Directory.CreateDirectory(Path.GetDirectoryName(candidateDb.DatabasePath)!);
        checkpoint?.Invoke("candidate-created", Path.GetDirectoryName(candidateDb.DatabasePath)!);
        await using var package = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var manifest = await OfflineBackupPackageService.ValidateStreamAsync(package, CancellationToken.None);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        await using (var destination = new FileStream(candidateDb.DatabasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var source = archive.GetEntry(manifest.DatabaseEntry)!.Open())
            await source.CopyToAsync(destination);
        checkpoint?.Invoke("copy-ready", Path.GetDirectoryName(candidateDb.DatabasePath)!);
        await using (var header = File.OpenRead(candidateDb.DatabasePath))
        {
            var bytes = new byte[20];
            if (header.Length < bytes.Length) return false;
            await header.ReadExactlyAsync(bytes);
            if (bytes[18] != 1 || bytes[19] != 1 || header.Length != manifest.DatabaseBytes) return false;
        }
        var candidateOptions = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = candidateDb.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()).Options;
        await using var candidate = new ZupuDbContext(candidateOptions);
        await candidate.Database.OpenConnectionAsync();
        checkpoint?.Invoke("candidate-open", Path.GetDirectoryName(candidateDb.DatabasePath)!);
        await reference.Database.OpenConnectionAsync();
        var connection = candidate.Database.GetDbConnection();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check";
            if (!string.Equals(await command.ExecuteScalarAsync() as string, "ok", StringComparison.OrdinalIgnoreCase)) return false;
            command.CommandText = "PRAGMA foreign_key_check";
            await using var violations = await command.ExecuteReaderAsync();
            if (await violations.ReadAsync()) return false;
        }
        var expectedMigrations = reference.Database.GetMigrations().ToArray();
        if (!expectedMigrations.SequenceEqual(await candidate.Database.GetAppliedMigrationsAsync())) return false;
        static async Task<List<string>> SchemaAsync(ZupuDbContext context)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT type, name, sql FROM sqlite_master WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' ORDER BY type, name";
            var items = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) items.Add($"{reader.GetString(0)}\n{reader.GetString(1)}\n{reader.GetString(2)}");
            return items;
        }
        if (!(await SchemaAsync(reference)).SequenceEqual(await SchemaAsync(candidate), StringComparer.Ordinal)) return false;
        if (!await candidate.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).AnyAsync()) return false;
        if (await candidate.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM ScopedGenerationPrototype WHERE typeof(Value) <> 'integer' OR Value < 0 OR Value >= 9223372036854775807").SingleAsync() != 0) return false;
        foreach (var id in await candidate.Genealogies.AsNoTracking().Select(item => item.Id).ToListAsync())
            try { await ReadAsync(candidate, id); } catch (InvalidOperationException) { return false; }
        return true;
    }

    [Theory]
    [InlineData("reference-ready")]
    [InlineData("candidate-created")]
    [InlineData("copy-ready")]
    [InlineData("candidate-open")]
    [Trait("Category", "Integration")]
    public async Task Scoped_preflight_exception_cleans_owned_directories_and_preserves_input(string failurePoint)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using (var seed = db.CreateContext())
        {
            await SeedAsync(seed);
            await InstallAsync(seed);
        }
        Directory.CreateDirectory(db.BackupDirectory);
        var path = Path.Combine(db.BackupDirectory, "scoped.zip");
        await OfflineBackupPackageService.CreateAsync(db.DatabasePath, path);
        var originalDb = await File.ReadAllBytesAsync(db.DatabasePath);
        var originalPackage = await File.ReadAllBytesAsync(path);
        var ownedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => FullScopedPreflightAsync(path, (stage, directory) =>
        {
            ownedDirectories.Add(directory);
            if (stage == failurePoint) throw new InvalidOperationException("injected " + stage);
        }));
        Assert.Equal("injected " + failurePoint, error.Message);
        Assert.NotEmpty(ownedDirectories);
        Assert.All(ownedDirectories, directory => Assert.False(Directory.Exists(directory)));
        Assert.Equal(originalDb, await File.ReadAllBytesAsync(db.DatabasePath));
        Assert.Equal(originalPackage, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(db.BackupDirectory));
    }
}
