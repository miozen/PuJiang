using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using Xunit;

namespace PuJiang.Tests;

public sealed class V1SiblingSliceTests
{
    private static async Task<(IsolatedTestDatabase Db, Guid G, Guid L, RootCreated Root, ChildCreated Child, ChildApplication Children, SpouseApplication Spouses)> Setup()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "测试")); await c.SaveChangesAsync(); }
        var root = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var children = new ChildApplication(new IsolatedChildStore(db)); var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", Generation: 2, ExpectedParentGeneration: 1));
        return (db, g, l, root, child, children, spouses);
    }
    private static AddSibling Command(Guid g, Guid l, RootCreated root, ChildNode source) => new(Guid.NewGuid(), g, l, source.PersonId, source.PlacementId, root.PersonId, root.PlacementId, source.FatherRelationshipId, source.MotherId, source.MotherExplicitlyCleared, "冯兄弟", 2, 1);

    [Fact, Trait("Category", "Integration")]
    public async Task Source_mother_is_used_instead_of_first_spouse_and_replay_is_idempotent()
    {
        var (db, g, l, root, child, children, spouses) = await Setup();
        var first = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "甲氏"));
        var second = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "乙氏"));
        await spouses.SetMotherAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, child.PersonId, second.PersonId, first.PersonId, false));
        var source = Assert.Single(await children.ReadAsync(g, l)); var command = Command(g, l, root, source);
        var saved = await children.ExecuteSiblingAsync(command); var replay = await children.ExecuteSiblingAsync(command);
        Assert.False(saved.Replayed); Assert.True(replay.Replayed); Assert.Equal(saved.PersonId, replay.PersonId);
        var read = await new ChildApplication(new IsolatedChildStore(db)).ReadAsync(g, l);
        Assert.Equal(second.PersonId, read.Single(x => x.PersonId == saved.PersonId).MotherId);
        Assert.Equal(second.PersonId, read.Single(x => x.PersonId == child.PersonId).MotherId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => children.ExecuteSiblingAsync(command with { Name = "不同" }));
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Cleared_source_stays_cleared_new_sibling_gets_later_first_spouse()
    {
        var (db, g, l, root, child, children, spouses) = await Setup();
        await spouses.SetMotherAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, child.PersonId, null, null, false));
        var source = Assert.Single(await children.ReadAsync(g, l)); Assert.True(source.MotherExplicitlyCleared);
        var saved = await children.ExecuteSiblingAsync(Command(g, l, root, source));
        var before = await children.ReadAsync(g, l); Assert.Null(before.Single(x => x.PersonId == saved.PersonId).MotherId);
        var mother = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var after = await new ChildApplication(new IsolatedChildStore(db)).ReadAsync(g, l);
        Assert.Null(after.Single(x => x.PersonId == child.PersonId).MotherId); Assert.True(after.Single(x => x.PersonId == child.PersonId).MotherExplicitlyCleared);
        Assert.Equal(mother.PersonId, after.Single(x => x.PersonId == saved.PersonId).MotherId);
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Stale_source_and_storage_failure_do_not_create_partial_records()
    {
        var (db, g, l, root, child, children, spouses) = await Setup();
        var source = Assert.Single(await children.ReadAsync(g, l)); var command = Command(g, l, root, source);
        await spouses.SetMotherAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, child.PersonId, null, null, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => children.ExecuteSiblingAsync(command));
        await Assert.ThrowsAsync<InvalidOperationException>(() => children.ExecuteSiblingAsync(command with { OperationId = Guid.NewGuid(), SourcePlacementId = Guid.NewGuid() }));
        source = Assert.Single(await children.ReadAsync(g, l)); command = Command(g, l, root, source);
        await using (var c = db.OpenContext()) await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailSibling BEFORE INSERT ON SiblingReceipts BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAsync<DbUpdateException>(() => children.ExecuteSiblingAsync(command));
        await using (var c = db.OpenContext()) { Assert.Equal(2, await c.People.CountAsync()); Assert.Equal(2, await c.Placements.CountAsync()); Assert.Equal(1, await c.BiologicalParents.CountAsync()); await c.Database.ExecuteSqlRawAsync("DROP TRIGGER FailSibling"); }
        Assert.False((await children.ExecuteSiblingAsync(command)).Replayed);
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Blank_name_and_foreign_source_or_parent_are_rejected()
    {
        var (db, g, l, root, _, children, _) = await Setup();
        var command = Command(g, l, root, Assert.Single(await children.ReadAsync(g, l)));
        await Assert.ThrowsAsync<ArgumentException>(() => children.ExecuteSiblingAsync(command with { Name = "  " }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => children.ExecuteSiblingAsync(command with { OperationId = Guid.NewGuid(), ParentPlacementId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => children.ExecuteSiblingAsync(command with { OperationId = Guid.NewGuid(), SourcePersonId = root.PersonId, SourcePlacementId = root.PlacementId }));
        await using var c = db.OpenContext(); Assert.Equal(2, await c.People.CountAsync()); Assert.Equal(2, await c.Placements.CountAsync());
    }
}
