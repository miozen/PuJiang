using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

// Test-only experiment against the real V1 schema. It is not used by production candidates.
[Collection("ProcessWorkerExclusive")]
public class ScopedCandidateIsolationTests
{
    private sealed record Token(string Strategy, Guid Session, Guid GenealogyId, long Generation);
    private sealed record Candidate(Token Token, Guid Participant, PersonRelationship? Relationship,
        FamilyUnion? Union, string FirstName, string? SecondName);
    private sealed class DeleteCounter : DbCommandInterceptor
    {
        public int Deletes { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)) Deletes++;
            return ValueTask.FromResult(result);
        }
    }

    private static SqliteParameter GenealogyParameter(Guid id) => new("@genealogy", id.ToString().ToUpperInvariant());

    private static async Task<Candidate> CaptureAsync(ZupuDbContext context, Guid genealogyId, Guid participant,
        Guid session, bool union, Func<Task>? afterGuard = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var generation = await context.Database.SqlQueryRaw<long>(ScopedUnlinkGenerationV1.GuardSql(), GenealogyParameter(genealogyId)).SingleAsync();
        if (afterGuard is not null) await afterGuard();
        var people = await context.People.AsNoTracking().Where(p => p.GenealogyId == genealogyId).ToDictionaryAsync(p => p.Id, p => p.Name);
        var relationship = union ? null : await context.PersonRelationships.AsNoTracking().SingleAsync(r => r.GenealogyId == genealogyId);
        var family = union ? await context.FamilyUnions.AsNoTracking().SingleAsync(u => u.GenealogyId == genealogyId) : null;
        var first = relationship?.PersonId ?? family!.PartnerOneId;
        var second = relationship is not null ? relationship.RelatedPersonId : family!.PartnerTwoId;
        var result = new Candidate(new Token("ScopedV1", session, genealogyId, generation), participant,
            relationship, family, people[first], second is Guid id ? people[id] : null);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task<int> DeleteAsync(ZupuDbContext context, Candidate candidate, string strategy, Guid session, Guid genealogyId)
    {
        if (strategy != "ScopedV1" || candidate.Token.Strategy != strategy || candidate.Token.Session != session ||
            candidate.Token.GenealogyId != genealogyId) return 0;
        var guard = context.Database.SqlQueryRaw<long>(ScopedUnlinkGenerationV1.GuardSql(), GenealogyParameter(genealogyId));
        var generation = candidate.Token.Generation;
        if (candidate.Relationship is { } r && candidate.Union is null)
            return await context.PersonRelationships.Where(x => x.GenealogyId == genealogyId && x.Id == r.Id &&
                (x.PersonId == candidate.Participant || x.RelatedPersonId == candidate.Participant) &&
                x.PersonId == r.PersonId && x.RelatedPersonId == r.RelatedPersonId && x.Kind == r.Kind &&
                x.IsPreferredForLineage == r.IsPreferredForLineage && x.Note == r.Note && x.SourceCitation == r.SourceCitation &&
                context.People.Any(p => p.GenealogyId == genealogyId && p.Id == x.PersonId && p.Name == candidate.FirstName) &&
                (x.RelatedPersonId == null || context.People.Any(p => p.GenealogyId == genealogyId && p.Id == x.RelatedPersonId && p.Name == candidate.SecondName)) &&
                guard.Any(value => value == generation)).ExecuteDeleteAsync();
        if (candidate.Union is { } u && candidate.Relationship is null)
            return await context.FamilyUnions.Where(x => x.GenealogyId == genealogyId && x.Id == u.Id &&
                (x.PartnerOneId == candidate.Participant || x.PartnerTwoId == candidate.Participant) &&
                x.PartnerOneId == u.PartnerOneId && x.PartnerTwoId == u.PartnerTwoId && x.Kind == u.Kind &&
                x.StartDateText == u.StartDateText && x.EndDateText == u.EndDateText && x.Note == u.Note &&
                context.People.Any(p => p.GenealogyId == genealogyId && p.Id == x.PartnerOneId && p.Name == candidate.FirstName) &&
                (x.PartnerTwoId == null || context.People.Any(p => p.GenealogyId == genealogyId && p.Id == x.PartnerTwoId && p.Name == candidate.SecondName)) &&
                guard.Any(value => value == generation)).ExecuteDeleteAsync();
        return 0;
    }

    private static async Task<(Genealogy A, Genealogy B, Person First, Person Second)> SeedAsync(ZupuDbContext context)
    {
        var service = new GenealogyDataService(context);
        var a = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var b = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var first = await service.AddPersonAsync(new Person { GenealogyId = a.Id, Name = "甲" });
        var second = await service.AddPersonAsync(new Person { GenealogyId = a.Id, Name = "乙" });
        await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = a.Id, PersonId = first.Id,
            RelatedPersonId = second.Id, Kind = PersonRelationshipKind.Spouse });
        await service.AddFamilyUnionAsync(new FamilyUnion { GenealogyId = a.Id, PartnerOneId = first.Id,
            PartnerTwoId = second.Id, Kind = FamilyUnionKind.Marriage });
        return (a, b, first, second);
    }

    // A test-only gate; no production switch or recovery capability is introduced.
    private sealed class PolicyGate
    {
        private readonly object sync = new();
        private TaskCompletionSource? drained;
        private int active;
        private bool switching;
        public Guid Session { get; private set; } = Guid.NewGuid();
        public string Strategy { get; private set; } = "ScopedV1";

        public async Task<int> RunAsync(Func<string, Guid, Task<int>> operation)
        {
            string strategy;
            Guid session;
            lock (sync)
            {
                if (switching) return -1;
                active++;
                strategy = Strategy;
                session = Session;
            }
            try { return await operation(strategy, session); }
            finally { lock (sync) { if (--active == 0) drained?.TrySetResult(); } }
        }

        public async Task SwitchAsync(string target, Action started)
        {
            Task wait;
            lock (sync)
            {
                if (switching) throw new InvalidOperationException("switch in progress");
                switching = true;
                drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (active == 0) drained.SetResult();
                wait = drained.Task;
                started();
            }
            await wait;
            lock (sync) { Strategy = target; Session = Guid.NewGuid(); switching = false; drained = null; }
        }
    }

    private sealed class PauseDelete(bool fail = false) : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                if (fail) throw new IOException("模拟在途删除失败");
            }
            return result;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Category", "Integration")]
    public async Task Null_other_person_is_preserved_and_stale_name_fails_closed(bool union, bool stale)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var context = db.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "失考谱" });
        var person = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        if (union) await service.AddFamilyUnionAsync(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = person.Id, Kind = FamilyUnionKind.Marriage });
        else await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = person.Id, Kind = PersonRelationshipKind.LostAncestry });
        var session = Guid.NewGuid();
        var candidate = await CaptureAsync(context, genealogy.Id, person.Id, session, union);
        Assert.Null(candidate.SecondName);
        if (stale) await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"改名"} WHERE Id = {person.Id}");
        Assert.Equal(stale ? 0 : 1, await DeleteAsync(context, candidate, "ScopedV1", session, genealogy.Id));
        Assert.Equal(stale ? 1 : 0, union ? await context.FamilyUnions.CountAsync() : await context.PersonRelationships.CountAsync());
        Assert.Equal(1, await context.People.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Test_only_switch_drains_inflight_delete_and_invalidates_old_session(bool union)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var seed = db.CreateContext();
        var data = await SeedAsync(seed);
        var gate = new PolicyGate();
        var candidate = await CaptureAsync(seed, data.A.Id, data.First.Id, gate.Session, union);
        var pause = new PauseDelete();
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={db.DatabasePath};Pooling=False").AddInterceptors(pause).Options;
        await using var deleting = new ZupuDbContext(options);
        var inflight = gate.RunAsync((strategy, session) => DeleteAsync(deleting, candidate, strategy, session, data.A.Id));
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var switching = gate.SwitchAsync("GlobalV1", started.SetResult);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(switching.IsCompleted);
            Assert.Equal(-1, await gate.RunAsync((_, _) => Task.FromResult(99)));
        }
        finally { pause.Release.TrySetResult(); }
        Assert.Equal(1, await inflight.WaitAsync(TimeSpan.FromSeconds(15)));
        await switching.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("GlobalV1", gate.Strategy);
        Assert.Equal(0, await gate.RunAsync((strategy, session) => DeleteAsync(seed, candidate, strategy, session, data.A.Id)));
        await gate.SwitchAsync("ScopedV1", () => { });
        Assert.NotEqual(candidate.Token.Session, gate.Session);
        Assert.Equal(0, await gate.RunAsync((strategy, session) => DeleteAsync(seed, candidate, strategy, session, data.A.Id)));
        await using var verify = db.CreateContext();
        Assert.Equal(union ? 0 : 1, await verify.FamilyUnions.CountAsync());
        Assert.Equal(union ? 1 : 0, await verify.PersonRelationships.CountAsync());
        Assert.Equal(2, await verify.People.CountAsync());
        var fresh = await CaptureAsync(verify, data.A.Id, data.First.Id, gate.Session, !union);
        Assert.Equal(1, await gate.RunAsync((strategy, session) => DeleteAsync(verify, fresh, strategy, session, data.A.Id)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Test_only_switch_drains_failed_delete_without_changing_records_or_generations(bool union)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var seed = db.CreateContext();
        var data = await SeedAsync(seed);
        var gate = new PolicyGate();
        var candidate = await CaptureAsync(seed, data.A.Id, data.First.Id, gate.Session, union);
        var global = await seed.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync();
        var pause = new PauseDelete(fail: true);
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={db.DatabasePath};Pooling=False").AddInterceptors(pause).Options;
        await using var deleting = new ZupuDbContext(options);
        var inflight = gate.RunAsync((strategy, session) => DeleteAsync(deleting, candidate, strategy, session, data.A.Id));
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var switching = gate.SwitchAsync("GlobalV1", started.SetResult);
        try { await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(switching.IsCompleted); }
        finally { pause.Release.TrySetResult(); }
        await Assert.ThrowsAsync<IOException>(() => inflight.WaitAsync(TimeSpan.FromSeconds(15)));
        await switching.WaitAsync(TimeSpan.FromSeconds(15));
        await gate.SwitchAsync("ScopedV1", () => { });
        Assert.Equal(0, await gate.RunAsync((strategy, session) => DeleteAsync(seed, candidate, strategy, session, data.A.Id)));
        Assert.Equal(candidate.Token.Generation, await seed.Database.SqlQueryRaw<long>(ScopedUnlinkGenerationV1.GuardSql(), GenealogyParameter(data.A.Id)).SingleAsync());
        Assert.Equal(global, await seed.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync());
        Assert.Single(await seed.PersonRelationships.AsNoTracking().ToListAsync());
        Assert.Single(await seed.FamilyUnions.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(false, "other")]
    [InlineData(true, "other")]
    [InlineData(false, "aba")]
    [InlineData(true, "aba")]
    [InlineData(false, "strategy")]
    [InlineData(true, "strategy")]
    [InlineData(false, "token-strategy")]
    [InlineData(true, "token-strategy")]
    [InlineData(false, "session")]
    [InlineData(true, "session")]
    [InlineData(false, "scope")]
    [InlineData(true, "scope")]
    [InlineData(false, "trigger")]
    [InlineData(true, "trigger")]
    [Trait("Category", "Integration")]
    public async Task Test_only_candidate_uses_single_guarded_delete_and_rejects_stale_tokens(bool union, string scenario)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var seed = db.CreateContext();
        var data = await SeedAsync(seed);
        var session = Guid.NewGuid();
        var candidate = await CaptureAsync(seed, data.A.Id, data.First.Id, session, union);
        if (scenario == "token-strategy") candidate = candidate with { Token = candidate.Token with { Strategy = "GlobalV1" } };
        await using (var external = db.CreateContext())
        {
            if (scenario == "other") await new GenealogyDataService(external).AddPersonAsync(new Person { GenealogyId = data.B.Id, Name = "外谱" });
            if (scenario == "aba")
            {
                await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"临时"} WHERE Id = {data.Second.Id}");
                await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"乙"} WHERE Id = {data.Second.Id}");
            }
            if (scenario == "trigger") await external.Database.ExecuteSqlRawAsync("DROP TRIGGER ScopedUnlinkGenerationV1PeopleUPDATE");
        }
        var counter = new DeleteCounter();
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={db.DatabasePath};Pooling=False").AddInterceptors(counter).Options;
        await using var deleting = new ZupuDbContext(options);
        var strategy = scenario == "strategy" ? "GlobalV1" : "ScopedV1";
        var selectedSession = scenario == "session" ? Guid.NewGuid() : session;
        var selectedGenealogy = scenario == "scope" ? data.B.Id : data.A.Id;
        Assert.Equal(scenario == "other" ? 1 : 0, await DeleteAsync(deleting, candidate, strategy, selectedSession, selectedGenealogy));
        Assert.Equal(scenario is "strategy" or "token-strategy" or "session" or "scope" ? 0 : 1, counter.Deletes);
        await using var verify = db.CreateContext();
        Assert.Equal(!union && scenario == "other" ? 0 : 1, await verify.PersonRelationships.CountAsync());
        Assert.Equal(union && scenario == "other" ? 0 : 1, await verify.FamilyUnions.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Guard_and_display_share_read_transaction_when_other_writer_changes_name(bool union)
    {
        await using var db = new TestDatabase();
        await db.MigrateAsync();
        await using var seed = db.CreateContext();
        var data = await SeedAsync(seed);
        var session = Guid.NewGuid();
        var initial = await seed.Database.SqlQueryRaw<long>(ScopedUnlinkGenerationV1.GuardSql(), GenealogyParameter(data.A.Id)).SingleAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var reader = db.CreateContext();
        var capture = CaptureAsync(reader, data.A.Id, data.First.Id, session, union, async () => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = Task.Run(async () =>
        {
            await using var external = db.CreateContext();
            started.SetResult();
            await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"新名"} WHERE Id = {data.Second.Id}");
        });
        try { await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Task.WhenAny(writer, Task.Delay(250)); }
        finally { release.TrySetResult(); }
        var candidate = await capture.WaitAsync(TimeSpan.FromSeconds(15));
        await writer.WaitAsync(TimeSpan.FromSeconds(30));
        var changedWasFirst = union && candidate.Union!.PartnerOneId == data.Second.Id;
        Assert.Equal("乙", changedWasFirst ? candidate.FirstName : candidate.SecondName);
        Assert.Equal(initial, candidate.Token.Generation);
        Assert.Equal(initial + 1, await seed.Database.SqlQueryRaw<long>(ScopedUnlinkGenerationV1.GuardSql(), GenealogyParameter(data.A.Id)).SingleAsync());
        Assert.Equal(0, await DeleteAsync(seed, candidate, "ScopedV1", session, data.A.Id));
        Assert.Single(await seed.PersonRelationships.AsNoTracking().ToListAsync());
        Assert.Single(await seed.FamilyUnions.AsNoTracking().ToListAsync());
    }
}
