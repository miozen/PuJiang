using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace PuJiang.Tests;

public class WorkbenchProjectionSessionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Save_requires_new_read_session_and_old_results_remain_invalid()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var a = new Genealogy { Name = "甲谱" }; var b = new Genealogy { Name = "乙谱" };
        var person = new Person { GenealogyId = a.Id, Name = "原名" }; var other = new Person { GenealogyId = b.Id, Name = "另一谱" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.AddRange(a, b); seed.People.AddRange(person, other); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(db);
        await using var projection = new WorkbenchProjectionSession(coordinator, a.Id);
        var revision = await projection.ReopenAsync();
        var before = await projection.ReadPeopleAsync(revision, null, 50);
        Assert.Equal("原名", Assert.Single(before.Page.Items).Name);
        await using (var write = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(write);
            var draft = new WorkbenchPersonDraft(await command.ReadAsync(a.Id, person.Id));
            draft.Values = draft.Values with { Name = "新名" }; await draft.SaveAsync(command);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => projection.ValidateAsync(before.Revision));
        Assert.False(projection.IsCurrent(before.Revision));
        var next = await projection.ReopenAsync(); Assert.NotEqual(revision, next);
        Assert.False(projection.IsCurrent(before.Revision));
        var after = await projection.ReadPeopleAsync(next, null, 50);
        Assert.Equal("新名", Assert.Single(after.Page.Items).Name);
        await projection.ValidateAsync(after.Revision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancelled_reopen_invalidates_old_revision_and_disposal_releases_lease()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var genealogy = new Genealogy { Name = "谱" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(genealogy); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(db); var projection = new WorkbenchProjectionSession(coordinator, genealogy.Id);
        var revision = await projection.ReopenAsync();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => projection.ReopenAsync(cancelled.Token));
        Assert.False(projection.IsCurrent(revision));
        await Assert.ThrowsAsync<InvalidOperationException>(() => projection.ReadPeopleAsync(revision, null, 10));
        await Task.WhenAll(projection.DisposeAsync().AsTask(), projection.DisposeAsync().AsTask());
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static TargetContextCoordinator Coordinator(TestDatabase db) => new(new DbContextOptionsBuilder<ZupuDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()).Options);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Reopen_invalidates_inflight_read_before_it_can_be_returned()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var genealogy = new Genealogy { Name = "虚构谱" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(genealogy); seed.People.Add(new Person { GenealogyId = genealogy.Id, Name = "虚构人物" }); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(db); await using var projection = new WorkbenchProjectionSession(coordinator, genealogy.Id);
        var revision = await projection.ReopenAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        projection.BeforeReadForTest = async () => { entered.TrySetResult(); await release.Task; };
        var read = projection.ReadPeopleAsync(revision, null, 10); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var reopen = projection.ReopenAsync(); Assert.False(projection.IsCurrent(revision));
        release.TrySetResult(); await Assert.ThrowsAsync<InvalidOperationException>(() => read);
        var next = await reopen; projection.BeforeReadForTest = null;
        Assert.Single((await projection.ReadPeopleAsync(next, null, 10)).Page.Items);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Reopen_rejects_an_inflight_single_person_result()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var genealogy = new Genealogy { Name = "虚构谱" }; var person = new Person { GenealogyId = genealogy.Id, Name = "虚构人物" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(genealogy); seed.People.Add(person); await seed.SaveChangesAsync(); }
        await using var projection = new WorkbenchProjectionSession(Coordinator(db), genealogy.Id);
        var revision = await projection.ReopenAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        projection.BeforeReadForTest = async () => { entered.TrySetResult(); await release.Task; };
        var read = projection.ReadPersonAsync(revision, person.Id); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var reopen = projection.ReopenAsync(); release.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => read);
        var next = await reopen; projection.BeforeReadForTest = null;
        Assert.Equal(person.Id, (await projection.ReadPersonAsync(next, person.Id)).Person.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Relationship_pages_are_scoped_and_rejected_after_reopen()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "甲" }; var other = new Genealogy { Name = "乙" };
        var parent = new Person { GenealogyId = g.Id, Name = "父" }; var child = new Person { GenealogyId = g.Id, Name = "子" };
        var outside = new Person { GenealogyId = other.Id, Name = "另谱" };
        var edge = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.AdoptiveParent };
        await using (var seed = db.CreateContext()) { seed.Genealogies.AddRange(g, other); seed.People.AddRange(parent, child, outside); seed.PersonRelationships.Add(edge); await seed.SaveChangesAsync(); }
        await using var projection = new WorkbenchProjectionSession(Coordinator(db), g.Id);
        var revision = await projection.ReopenAsync();
        var children = await projection.ReadChildrenAsync(revision, parent.Id, null, 20);
        Assert.Equal(edge.Id, Assert.Single(children.Page.Items).RelationshipId);
        var parents = await projection.ReadParentsAsync(revision, child.Id, null, 20);
        Assert.Equal(parent.Id, Assert.Single(parents.Page.Items).ParentId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => projection.ReadChildrenAsync(revision, outside.Id, null, 20));
        var next = await projection.ReopenAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => projection.ReadParentsAsync(revision, child.Id, null, 20));
        Assert.Equal(edge.Id, Assert.Single((await projection.ReadParentsAsync(next, child.Id, null, 20)).Page.Items).RelationshipId);
    }
}
