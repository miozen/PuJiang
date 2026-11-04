using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class ProjectionReadSessionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Chart_choice_preserves_historical_identity_when_subject_was_deleted()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid genealogyId, personId, chartId;
        await using (var context = database.CreateContext())
        {
            var g = new Genealogy { Name = "谱" };
            var parent = new Person { GenealogyId = g.Id, Name = "父" };
            var child = new Person { GenealogyId = g.Id, Name = "已删人物" };
            var relation = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent };
            context.Genealogies.Add(g); context.People.AddRange(parent, child); context.PersonRelationships.Add(relation);
            await context.SaveChangesAsync();
            var snapshot = await new GenealogyDataService(context).GetGraphSnapshotAsync(g.Id);
            var chart = await new LineageChartService(context).SaveAsync(g.Id, null, "图", null, snapshot,
                new Dictionary<Guid, Guid> { [child.Id] = relation.Id });
            (genealogyId, personId, chartId) = (g.Id, child.Id, chart.Id);
            await context.PersonRelationships.Where(r => r.Id == relation.Id).ExecuteDeleteAsync();
            await context.People.Where(p => p.Id == child.Id).ExecuteDeleteAsync();
        }
        await using var session = await ProjectionReadSession.OpenAsync(Coordinator(database), genealogyId);
        var result = await session.ReadChartChoiceAsync(session.Token, chartId, personId);
        Assert.NotNull(result); Assert.Equal("已删人物", result.PersonName);
        Assert.True(result.NeedsReview); Assert.Null(result.ApplicableRelationshipId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Parent_union_and_chart_choice_are_scoped_paged_and_require_review_after_change()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid geneId, parentId, childId, relationId, chartId, foreignPersonId;
        await using (var seed = database.CreateContext())
        {
            var g = new Genealogy { Name = "甲" }; var other = new Genealogy { Name = "乙" };
            var parent = new Person { GenealogyId = g.Id, Name = "父" };
            var adoptive = new Person { GenealogyId = g.Id, Name = "养母" };
            var child = new Person { GenealogyId = g.Id, Name = "子" };
            var foreign = new Person { GenealogyId = other.Id, Name = "外谱" };
            var relation = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = parent.Id,
                Kind = PersonRelationshipKind.BiologicalParent, SourceCitation = "谱页" };
            seed.Genealogies.AddRange(g, other); seed.People.AddRange(parent, adoptive, child, foreign);
            seed.PersonRelationships.Add(relation);
            seed.PersonRelationships.Add(new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = adoptive.Id, Kind = PersonRelationshipKind.AdoptiveParent });
            seed.PersonRelationships.Add(new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, Kind = PersonRelationshipKind.LostAncestry, SourceCitation = "失考谱页" });
            seed.PersonRelationships.Add(new PersonRelationship { GenealogyId = g.Id, PersonId = parent.Id, RelatedPersonId = child.Id, Kind = PersonRelationshipKind.Spouse });
            seed.FamilyUnions.Add(new FamilyUnion { GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = child.Id });
            seed.FamilyUnions.Add(new FamilyUnion { GenealogyId = g.Id, PartnerOneId = child.Id, PartnerTwoId = null });
            await seed.SaveChangesAsync();
            var snapshot = await new GenealogyDataService(seed).GetGraphSnapshotAsync(g.Id);
            var chart = await new LineageChartService(seed).SaveAsync(g.Id, null, "图", null, snapshot,
                new Dictionary<Guid, Guid> { [child.Id] = relation.Id });
            (geneId, parentId, childId, relationId, chartId, foreignPersonId) = (g.Id, parent.Id, child.Id, relation.Id, chart.Id, foreign.Id);
        }
        var coordinator = Coordinator(database);
        await using (var session = await ProjectionReadSession.OpenAsync(coordinator, geneId))
        {
            var token = session.Token;
            Assert.Equal("父", (await session.ReadPersonAsync(token, parentId)).Name);
            Assert.Equal(childId, Assert.Single((await session.SearchPeopleAsync(token, childId.ToString(), null, 10)).Items).Id);
            Assert.Empty((await session.SearchPeopleAsync(token, "外谱", null, 10)).Items);
            var parents = await session.ReadParentCandidatesAsync(token, childId, null, 1);
            Assert.Single(parents.Items); Assert.NotNull(parents.NextCursor);
            var parentsNext = await session.ReadParentCandidatesAsync(token, childId, parents.NextCursor, 1);
            Assert.Single(parentsNext.Items); Assert.Null(parentsNext.NextCursor);
            Assert.Contains(parents.Items.Concat(parentsNext.Items), candidate => candidate.RelationshipId == relationId && candidate.Source == "谱页");
            Assert.Equal(childId, Assert.Single((await session.ReadChildCandidatesAsync(token, parentId, null, 10)).Items).ChildId);
            var firstUnion = await session.ReadUnionsAsync(token, childId, null, 1);
            Assert.Single(firstUnion.Items); Assert.NotNull(firstUnion.NextCursor);
            var secondUnion = await session.ReadUnionsAsync(token, childId, firstUnion.NextCursor, 1);
            Assert.Single(secondUnion.Items); Assert.Null(secondUnion.NextCursor);
            var otherRelations = await session.ReadOtherRelationshipsAsync(token, childId, null, 10);
            Assert.Contains(otherRelations.Items, row => row.Kind == PersonRelationshipKind.LostAncestry && row.Source == "失考谱页");
            Assert.Contains(otherRelations.Items, row => row.Kind == PersonRelationshipKind.Spouse && row.RelatedPersonId == childId);
            var choice = await session.ReadChartChoiceAsync(token, chartId, childId);
            Assert.NotNull(choice); Assert.False(choice.NeedsReview); Assert.Equal(relationId, choice.ApplicableRelationshipId);
            Assert.Equal(chartId, Assert.Single((await session.ReadChartsAsync(token, null, 10)).Items).Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadParentCandidatesAsync(token, foreignPersonId, null, 1));
        }
        await using (var writer = database.CreateContext())
            await writer.People.Where(p => p.Id == childId).ExecuteUpdateAsync(p => p.SetProperty(x => x.Name, "新名"));
        await using var refreshed = await ProjectionReadSession.OpenAsync(coordinator, geneId);
        var stale = await refreshed.ReadChartChoiceAsync(refreshed.Token, chartId, childId);
        Assert.NotNull(stale); Assert.True(stale.NeedsReview); Assert.Null(stale.ApplicableRelationshipId);
        await refreshed.DisposeAsync();
        await using (var writer = database.CreateContext())
            await writer.PersonRelationships.Where(r => r.Id == relationId).ExecuteDeleteAsync();
        await using var removed = await ProjectionReadSession.OpenAsync(coordinator, geneId);
        var invalid = await removed.ReadChartChoiceAsync(removed.Token, chartId, childId);
        Assert.NotNull(invalid); Assert.True(invalid.NeedsReview); Assert.Null(invalid.ApplicableRelationshipId);
        Assert.Equal("人物或关系已变化", invalid.Problem);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Drain_signals_all_sessions_even_when_one_read_is_still_busy()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid id;
        await using (var seed = database.CreateContext()) { var g = new Genealogy { Name = "谱" }; id = g.Id; seed.Genealogies.Add(g); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(database);
        var first = await ProjectionReadSession.OpenAsync(coordinator, id);
        var second = await ProjectionReadSession.OpenAsync(coordinator, id);
        var token = first.Token; var secondToken = second.Token;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.AfterVersionChecked = async _ => { entered.SetResult(); await release.Task; };
        var read = first.ReadPeopleAsync(token, null, 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var drain = coordinator.DrainAsync();
        try
        {
            Assert.False(drain.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => second.ReadPeopleAsync(secondToken, null, 1));
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await drain.WaitAsync(TimeSpan.FromSeconds(10));
        await first.DisposeAsync(); await second.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Failed_open_releases_file_and_coordinator_lease()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        var coordinator = Coordinator(database);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProjectionReadSession.OpenAsync(coordinator, Guid.NewGuid()));
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        File.Move(database.DatabasePath, database.DatabasePath + ".moved");
        File.Move(database.DatabasePath + ".moved", database.DatabasePath);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Pages_are_scoped_and_change_permanently_invalidates_session()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid genealogyId;
        await using (var seed = database.CreateContext())
        {
            var a = new Genealogy { Name = "甲" }; var b = new Genealogy { Name = "乙" };
            genealogyId = a.Id; seed.Genealogies.AddRange(a, b);
            seed.People.AddRange(new Person { GenealogyId = a.Id, Name = "甲一", RecordedGeneration = "三世" },
                new Person { GenealogyId = a.Id, Name = "甲二" }, new Person { GenealogyId = b.Id, Name = "外谱" });
            await seed.SaveChangesAsync();
        }
        var coordinator = Coordinator(database);
        await using var session = await ProjectionReadSession.OpenAsync(coordinator, genealogyId);
        var token = session.Token;
        var first = await session.ReadPeopleAsync(token, null, 1);
        Assert.Single(first.Items); Assert.NotNull(first.NextCursor);
        var second = await session.ReadPeopleAsync(token, first.NextCursor, 1);
        Assert.Single(second.Items); Assert.Null(second.NextCursor);
        Assert.DoesNotContain(new[] { first.Items[0], second.Items[0] }, row => row.Name == "外谱");
        await using (var writer = database.CreateContext())
        {
            writer.FamilyBranches.Add(new FamilyBranch { GenealogyId = genealogyId, Name = "支" });
            await writer.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ValidateAsync(token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadPeopleAsync(token, first.NextCursor, 1));
        Assert.Throws<InvalidOperationException>(() => session.Token);
        await using var replacement = await ProjectionReadSession.OpenAsync(coordinator, genealogyId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => replacement.ReadPeopleAsync(token, null, 1));
        Assert.Single((await replacement.ReadPeopleAsync(replacement.Token, null, 1)).Items);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Active_file_lease_prevents_replacement_by_parent_directory_rename()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid id;
        await using (var seed = database.CreateContext()) { var g = new Genealogy { Name = "谱" }; id = g.Id; seed.Genealogies.Add(g); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(database);
        await using var session = await ProjectionReadSession.OpenAsync(coordinator, id);
        var directory = Path.GetDirectoryName(database.DatabasePath)!;
        var moved = directory + "-moved";
        try { Assert.ThrowsAny<IOException>(() => Directory.Move(directory, moved)); }
        finally { if (Directory.Exists(moved)) Directory.Move(moved, directory); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Drain_waits_for_actual_sql_callback_then_discards_cancelled_result()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid id;
        await using (var seed = database.CreateContext())
        {
            var g = new Genealogy { Name = "谱" }; id = g.Id; seed.Genealogies.Add(g);
            seed.People.Add(new Person { GenealogyId = id, Name = "测试人物" }); await seed.SaveChangesAsync();
        }
        var coordinator = Coordinator(database);
        await using var session = await ProjectionReadSession.OpenAsync(coordinator, id);
        var token = session.Token;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        session.BeforePeopleQueryForTest = connection => connection.CreateFunction<string, long>("projection_test_wait", _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("测试 SQL 等待超时。");
            return 1;
        });
        var read = Task.Run(() => session.ReadPeopleAsync(token, null, 1));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "未进入实际 SQL 回调。");
            var draining = coordinator.DrainAsync();
            Assert.False(draining.IsCompleted);
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            await draining.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadPeopleAsync(token, null, 1));
        }
        finally { release.Set(); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancellation_of_queued_read_invalidates_inflight_result()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid id;
        await using (var seed = database.CreateContext()) { var g = new Genealogy { Name = "谱" }; id = g.Id; seed.Genealogies.Add(g); await seed.SaveChangesAsync(); }
        await using var session = await ProjectionReadSession.OpenAsync(Coordinator(database), id);
        var token = session.Token;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AfterVersionChecked = async _ => { entered.SetResult(); await release.Task; };
        var first = session.ReadPeopleAsync(token, null, 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ReadPeopleAsync(token, null, 1, cancellation.Token));
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        Assert.Throws<InvalidOperationException>(() => session.Token);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Concurrent_dispose_and_drain_wait_for_the_same_inflight_read()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid id;
        await using (var seed = database.CreateContext()) { var g = new Genealogy { Name = "谱" }; id = g.Id; seed.Genealogies.Add(g); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(database);
        var session = await ProjectionReadSession.OpenAsync(coordinator, id);
        var token = session.Token;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AfterVersionChecked = async _ => { entered.SetResult(); await release.Task; };
        var read = session.ReadPeopleAsync(token, null, 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var closing = session.DisposeAsync().AsTask();
        var draining = coordinator.DrainAsync();
        Assert.False(closing.IsCompleted); Assert.False(draining.IsCompleted);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await closing.WaitAsync(TimeSpan.FromSeconds(10));
        await draining.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Drain_cancels_inflight_read_before_releasing_database()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid id;
        await using (var seed = database.CreateContext()) { var g = new Genealogy { Name = "谱" }; id = g.Id; seed.Genealogies.Add(g); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(database);
        await using var session = await ProjectionReadSession.OpenAsync(coordinator, id);
        var token = session.Token;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AfterVersionChecked = async cancellation => { entered.SetResult(); await Task.Delay(Timeout.Infinite, cancellation); };
        var read = session.ReadPeopleAsync(token, null, 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadPeopleAsync(token, null, 1));
        File.Move(database.DatabasePath, database.DatabasePath + ".moved");
        File.Move(database.DatabasePath + ".moved", database.DatabasePath);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Commit_during_read_discards_page_and_invalidates_token()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid id;
        await using (var seed = database.CreateContext())
        {
            await seed.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL");
            var g = new Genealogy { Name = "谱" }; id = g.Id;
            seed.Genealogies.Add(g); seed.People.Add(new Person { GenealogyId = id, Name = "原名" }); await seed.SaveChangesAsync();
        }
        var coordinator = Coordinator(database);
        await using var session = await ProjectionReadSession.OpenAsync(coordinator, id);
        var token = session.Token;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AfterVersionChecked = async cancellation => { entered.SetResult(); await resume.Task.WaitAsync(cancellation); };
        var read = session.ReadPeopleAsync(token, null, 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var writer = database.CreateContext()) await writer.People.ExecuteUpdateAsync(p => p.SetProperty(x => x.Name, "新名"));
        resume.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => read);
        Assert.Throws<InvalidOperationException>(() => session.Token);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Drain_closes_session_releases_file_and_rejects_old_token()
    {
        await using var database = new TestDatabase(); await database.MigrateAsync();
        Guid id;
        await using (var seed = database.CreateContext()) { var g = new Genealogy { Name = "谱" }; id = g.Id; seed.Genealogies.Add(g); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(database);
        var session = await ProjectionReadSession.OpenAsync(coordinator, id);
        var token = session.Token;
        Assert.ThrowsAny<IOException>(() => File.Move(database.DatabasePath, database.DatabasePath + ".moved"));
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadPeopleAsync(token, null, 1));
        await session.DisposeAsync();
        File.Move(database.DatabasePath, database.DatabasePath + ".moved");
        File.Move(database.DatabasePath + ".moved", database.DatabasePath);
    }

    private static TargetContextCoordinator Coordinator(TestDatabase database) => new(new DbContextOptionsBuilder<ZupuDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = false }.ToString()).Options);
}
