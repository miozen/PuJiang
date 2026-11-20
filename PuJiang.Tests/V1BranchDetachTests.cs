using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using Xunit;

namespace PuJiang.Tests;

public sealed class V1BranchDetachTests
{
    [Fact]
    public async Task New_root_parent_is_not_merged_with_existing_person_of_same_name()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid(); var existing = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); c.People.Add(new(existing, l, "虚构同名", PersonSex.Male)); await c.SaveChangesAsync(); }
        var root = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯首", 1));
        var branches = new BranchApplication(new IsolatedBranchStore(db)); var before = await branches.ReadRootParentsAsync(g, l, root.PersonId, root.PlacementId);
        await branches.UpdateRootParentsAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, "虚构同名", null, before.Evidence));
        var after = await branches.ReadRootParentsAsync(g, l, root.PersonId, root.PlacementId); Assert.NotNull(after.FatherId); Assert.NotEqual(existing, after.FatherId);
        await using var verify = db.OpenContext(); Assert.Equal(2, await verify.People.CountAsync(x => x.Name == "虚构同名"));
    }
    [Fact]
    public async Task Empty_canvas_can_get_new_root_without_removing_referenced_parent_archives()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid(); var father = Guid.NewGuid(); var child = Guid.NewGuid();
        await using (var c = db.OpenContext())
        {
            await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构"));
            c.People.AddRange(new Person(father, l, "虚构父", PersonSex.Male), new Person(child, l, "虚构子", PersonSex.Male));
            c.BiologicalParents.Add(new(Guid.NewGuid(), l, child, father, ParentRole.Father)); await c.SaveChangesAsync();
        }
        var root = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯新首", 1));
        await using var verify = db.OpenContext(); Assert.True(await verify.Placements.AnyAsync(x => x.Id == root.PlacementId));
        Assert.True(await verify.BiologicalParents.AnyAsync(x => x.ChildId == child && x.ParentId == father)); Assert.Equal(3, await verify.People.CountAsync());
    }

    [Fact]
    public async Task Delete_preview_rejects_profile_changed_back_after_confirmation()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var root = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var branches = new BranchApplication(new IsolatedBranchStore(db)); var preview = await branches.PreviewDeleteAsync(g, l, root.PersonId, root.PlacementId);
        var profiles = new PersonProfileApplication(new IsolatedPersonProfileStore(db));
        await profiles.UpdateAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, "冯改", PersonSex.Male, "冯父", PersonSex.Male));
        await profiles.UpdateAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, "冯父", PersonSex.Male, "冯改", PersonSex.Male));
        await Assert.ThrowsAsync<InvalidOperationException>(() => branches.DeleteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, preview.Evidence)));
        await using var verify = db.OpenContext(); Assert.True(await verify.Placements.AnyAsync(x => x.Id == root.PlacementId));
    }
    [Fact]
    public async Task Deleting_last_detached_child_cleans_unreferenced_parent_archives()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db)); var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db)); await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "虚构母"));
        var child = await new ChildApplication(new IsolatedChildStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", 1, 2, 1));
        var branches = new BranchApplication(new IsolatedBranchStore(db)); var snap = await new ChildOrderApplication(new IsolatedChildOrderStore(db)).ReadAsync(g, l, root.PlacementId);
        await branches.DetachAsync(new(Guid.NewGuid(), g, l, child.PersonId, child.PlacementId, root.PlacementId, snap.Revision, snap.Evidence));
        var original = await branches.PreviewDeleteAsync(g, l, root.PersonId, root.PlacementId); await branches.DeleteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, original.Evidence));
        var remaining = await branches.PreviewDeleteAsync(g, l, child.PersonId, child.PlacementId);
        Assert.Contains("冯父", remaining.AutoCleanedPeople); Assert.Contains("虚构母", remaining.AutoCleanedPeople);
        await branches.DeleteAsync(new(Guid.NewGuid(), g, l, child.PersonId, child.PlacementId, remaining.Evidence));
        await using (var verify = db.OpenContext()) { Assert.Empty(await verify.People.ToListAsync()); Assert.Empty(await verify.Placements.ToListAsync()); Assert.Empty(await verify.BiologicalParents.ToListAsync()); }
        var next = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯新首", 1)); Assert.NotEqual(Guid.Empty, next.PersonId);
    }
    [Fact]
    public async Task Deleting_child_branch_keeps_siblings_spouses_and_fixes_rank()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var root = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db)); var mother = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "虚构母"));
        var children = new ChildApplication(new IsolatedChildStore(db)); var first = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯甲", 1, 2, 1));
        var second = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯乙", 1, 2, 1));
        var branches = new BranchApplication(new IsolatedBranchStore(db)); var preview = await branches.PreviewDeleteAsync(g, l, first.PersonId, first.PlacementId);
        await branches.DeleteAsync(new(Guid.NewGuid(), g, l, first.PersonId, first.PlacementId, preview.Evidence));
        var remaining = Assert.Single(await children.ReadAsync(g, l)); Assert.Equal(second.PersonId, remaining.PersonId); Assert.Equal(1, remaining.Position); Assert.Equal(mother.PersonId, remaining.MotherId);
        Assert.Equal(mother.PersonId, Assert.Single(await spouses.ReadAsync(g, l, root.PersonId, root.PlacementId)).PersonId);
    }

    [Fact]
    public async Task Deleting_first_spouse_reindexes_remaining_and_new_child_uses_remaining_first()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var root = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db)); var first = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "虚构甲"));
        var second = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "虚构乙"));
        var branches = new BranchApplication(new IsolatedBranchStore(db)); var preview = await branches.PreviewSpouseAsync(g, l, root.PersonId, root.PlacementId, first.PersonId, first.UnionId);
        await branches.DeleteSpouseAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, first.PersonId, first.UnionId, preview.Evidence));
        var snapshot = await spouses.ReadSnapshotAsync(g, l, root.PersonId, root.PlacementId); var remaining = Assert.Single(snapshot.Nodes); Assert.Equal(second.PersonId, remaining.PersonId); Assert.Equal(1, remaining.Position);
        var child = await new ChildApplication(new IsolatedChildStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", snapshot.Revision, 2, 1));
        await using var verify = db.OpenContext(); Assert.True(await verify.BiologicalParents.AnyAsync(x => x.ChildId == child.PersonId && x.ParentId == second.PersonId && x.Role == ParentRole.Mother));
    }
    [Fact]
    public async Task Root_parent_edit_creates_new_people_preserves_other_parent_and_cleans_unreferenced_old_parent()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db)); var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db)); var mother = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "虚构母"));
        var child = await new ChildApplication(new IsolatedChildStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", 1, 2, 1));
        var branches = new BranchApplication(new IsolatedBranchStore(db)); var snap = await new ChildOrderApplication(new IsolatedChildOrderStore(db)).ReadAsync(g, l, root.PlacementId);
        await branches.DetachAsync(new(Guid.NewGuid(), g, l, child.PersonId, child.PlacementId, root.PlacementId, snap.Revision, snap.Evidence));
        var delete = await branches.PreviewDeleteAsync(g, l, root.PersonId, root.PlacementId);
        await branches.DeleteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, delete.Evidence));
        var before = await branches.ReadRootParentsAsync(g, l, child.PersonId, child.PlacementId);
        var command = new UpdateRootParents(Guid.NewGuid(), g, l, child.PersonId, child.PlacementId, "虚构新父", before.MotherName, before.Evidence);
        await branches.UpdateRootParentsAsync(command); await branches.UpdateRootParentsAsync(command);
        var after = await branches.ReadRootParentsAsync(g, l, child.PersonId, child.PlacementId);
        Assert.Equal("虚构新父", after.FatherName); Assert.Equal(mother.PersonId, after.MotherId); Assert.NotEqual(root.PersonId, after.FatherId);
        await using var verify = db.OpenContext(); Assert.False(await verify.People.AnyAsync(x => x.Id == root.PersonId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => branches.UpdateRootParentsAsync(command with { OperationId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Root_parent_edit_rolls_back_new_person_and_relation_on_failure()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var root = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯首", 1));
        var branches = new BranchApplication(new IsolatedBranchStore(db)); var before = await branches.ReadRootParentsAsync(g, l, root.PersonId, root.PlacementId);
        await using (var c = db.OpenContext()) await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailParents BEFORE INSERT ON ChildOrderReceipts BEGIN SELECT RAISE(ABORT, 'injected'); END");
        var command = new UpdateRootParents(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, "虚构父", "虚构母", before.Evidence);
        await Assert.ThrowsAsync<DbUpdateException>(() => branches.UpdateRootParentsAsync(command));
        await using var verify = db.OpenContext(); Assert.Equal(1, await verify.People.CountAsync()); Assert.Empty(await verify.BiologicalParents.ToListAsync()); Assert.False(await branches.HasRootParentsReceiptAsync(command.OperationId));
    }
    [Fact]
    public async Task Deleting_spouse_preserves_children_and_detached_mother_fact()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var root = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var spouseCommand = new AddSpouse(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "虚构母");
        var mother = await spouses.AddAsync(spouseCommand);
        var children = new ChildApplication(new IsolatedChildStore(db));
        var attached = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯甲", 1, 2, 1));
        var detached = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯乙", 1, 2, 1));
        var branches = new BranchApplication(new IsolatedBranchStore(db)); var snap = await new ChildOrderApplication(new IsolatedChildOrderStore(db)).ReadAsync(g, l, root.PlacementId);
        await branches.DetachAsync(new(Guid.NewGuid(), g, l, detached.PersonId, detached.PlacementId, root.PlacementId, snap.Revision, snap.Evidence));
        var preview = await branches.PreviewSpouseAsync(g, l, root.PersonId, root.PlacementId, mother.PersonId, mother.UnionId);
        Assert.Contains("冯甲", preview.AffectedChildren); Assert.DoesNotContain("冯乙", preview.AffectedChildren);
        var command = new DeleteSpouse(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, mother.PersonId, mother.UnionId, preview.Evidence);
        await branches.DeleteSpouseAsync(command); await branches.DeleteSpouseAsync(command);
        await using (var c = db.OpenContext())
        {
            Assert.True(await c.People.AnyAsync(x => x.Id == mother.PersonId));
            Assert.True(await c.BiologicalParents.AnyAsync(x => x.ChildId == detached.PersonId && x.ParentId == mother.PersonId));
            Assert.False(await c.BiologicalParents.AnyAsync(x => x.ChildId == attached.PersonId && x.Role == ParentRole.Mother));
        }
        var next = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "虚构新母"));
        await using var verify = db.OpenContext();
        Assert.True(await verify.BiologicalParents.AnyAsync(x => x.ChildId == attached.PersonId && x.ParentId == next.PersonId));
        Assert.True(await verify.BiologicalParents.AnyAsync(x => x.ChildId == detached.PersonId && x.ParentId == mother.PersonId));
    }
    [Fact]
    public async Task Detach_then_delete_preserves_detached_parents_and_replay_does_not_recreate()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db)); var rootOp = Guid.NewGuid();
        var root = await roots.ExecuteAsync(new(rootOp, g, l, RootNameMode.Named, "冯父", 1));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var spouseCommand = new AddSpouse(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "虚构母");
        var mother = await spouses.AddAsync(spouseCommand);
        var children = new ChildApplication(new IsolatedChildStore(db));
        var keep = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯保留", 1, 2, 1));
        var childCommand = new AddChild(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯删除", 1, 2, 1);
        var removed = await children.ExecuteAsync(childCommand);
        var branches = new BranchApplication(new IsolatedBranchStore(db)); var snap = await new ChildOrderApplication(new IsolatedChildOrderStore(db)).ReadAsync(g, l, root.PlacementId);
        await branches.DetachAsync(new(Guid.NewGuid(), g, l, keep.PersonId, keep.PlacementId, root.PlacementId, snap.Revision, snap.Evidence));
        var preview = await branches.PreviewDeleteAsync(g, l, root.PersonId, root.PlacementId);
        Assert.Contains("冯删除", preview.People); Assert.DoesNotContain("冯保留", preview.People); Assert.Contains("虚构母", preview.Spouses);
        Assert.Contains("冯父", preview.RetainedPeople); Assert.Contains("虚构母", preview.RetainedPeople);
        var command = new DeleteBranch(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, preview.Evidence);
        await branches.DeleteAsync(command); await branches.DeleteAsync(command);
        var replay = await roots.ExecuteAsync(new(rootOp, g, l, RootNameMode.Named, "冯父", 1)); Assert.True(replay.Replayed);
        Assert.True((await spouses.AddAsync(spouseCommand)).Replayed); Assert.True((await children.ExecuteAsync(childCommand)).Replayed);
        await using var verify = db.OpenContext();
        Assert.False(await verify.Placements.AnyAsync(x => x.Id == root.PlacementId || x.Id == removed.PlacementId));
        Assert.True(await verify.Placements.AnyAsync(x => x.Id == keep.PlacementId && x.ParentPlacementId == null));
        Assert.False(await verify.People.AnyAsync(x => x.Id == removed.PersonId));
        Assert.True(await verify.People.AnyAsync(x => x.Id == root.PersonId)); Assert.True(await verify.People.AnyAsync(x => x.Id == mother.PersonId));
        Assert.Equal(2, await verify.BiologicalParents.CountAsync(x => x.ChildId == keep.PersonId));
        var preservedParents = await branches.ReadRootParentsAsync(g, l, keep.PersonId, keep.PlacementId);
        Assert.Equal("冯父", preservedParents.FatherName); Assert.Equal("虚构母", preservedParents.MotherName);
    }

    [Fact]
    public async Task Delete_rejects_stale_snapshot_and_rolls_back_injected_failure()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var root = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var branches = new BranchApplication(new IsolatedBranchStore(db)); var stale = await branches.PreviewDeleteAsync(g, l, root.PersonId, root.PlacementId);
        await new ChildApplication(new IsolatedChildStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯甲", 0, 2, 1));
        var command = new DeleteBranch(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, stale.Evidence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => branches.DeleteAsync(command));
        var current = await branches.PreviewDeleteAsync(g, l, root.PersonId, root.PlacementId);
        await using (var c = db.OpenContext()) await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailDelete BEFORE INSERT ON ChildOrderReceipts BEGIN SELECT RAISE(ABORT, 'injected'); END");
        var fail = command with { OperationId = Guid.NewGuid(), ExpectedEvidence = current.Evidence };
        await Assert.ThrowsAsync<DbUpdateException>(() => branches.DeleteAsync(fail));
        await using var verify = db.OpenContext(); Assert.Equal(2, await verify.People.CountAsync()); Assert.Equal(2, await verify.Placements.CountAsync()); Assert.False(await branches.HasDeleteReceiptAsync(fail.OperationId));
    }

    [Fact]
    public async Task Detach_preserves_parents_and_descendants_and_reindexes_siblings()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var children = new ChildApplication(new IsolatedChildStore(db));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯甲", 0, 2, 1));
        var sibling = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯乙", 0, 2, 1));
        var grandchild = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, child.PersonId, child.PlacementId, RootNameMode.Named, "冯丙", 0, 3, 2));
        var orders = new ChildOrderApplication(new IsolatedChildOrderStore(db)); var snapshot = await orders.ReadAsync(g, l, root.PlacementId);
        var command = new DetachBranch(Guid.NewGuid(), g, l, child.PersonId, child.PlacementId, root.PlacementId, snapshot.Revision, snapshot.Evidence);
        var branches = new BranchApplication(new IsolatedBranchStore(db));
        await branches.DetachAsync(command); await branches.DetachAsync(command);
        Assert.True(await branches.HasDetachReceiptAsync(command.OperationId));
        var rootsAfter = await roots.ReadAsync(g, l); Assert.Contains(rootsAfter.Nodes, x => x.PersonId == child.PersonId && x.PlacementId == child.PlacementId);
        await using var verify = db.OpenContext();
        Assert.True(await verify.BiologicalParents.AnyAsync(x => x.ChildId == child.PersonId && x.ParentId == root.PersonId));
        Assert.Equal(child.PlacementId, (await verify.Placements.SingleAsync(x => x.Id == grandchild.PlacementId)).ParentPlacementId);
        Assert.Equal(1, (await verify.Placements.SingleAsync(x => x.Id == sibling.PlacementId)).Position);
        await Assert.ThrowsAsync<InvalidOperationException>(() => branches.DetachAsync(command with { PersonId = sibling.PersonId }));
    }

    [Fact]
    public async Task Detach_rejects_stale_and_rolls_back_failure()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using (var c = db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g, "虚构", "冯")); c.Lineages.Add(new(l, g, "虚构")); await c.SaveChangesAsync(); }
        var root = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var children = new ChildApplication(new IsolatedChildStore(db));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯甲", 0, 2, 1));
        var orders = new ChildOrderApplication(new IsolatedChildOrderStore(db)); var stale = await orders.ReadAsync(g, l, root.PlacementId);
        await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯乙", 0, 2, 1));
        var branches = new BranchApplication(new IsolatedBranchStore(db));
        var command = new DetachBranch(Guid.NewGuid(), g, l, child.PersonId, child.PlacementId, root.PlacementId, stale.Revision, stale.Evidence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => branches.DetachAsync(command));
        var current = await orders.ReadAsync(g, l, root.PlacementId);
        await using (var c = db.OpenContext()) await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailDetach BEFORE INSERT ON ChildOrderReceipts BEGIN SELECT RAISE(ABORT, 'injected'); END");
        var fail = command with { OperationId = Guid.NewGuid(), ExpectedRevision = current.Revision, ExpectedEvidence = current.Evidence };
        await Assert.ThrowsAsync<DbUpdateException>(() => branches.DetachAsync(fail));
        await using var verify = db.OpenContext(); Assert.Equal(root.PlacementId, (await verify.Placements.SingleAsync(x => x.Id == child.PlacementId)).ParentPlacementId);
        Assert.False(await branches.HasDetachReceiptAsync(fail.OperationId));
    }
}
