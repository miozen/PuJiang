using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using Xunit;

namespace PuJiang.Tests;

public sealed class V1ChildSliceTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task Explicit_sex_correction_of_unknown_child_keeps_position_and_identity()
    {
        var (db, g, l, root) = await Setup(); var id = Guid.NewGuid(); var placement = Guid.NewGuid();
        await using (var context = db.OpenContext())
        {
            context.People.Add(new Person(id, l, "冯待核", PersonSex.Unknown));
            context.Placements.Add(new Placement(placement, l, id, root.PlacementId, 1));
            context.BiologicalParents.Add(new BiologicalParent(Guid.NewGuid(), l, id, root.PersonId, ParentRole.Father));
            await context.SaveChangesAsync();
        }
        var profiles = new PersonProfileApplication(new IsolatedPersonProfileStore(db));
        await profiles.UpdateAsync(new(Guid.NewGuid(), g, l, id, placement, "冯待核", PersonSex.Female, "冯待核", PersonSex.Unknown));
        var corrected = await profiles.ReadAsync(g, l, id, placement);
        Assert.Equal(PersonSex.Female, corrected.Sex); Assert.Equal(id, corrected.PersonId); Assert.Equal(placement, corrected.PlacementId);
        Assert.Equal(1, Assert.Single(await new ChildApplication(new IsolatedChildStore(db)).ReadAsync(g, l)).Position);
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Explicit_mother_candidates_and_writes_are_limited_to_fathers_spouses()
    {
        var (db, g, l, root) = await Setup();
        var children = new ChildApplication(new IsolatedChildStore(db)); var family = new SpouseApplication(new IsolatedSpouseStore(db));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯甲"));
        var spouse = await family.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var second = await family.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "赵氏"));
        var other = Guid.NewGuid();
        await using (var context = db.OpenContext()) { context.People.Add(new Person(other, l, "王氏", PersonSex.Female)); await context.SaveChangesAsync(); }
        var candidates = await family.ReadMotherCandidatesAsync(g, l, root.PersonId, root.PlacementId, child.PersonId);
        Assert.Equal(new[] { spouse.PersonId, second.PersonId }, candidates.Select(x => x.PersonId));
        Assert.DoesNotContain(candidates, x => x.PersonId == other);
        await Assert.ThrowsAsync<InvalidOperationException>(() => family.SetMotherAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, child.PersonId, other, spouse.PersonId, false)));
        Assert.Equal(spouse.PersonId, Assert.Single(await children.ReadAsync(g, l)).MotherId);
        await family.SetMotherAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, child.PersonId, second.PersonId, spouse.PersonId, false));
        Assert.Equal(second.PersonId, Assert.Single(await children.ReadAsync(g, l)).MotherId);
        await family.SetMotherAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, child.PersonId, null, second.PersonId, false));
        var reread = Assert.Single(await children.ReadAsync(g, l)); Assert.Null(reread.MotherId); Assert.True(reread.MotherExplicitlyCleared);
        await Assert.ThrowsAsync<InvalidOperationException>(() => family.SetMotherAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, child.PersonId, spouse.PersonId, second.PersonId, false)));
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Explicit_mother_cannot_be_descendant_of_child()
    {
        var (db, g, l, root) = await Setup();
        var children = new ChildApplication(new IsolatedChildStore(db)); var family = new SpouseApplication(new IsolatedSpouseStore(db));
        var a = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯甲"));
        var descendant = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, a.PersonId, a.PlacementId, RootNameMode.Named, "冯乙"));
        var profiles = new PersonProfileApplication(new IsolatedPersonProfileStore(db));
        await profiles.UpdateAsync(new(Guid.NewGuid(), g, l, descendant.PersonId, descendant.PlacementId, "冯乙", PersonSex.Female, "冯乙", PersonSex.Male));
        await using (var context = db.OpenContext()) { var u=Guid.NewGuid(); context.SpouseUnions.Add(new(u, l, root.PersonId, descendant.PersonId)); context.SpouseOrderGroups.Add(new() {LineageId=l,FatherId=root.PersonId,Revision=1}); context.SpouseOrderEntries.Add(new() {LineageId=l,FatherId=root.PersonId,UnionId=u,Position=1}); await context.SaveChangesAsync(); }
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => family.SetMotherAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, a.PersonId, descendant.PersonId, null, false)));
        Assert.Contains("循环", error.Message);
        Assert.Null((await children.ReadAsync(g, l)).Single(x => x.PersonId == a.PersonId).MotherId);
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Existing_non_spouse_mother_is_preserved_on_read_and_can_be_explicitly_cleared()
    {
        var (db, g, l, root) = await Setup();
        var children = new ChildApplication(new IsolatedChildStore(db)); var family = new SpouseApplication(new IsolatedSpouseStore(db));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯甲"));
        var outsider = Guid.NewGuid();
        await using (var context = db.OpenContext())
        {
            context.People.Add(new Person(outsider, l, "待核实", PersonSex.Female));
            context.BiologicalParents.Add(new BiologicalParent(Guid.NewGuid(), l, child.PersonId, outsider, ParentRole.Mother));
            await context.SaveChangesAsync();
        }
        Assert.Empty(await family.ReadMotherCandidatesAsync(g, l, root.PersonId, root.PlacementId, child.PersonId));
        Assert.Equal(outsider, Assert.Single(await children.ReadAsync(g, l)).MotherId);
        await family.SetMotherAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, child.PersonId, null, outsider, false));
        var read = Assert.Single(await children.ReadAsync(g, l)); Assert.Null(read.MotherId); Assert.True(read.MotherExplicitlyCleared);
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Profile_updates_keep_identity_reorder_and_reject_parent_role_conflict()
    {
        var (db, g, l, root) = await Setup();
        var children = new ChildApplication(new IsolatedChildStore(db));
        var profiles = new PersonProfileApplication(new IsolatedPersonProfileStore(db));
        var a = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Empty, ""));
        var b = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯乙"));
        var update = new UpdatePersonProfile(Guid.NewGuid(), g, l, a.PersonId, a.PlacementId, "冯甲", PersonSex.Female, "", PersonSex.Male);
        await profiles.UpdateAsync(update); await profiles.UpdateAsync(update);
        var nodes = await children.ReadAsync(g, l);
        Assert.Equal(2, nodes.Single(x => x.PersonId == a.PersonId).Position);
        Assert.Equal(1, nodes.Single(x => x.PersonId == b.PersonId).Position);
        Assert.Equal("冯甲", (await profiles.ReadAsync(g, l, a.PersonId, a.PlacementId)).Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.UpdateAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, "冯父", PersonSex.Female, "冯父", PersonSex.Male)));
        Assert.Equal(PersonSex.Male, (await profiles.ReadAsync(g, l, root.PersonId, root.PlacementId)).Sex);
        await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.UpdateAsync(update with { OperationId = Guid.NewGuid() }));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var spouse = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var spouseProfile = await profiles.ReadAsync(g, l, spouse.PersonId, root.PlacementId);
        await profiles.UpdateAsync(new(Guid.NewGuid(), g, l, spouse.PersonId, root.PlacementId, "李夫人", PersonSex.Female, spouseProfile.Name, spouseProfile.Sex));
        await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.UpdateAsync(new(Guid.NewGuid(), g, l, spouse.PersonId, root.PlacementId, "李夫人", PersonSex.Male, "李夫人", PersonSex.Female)));
    }
    [Fact, Trait("Category", "Integration")]
    public async Task New_male_child_precedes_existing_female_siblings_without_changing_relationships()
    {
        var (db, g, l, root) = await Setup(); var daughterId = Guid.NewGuid(); var daughterPlace = Guid.NewGuid(); var relationId = Guid.NewGuid();
        await using (var context = db.OpenContext())
        {
            context.People.Add(new Person(daughterId, l, "冯女", PersonSex.Female));
            context.Placements.Add(new Placement(daughterPlace, l, daughterId, root.PlacementId, 1));
            context.BiologicalParents.Add(new BiologicalParent(relationId, l, daughterId, root.PersonId, ParentRole.Father));
            await context.SaveChangesAsync();
        }
        var children = new ChildApplication(new IsolatedChildStore(db));
        var son = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Empty, ""));
        var read = await children.ReadAsync(g, l);
        Assert.Equal(1, read.Single(x => x.PersonId == son.PersonId).Position);
        var daughter = read.Single(x => x.PersonId == daughterId); Assert.Equal(2, daughter.Position); Assert.Equal(relationId, daughter.FatherRelationshipId);
    }
    private static async Task<(IsolatedTestDatabase Db, Guid G, Guid L, RootCreated Root)> Setup()
    {
        var database=IsolatedTestDatabase.CreateNew(); var g=Guid.NewGuid();var l=Guid.NewGuid();
        await using(var db=database.OpenContext()){await db.Database.EnsureCreatedAsync();db.Genealogies.Add(new(g,"虚构项目","冯"));db.Lineages.Add(new(l,g,"虚构谱"));await db.SaveChangesAsync();}
        var root=await new FirstRootApplication(new IsolatedFirstRootStore(database)).ExecuteAsync(new(Guid.NewGuid(),g,l,RootNameMode.Named,"冯父"));
        return(database,g,l,root);
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Repair_of_missing_group_rejects_prior_child_draft()
    {
        var (db, g, l, root) = await Setup(); var app = new SpouseApplication(new IsolatedSpouseStore(db));
        var a = await app.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        await using (var c = db.OpenContext())
        {
            await c.Database.OpenConnectionAsync(); await c.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
            await c.Database.ExecuteSqlRawAsync("DELETE FROM SpouseOrderGroups");
        }
        var original = await app.ReadRepairAsync(g, l, root.PersonId, root.PlacementId);
        await app.RepairAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, original.Evidence, new[] { a.UnionId }));
        var repaired = await app.ReadSnapshotAsync(g, l, root.PersonId, root.PlacementId); Assert.True(repaired.Revision > 1);
        var children = new ChildApplication(new IsolatedChildStore(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "旧稿", 1)));
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Repair_of_invalid_union_reference_archives_the_orphan()
    {
        var (db, g, l, root) = await Setup(); var app = new SpouseApplication(new IsolatedSpouseStore(db));
        var a = await app.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var orphan = Guid.NewGuid();
        await using (var c = db.OpenContext())
        {
            await c.Database.OpenConnectionAsync(); await c.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
            await c.Database.ExecuteSqlInterpolatedAsync($"UPDATE SpouseOrderEntries SET UnionId={orphan} WHERE UnionId={a.UnionId}");
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.ReadSnapshotAsync(g, l, root.PersonId, root.PlacementId));
        var original = await app.ReadRepairAsync(g, l, root.PersonId, root.PlacementId);
        await app.RepairAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, original.Evidence, new[] { a.UnionId }));
        await using (var c = db.OpenContext()) { var saved = await c.SpouseOrderRepairReceipts.SingleAsync(); Assert.Contains(orphan.ToString(), saved.OriginalSnapshot, StringComparison.OrdinalIgnoreCase); }
        Assert.Equal(a.UnionId, Assert.Single((await app.ReadSnapshotAsync(g, l, root.PersonId, root.PlacementId)).Nodes).UnionId);
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Explicit_repair_preserves_original_and_rolls_back_before_reopen()
    {
        var (db, g, l, root) = await Setup(); var app = new SpouseApplication(new IsolatedSpouseStore(db)); var children = new ChildApplication(new IsolatedChildStore(db));
        var a = await app.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var b = await app.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "王氏"));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯甲", 2));
        await using (var c = db.OpenContext()) { var row = await c.SpouseOrderEntries.SingleAsync(x => x.UnionId == b.UnionId); row.Position = 3; await c.SaveChangesAsync(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.ReadSnapshotAsync(g, l, root.PersonId, root.PlacementId));
        var original = await app.ReadRepairAsync(g, l, root.PersonId, root.PlacementId);
        var repair = new RepairSpouseOrder(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, original.Evidence, new[] { b.UnionId, a.UnionId });
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.RepairAsync(repair with { OrderedUnionIds = new[] { a.UnionId } }));
        await using (var c = db.OpenContext()) { var row = await c.SpouseOrderEntries.SingleAsync(x => x.UnionId == b.UnionId); row.Position = 4; await c.SaveChangesAsync(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.RepairAsync(repair));
        await using (var c = db.OpenContext()) { var row = await c.SpouseOrderEntries.SingleAsync(x => x.UnionId == b.UnionId); row.Position = 3; await c.SaveChangesAsync(); await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailRepair BEFORE INSERT ON SpouseOrderEntries BEGIN SELECT RAISE(ABORT, 'injected'); END"); }
        await Assert.ThrowsAsync<DbUpdateException>(() => app.RepairAsync(repair));
        await using (var c = db.OpenContext()) { Assert.Equal(3, (await c.SpouseOrderEntries.SingleAsync(x => x.UnionId == b.UnionId)).Position); Assert.Empty(await c.SpouseOrderRepairReceipts.ToListAsync()); await c.Database.ExecuteSqlRawAsync("DROP TRIGGER FailRepair"); }
        await app.RepairAsync(repair); await app.RepairAsync(repair);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.RepairAsync(repair with { OrderedUnionIds = new[] { a.UnionId, b.UnionId } }));
        await using (var c = db.OpenContext()) { var receipt = await c.SpouseOrderRepairReceipts.SingleAsync(); Assert.Equal(original.Evidence, receipt.OriginalSnapshot); Assert.Equal(2, await c.SpouseUnions.CountAsync()); }
        var reopened = await new SpouseApplication(new IsolatedSpouseStore(db)).ReadSnapshotAsync(g, l, root.PersonId, root.PlacementId);
        Assert.Equal(new[] { b.UnionId, a.UnionId }, reopened.Nodes.Select(x => x.UnionId)); Assert.True(reopened.Revision > 2);
        Assert.Equal(a.PersonId, Assert.Single(await children.ReadAsync(g, l)).MotherId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "旧草稿", 2)));
        var next = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯乙", reopened.Revision));
        Assert.Equal(b.PersonId, (await children.ReadAsync(g, l)).Single(x => x.PersonId == next.PersonId).MotherId);
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Reordering_is_persisted_idempotent_and_affects_only_future_mothers()
    {
        var (db, g, l, root) = await Setup();
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var children = new ChildApplication(new IsolatedChildStore(db));
        var first = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var second = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "王氏"));
        var before = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯甲", 2));
        var move = new MoveSpouse(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, second.UnionId, first.UnionId, 2);
        await spouses.MoveAsync(move); await spouses.MoveAsync(move);
        await Assert.ThrowsAsync<InvalidOperationException>(() => spouses.MoveAsync(move with { TargetUnionId = second.UnionId, SourceUnionId = first.UnionId }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => spouses.MoveAsync(move with { OperationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "过期", 2)));
        var after = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯乙", 3));
        var reopened = new SpouseApplication(new IsolatedSpouseStore(db));
        var snapshot = await reopened.ReadSnapshotAsync(g, l, root.PersonId, root.PlacementId);
        Assert.Equal(3, snapshot.Revision); Assert.Equal(new[] { second.PersonId, first.PersonId }, snapshot.Nodes.Select(x => x.PersonId));
        var read = await children.ReadAsync(g, l);
        Assert.Equal(first.PersonId, read.Single(x => x.PersonId == before.PersonId).MotherId);
        Assert.Equal(second.PersonId, read.Single(x => x.PersonId == after.PersonId).MotherId);
        await using var verify = db.OpenContext(); Assert.Equal(2, await verify.SpouseUnions.CountAsync()); Assert.Equal(2, await verify.Placements.CountAsync(x => x.ParentPlacementId == root.PlacementId));
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Reordering_one_lineage_does_not_change_another()
    {
        var (db, g, l, root) = await Setup(); var other = Guid.NewGuid();
        await using (var context = db.OpenContext()) { context.Lineages.Add(new(other, g, "另一虚构谱")); await context.SaveChangesAsync(); }
        var otherRoot = await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(), g, other, RootNameMode.Named, "冯他"));
        var app = new SpouseApplication(new IsolatedSpouseStore(db));
        var a = await app.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var b = await app.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "王氏"));
        var c = await app.AddAsync(new(Guid.NewGuid(), g, other, otherRoot.PersonId, otherRoot.PlacementId, RootNameMode.Named, "赵氏"));
        await app.MoveAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, b.UnionId, a.UnionId, 2));
        var untouched = await app.ReadSnapshotAsync(g, other, otherRoot.PersonId, otherRoot.PlacementId);
        Assert.Equal(1, untouched.Revision); Assert.Equal(c.UnionId, Assert.Single(untouched.Nodes).UnionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.ReadSnapshotAsync(g, other, root.PersonId, root.PlacementId));
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Failed_reorder_rolls_back_positions_revision_and_receipt()
    {
        var (db, g, l, root) = await Setup(); var app = new SpouseApplication(new IsolatedSpouseStore(db));
        var a = await app.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var b = await app.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "王氏"));
        await using (var context = db.OpenContext()) await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailOrder BEFORE UPDATE ON SpouseOrderEntries BEGIN SELECT RAISE(ABORT, 'injected'); END");
        var move = new MoveSpouse(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, b.UnionId, a.UnionId, 2);
        await Assert.ThrowsAsync<DbUpdateException>(() => app.MoveAsync(move));
        var snapshot = await app.ReadSnapshotAsync(g, l, root.PersonId, root.PlacementId);
        Assert.Equal(2, snapshot.Revision); Assert.Equal(new[] { a.UnionId, b.UnionId }, snapshot.Nodes.Select(x => x.UnionId));
        Assert.False(await app.HasOrderReceiptAsync(move.OperationId));
        await using (var context = db.OpenContext()) await context.Database.ExecuteSqlRawAsync("DROP TRIGGER FailOrder");
        await app.MoveAsync(move); Assert.True(await app.HasOrderReceiptAsync(move.OperationId));
    }
    [Fact, Trait("Category","Integration")]
    public async Task Child_first_spouse_backfills_only_eligible_and_clear_survives_reopen()
    {
        var (db,g,l,root)=await Setup();var children=new ChildApplication(new IsolatedChildStore(db));var spouses=new SpouseApplication(new IsolatedSpouseStore(db));
        AddChild Child(string name)=>new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,name);
        var a=await children.ExecuteAsync(Child("冯甲"));var b=await children.ExecuteAsync(Child("冯乙"));
        await spouses.SetMotherAsync(new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,b.PersonId,null,null,false));
        var add=new AddSpouse(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"李氏");
        var first=await spouses.AddAsync(add); Assert.True((await spouses.AddAsync(add)).Replayed);
        await using(var verify=db.OpenContext())
        {
            Assert.Equal(PersonSex.Female,(await verify.People.SingleAsync(x=>x.Id==first.PersonId)).Sex);
            var mothers=await verify.BiologicalParents.Where(x=>x.Role==ParentRole.Mother).ToListAsync();Assert.Single(mothers);Assert.Equal(a.PersonId,mothers[0].ChildId);Assert.Equal(first.PersonId,mothers[0].ParentId);
            Assert.True((await verify.People.SingleAsync(x=>x.Id==b.PersonId)).MotherExplicitlyCleared);
        }
        var second=await spouses.AddAsync(new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"王氏"));
        var cCommand=Child("冯丙") with { ExpectedSpouseRevision = 2 };var results=await Task.WhenAll(children.ExecuteAsync(cCommand),children.ExecuteAsync(cCommand));
        Assert.Equal(results[0].PersonId,results[1].PersonId);Assert.Single(results,x=>x.Replayed);
        await using(var verify=db.OpenContext())
        {
            var mother=await verify.BiologicalParents.SingleAsync(x=>x.ChildId==results[0].PersonId && x.Role==ParentRole.Mother);Assert.Equal(first.PersonId,mother.ParentId);Assert.NotEqual(second.PersonId,mother.ParentId);
            Assert.Equal(3,await verify.Placements.CountAsync(x=>x.ParentPlacementId==root.PlacementId));
            Assert.Equal(new[]{1,2,3},await verify.Placements.Where(x=>x.ParentPlacementId==root.PlacementId).OrderBy(x=>x.Position).Select(x=>x.Position!.Value).ToArrayAsync());
        }
        var nodes=await children.ReadAsync(g,l);Assert.Equal(3,nodes.Count);Assert.All(nodes,n=>Assert.Equal(root.PlacementId,n.ParentPlacementId));
    }
    [Fact, Trait("Category","Integration")]
    public async Task Failed_backfill_rolls_back_spouse_and_retry_reuses_operation()
    {
        var(db,g,l,root)=await Setup();var children=new ChildApplication(new IsolatedChildStore(db));var spouses=new SpouseApplication(new IsolatedSpouseStore(db));
        await children.ExecuteAsync(new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"冯甲"));
        await using(var context=db.OpenContext())await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailMother BEFORE INSERT ON BiologicalParents WHEN NEW.Role = 2 BEGIN SELECT RAISE(ABORT, 'injected'); END");
        var command=new AddSpouse(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"李氏");
        await Assert.ThrowsAsync<DbUpdateException>(()=>spouses.AddAsync(command));
        await using(var context=db.OpenContext()){Assert.Equal(2,await context.People.CountAsync());Assert.Empty(await context.SpouseUnions.ToListAsync());Assert.Empty(await context.BiologicalParents.Where(x=>x.Role==ParentRole.Mother).ToListAsync());await context.Database.ExecuteSqlRawAsync("DROP TRIGGER FailMother");}
        var saved=await spouses.AddAsync(command);Assert.False(saved.Replayed);
        await using var verify=db.OpenContext();Assert.Equal(saved.PersonId,(await verify.BiologicalParents.SingleAsync(x=>x.Role==ParentRole.Mother)).ParentId);
    }
    [Fact, Trait("Category","Integration")]
    public async Task Invalid_position_and_damaged_spouse_order_fail_without_child()
    {
        var(db,g,l,root)=await Setup();var children=new ChildApplication(new IsolatedChildStore(db));
        var command=new AddChild(Guid.NewGuid(),g,l,root.PersonId,Guid.NewGuid(),RootNameMode.Named,"冯甲");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>children.ExecuteAsync(command));
        await using(var context=db.OpenContext()) { var spouse=new Person(Guid.NewGuid(),l,"李氏",PersonSex.Female);var u=Guid.NewGuid();context.People.Add(spouse);context.SpouseUnions.Add(new(u,l,root.PersonId,spouse.Id));context.SpouseOrderGroups.Add(new(){LineageId=l,FatherId=root.PersonId,Revision=1});context.SpouseOrderEntries.Add(new(){LineageId=l,FatherId=root.PersonId,UnionId=u,Position=2});await context.SaveChangesAsync(); }
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>children.ExecuteAsync(command with {ParentPlacementId=root.PlacementId})); Assert.Contains("排序",error.Message);
        await using var verify=db.OpenContext();Assert.Empty(await verify.BiologicalParents.ToListAsync());Assert.Single(await verify.Placements.ToListAsync());
    }
    [Fact, Trait("Category","Integration")]
    public async Task Mismatched_mother_role_and_failed_child_write_are_rejected_without_partial_data()
    {
        var(db,g,l,root)=await Setup(); var childApp=new ChildApplication(new IsolatedChildStore(db));
        var command=new AddChild(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"冯甲",1);
        await using(var context=db.OpenContext())
        {
            var spouse=new Person(Guid.NewGuid(),l,"待核实配偶",PersonSex.Male);
            var u=Guid.NewGuid(); context.People.Add(spouse); context.SpouseUnions.Add(new(u,l,root.PersonId,spouse.Id)); context.SpouseOrderGroups.Add(new(){LineageId=l,FatherId=root.PersonId,Revision=1}); context.SpouseOrderEntries.Add(new(){LineageId=l,FatherId=root.PersonId,UnionId=u,Position=1}); await context.SaveChangesAsync();
        }
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>childApp.ExecuteAsync(command)); Assert.Contains("角色",error.Message);
        await using(var context=db.OpenContext())
        {
            Assert.Single(await context.Placements.ToListAsync()); Assert.Empty(await context.BiologicalParents.ToListAsync());
            await context.SpouseOrderEntries.ExecuteDeleteAsync(); await context.SpouseOrderGroups.ExecuteDeleteAsync(); await context.SpouseUnions.ExecuteDeleteAsync();
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailChild BEFORE INSERT ON BiologicalParents BEGIN SELECT RAISE(ABORT, 'injected'); END");
        }
        command=command with { ExpectedSpouseRevision = 0 };
        await Assert.ThrowsAsync<DbUpdateException>(()=>childApp.ExecuteAsync(command));
        await using(var context=db.OpenContext())
        {
            Assert.Single(await context.Placements.ToListAsync()); Assert.Empty(await context.BiologicalParents.ToListAsync());
            Assert.Equal(2,await context.People.CountAsync()); await context.Database.ExecuteSqlRawAsync("DROP TRIGGER FailChild");
        }
        var saved=await childApp.ExecuteAsync(command); Assert.False(saved.Replayed);
        Assert.Equal(saved.PersonId, Assert.Single(await childApp.ReadAsync(g,l)).PersonId);
    }
}
