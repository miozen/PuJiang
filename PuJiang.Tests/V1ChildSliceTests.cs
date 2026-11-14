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
        await using (var context = db.OpenContext()) { context.SpouseUnions.Add(new(Guid.NewGuid(), l, root.PersonId, descendant.PersonId, 1)); await context.SaveChangesAsync(); }
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
        var cCommand=Child("冯丙");var results=await Task.WhenAll(children.ExecuteAsync(cCommand),children.ExecuteAsync(cCommand));
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
        await using(var context=db.OpenContext()) { var spouse=new Person(Guid.NewGuid(),l,"李氏",PersonSex.Female);context.People.Add(spouse);context.SpouseUnions.Add(new(Guid.NewGuid(),l,root.PersonId,spouse.Id,2));await context.SaveChangesAsync(); }
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>children.ExecuteAsync(command with {ParentPlacementId=root.PlacementId})); Assert.Contains("排序",error.Message);
        await using var verify=db.OpenContext();Assert.Empty(await verify.BiologicalParents.ToListAsync());Assert.Single(await verify.Placements.ToListAsync());
    }
    [Fact, Trait("Category","Integration")]
    public async Task Mismatched_mother_role_and_failed_child_write_are_rejected_without_partial_data()
    {
        var(db,g,l,root)=await Setup(); var childApp=new ChildApplication(new IsolatedChildStore(db));
        var command=new AddChild(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"冯甲");
        await using(var context=db.OpenContext())
        {
            var spouse=new Person(Guid.NewGuid(),l,"待核实配偶",PersonSex.Male);
            context.People.Add(spouse); context.SpouseUnions.Add(new(Guid.NewGuid(),l,root.PersonId,spouse.Id,1)); await context.SaveChangesAsync();
        }
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>childApp.ExecuteAsync(command)); Assert.Contains("角色",error.Message);
        await using(var context=db.OpenContext())
        {
            Assert.Single(await context.Placements.ToListAsync()); Assert.Empty(await context.BiologicalParents.ToListAsync());
            await context.SpouseUnions.ExecuteDeleteAsync();
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailChild BEFORE INSERT ON BiologicalParents BEGIN SELECT RAISE(ABORT, 'injected'); END");
        }
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
