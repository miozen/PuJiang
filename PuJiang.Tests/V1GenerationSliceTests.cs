using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using Xunit;

namespace PuJiang.Tests;

public sealed class V1GenerationSliceTests
{
    [Fact]
    public async Task Creation_saves_explicit_generations_and_rejects_stale_or_nonincreasing_inputs()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db));
        await Assert.ThrowsAsync<ArgumentException>(() => roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父")));
        var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 3));
        var app = new ChildApplication(new IsolatedChildStore(db));
        await Assert.ThrowsAsync<ArgumentException>(() => app.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", 0, 3, 3)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", 0, 3, 2)));
        await Assert.ThrowsAsync<ArgumentException>(() => app.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", 0, 6, 3)));
        var command = new AddChild(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", 0, 4, 3);
        var child = await app.ExecuteAsync(command); Assert.True((await app.ExecuteAsync(command)).Replayed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => roots.UpdateDetailsAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, 4, "", "", 3, "", "")));
        var source = Assert.Single(await app.ReadAsync(g, l)); Assert.Equal(4, source.Generation);
        var sibling = await app.ExecuteSiblingAsync(new(Guid.NewGuid(), g, l, child.PersonId, child.PlacementId, root.PersonId, root.PlacementId, source.FatherRelationshipId, null, false, "冯弟", 4, 3));
        await using var reopened = db.OpenContext(); var people = await reopened.People.ToDictionaryAsync(x => x.Id);
        Assert.Equal(3, people[root.PersonId].ExplicitGeneration); Assert.Equal(4, people[child.PersonId].ExplicitGeneration); Assert.Equal(4, people[sibling.PersonId].ExplicitGeneration);
    }
    private static async Task<(IsolatedTestDatabase Db, Guid G, Guid L, Guid Person, Guid Place, Guid Child, Guid Grandchild)> Setup()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        var p = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid(); var pp = Guid.NewGuid(); var cp = Guid.NewGuid(); var dp = Guid.NewGuid();
        await using var context = db.OpenContext(); await context.Database.EnsureCreatedAsync();
        context.Genealogies.Add(new(g, "虚构", "冯")); context.Lineages.Add(new(l, g, "虚构谱"));
        var parent = new Person(p, l, "冯甲", PersonSex.Male); parent.SetGeneration(3);
        var child = new Person(c, l, "冯乙", PersonSex.Male); child.SetGeneration(4);
        var grandchild = new Person(d, l, "冯丙", PersonSex.Male); grandchild.SetGeneration(6);
        context.People.AddRange(parent, child, grandchild);
        context.Placements.AddRange(new Placement(pp, l, p), new Placement(cp, l, c, pp, 1), new Placement(dp, l, d, cp, 1));
        context.BiologicalParents.AddRange(new BiologicalParent(Guid.NewGuid(), l, c, p, ParentRole.Father), new BiologicalParent(Guid.NewGuid(), l, d, c, ParentRole.Father));
        await context.SaveChangesAsync(); return (db, g, l, p, pp, c, d);
    }
    [Fact]
    public async Task Generation_shift_is_atomic_replayable_and_survives_reopen()
    {
        var (db, g, l, p, place, child, grandchild) = await Setup(); var app = new GenerationApplication(new IsolatedGenerationStore(db));
        var snap = await app.ReadAsync(g, l, p, place); var command = new UpdateGeneration(Guid.NewGuid(), g, l, p, place, 5, snap.Evidence);
        await app.UpdateAsync(command); await app.UpdateAsync(command);
        await using var reopened = db.OpenContext(); var data = await reopened.People.ToDictionaryAsync(x => x.Id);
        Assert.Equal(5, data[p].ExplicitGeneration); Assert.Equal(6, data[child].ExplicitGeneration); Assert.Equal(8, data[grandchild].ExplicitGeneration);
        Assert.Equal(2, await reopened.BiologicalParents.CountAsync()); Assert.Equal(3, await reopened.Placements.CountAsync());
    }
    [Fact]
    public async Task Stale_descendant_and_failed_transaction_leave_original_values()
    {
        var (db, g, l, p, place, child, grandchild) = await Setup(); var app = new GenerationApplication(new IsolatedGenerationStore(db));
        var before = await app.ReadAsync(g, l, p, place);
        await using (var c = db.OpenContext()) { var person = await c.People.SingleAsync(x => x.Id == child); person.SetGeneration(5); await c.SaveChangesAsync(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.UpdateAsync(new(Guid.NewGuid(), g, l, p, place, 5, before.Evidence)));
        var current = await app.ReadAsync(g, l, p, place);
        await using (var c = db.OpenContext()) await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailGeneration BEFORE INSERT ON GenerationReceipts BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAsync<DbUpdateException>(() => app.UpdateAsync(new(Guid.NewGuid(), g, l, p, place, 5, current.Evidence)));
        await using var verify = db.OpenContext(); var data = await verify.People.ToDictionaryAsync(x => x.Id);
        Assert.Equal(3, data[p].ExplicitGeneration); Assert.Equal(5, data[child].ExplicitGeneration); Assert.Equal(6, data[grandchild].ExplicitGeneration);
    }
}
