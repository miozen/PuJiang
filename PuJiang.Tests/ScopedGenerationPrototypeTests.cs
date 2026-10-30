using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Diagnostics;
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

    private static async Task InstallAsync(ZupuDbContext context, bool fail = false)
    {
        await using var tx = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync(TableSql);
        await context.Database.ExecuteSqlRawAsync("INSERT INTO ScopedGenerationPrototype (GenealogyId, Value) SELECT Id, 0 FROM Genealogies");
        foreach (var (_, sql) in ExpectedTriggers())
        {
            await context.Database.ExecuteSqlRawAsync(sql);
            if (fail) throw new InvalidOperationException("模拟迁移中断");
        }
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
    private static async Task<bool> FullScopedPreflightAsync(string path)
    {
        await using var referenceDb = new TestDatabase();
        await referenceDb.MigrateAsync();
        await using var reference = referenceDb.CreateContext();
        await InstallAsync(reference);
        await using var candidateDb = new TestDatabase();
        Directory.CreateDirectory(Path.GetDirectoryName(candidateDb.DatabasePath)!);
        await using var package = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var manifest = await OfflineBackupPackageService.ValidateStreamAsync(package, CancellationToken.None);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        await using (var destination = new FileStream(candidateDb.DatabasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var source = archive.GetEntry(manifest.DatabaseEntry)!.Open())
            await source.CopyToAsync(destination);
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
}
