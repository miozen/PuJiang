using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using Xunit;
namespace PuJiang.Tests;
public sealed class V1SuccessionTests
{
    [Fact]
    public async Task Detach_and_reattach_preserve_identity_parents_and_descendants()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup(); using var owned=db;
        var wives=new SpouseApplication(new IsolatedSpouseStore(db)); var oldMother=await wives.AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构原嗣母"));
        var app=new SuccessionApplication(new IsolatedSuccessionStore(db)); var initial=await app.PreviewAsync(g,l,child,birth,tp); var created=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,initial.Evidence));
        var order=new ChildOrderApplication(new IsolatedChildOrderStore(db)); var snapshot=await order.ReadAsync(g,l,tp); var branches=new BranchApplication(new IsolatedBranchStore(db));
        var detach=new DetachBranch(Guid.NewGuid(),g,l,child,created.HeirPlacementId,tp,snapshot.Revision,snapshot.Evidence); await branches.DetachAsync(detach); await branches.DetachAsync(detach);
        await using(var c=db.OpenContext()){Assert.Null((await c.Placements.SingleAsync(x=>x.Id==created.HeirPlacementId)).ParentPlacementId);Assert.Equal(created.HeirPlacementId,(await c.Placements.SingleAsync(x=>x.PersonId==desc)).ParentPlacementId);Assert.Equal(oldMother.PersonId,(await c.Successions.SingleAsync()).HeirMotherId);Assert.True(await c.BiologicalParents.AnyAsync(x=>x.ChildId==child && x.ParentId==f));}
        var parents=await branches.ReadRootParentsAsync(g,l,child,created.HeirPlacementId);await Assert.ThrowsAsync<InvalidOperationException>(()=>branches.UpdateRootParentsAsync(new(Guid.NewGuid(),g,l,child,created.HeirPlacementId,"错误新生父",parents.MotherName,parents.Evidence)));
        Assert.Contains(await new ChildApplication(new IsolatedChildStore(db)).ReadAsync(g,l),x=>x.PlacementId==birth && x.PositionKind==ChildPositionKind.BirthReference);
        var newFather=Guid.NewGuid();var newPlace=Guid.NewGuid();await using(var c=db.OpenContext()){var p=new Person(newFather,l,"冯新嗣父",PersonSex.Male);p.SetGeneration(1);c.People.Add(p);c.Placements.Add(new Placement(newPlace,l,newFather));await c.SaveChangesAsync();}
        var newMother=await wives.AddAsync(new(Guid.NewGuid(),g,l,newFather,newPlace,RootNameMode.Named,"虚构新嗣母"));
        var preview=await app.PreviewAsync(g,l,child,created.HeirPlacementId,newPlace);Assert.Equal(1,preview.DescendantCount);
        var reattach=new CreateSuccession(Guid.NewGuid(),g,l,child,created.HeirPlacementId,newPlace,preview.Evidence);var result=await app.CreateAsync(reattach);Assert.Equal(created.HeirPlacementId,result.HeirPlacementId);Assert.True((await app.CreateAsync(reattach)).Replayed);
        await using(var c=db.OpenContext()){Assert.Equal(2,await c.Placements.CountAsync(x=>x.PersonId==child));Assert.Equal(newPlace,(await c.Placements.SingleAsync(x=>x.Id==created.HeirPlacementId)).ParentPlacementId);Assert.Equal(created.HeirPlacementId,(await c.Placements.SingleAsync(x=>x.PersonId==desc)).ParentPlacementId);var s=await c.Successions.SingleAsync();Assert.Equal(newFather,s.HeirFatherId);Assert.Equal(newMother.PersonId,s.HeirMotherId);Assert.Equal(fp,(await c.Placements.SingleAsync(x=>x.Id==birth)).ParentPlacementId);}
    }
    [Fact]
    public async Task Detach_and_reattach_roll_back_and_reject_stale_confirmation()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;var app=new SuccessionApplication(new IsolatedSuccessionStore(db));
        var initial=await app.PreviewAsync(g,l,child,birth,tp);var created=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,initial.Evidence));
        var order=new ChildOrderApplication(new IsolatedChildOrderStore(db));var snapshot=await order.ReadAsync(g,l,tp);var branches=new BranchApplication(new IsolatedBranchStore(db));
        var cmd=new DetachBranch(Guid.NewGuid(),g,l,child,created.HeirPlacementId,tp,snapshot.Revision,snapshot.Evidence);
        await using(var c=db.OpenContext())await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailDetach BEFORE UPDATE ON Placements BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAsync<DbUpdateException>(()=>branches.DetachAsync(cmd));await using(var c=db.OpenContext()){Assert.Equal(tp,(await c.Placements.SingleAsync(x=>x.Id==created.HeirPlacementId)).ParentPlacementId);Assert.Equal(created.HeirPlacementId,(await c.Placements.SingleAsync(x=>x.PersonId==desc)).ParentPlacementId);}
        Assert.False(await branches.HasDetachReceiptAsync(cmd.OperationId));await using(var c=db.OpenContext())await c.Database.ExecuteSqlRawAsync("DROP TRIGGER FailDetach");await branches.DetachAsync(cmd);
        var newFather=Guid.NewGuid();var newPlace=Guid.NewGuid();await using(var c=db.OpenContext()){var p=new Person(newFather,l,"冯新嗣父",PersonSex.Male);p.SetGeneration(1);c.People.Add(p);c.Placements.Add(new Placement(newPlace,l,newFather));await c.SaveChangesAsync();}
        var preview=await app.PreviewAsync(g,l,child,created.HeirPlacementId,newPlace);var reattach=new CreateSuccession(Guid.NewGuid(),g,l,child,created.HeirPlacementId,newPlace,preview.Evidence);
        await using(var c=db.OpenContext())await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailReattach BEFORE UPDATE ON Successions BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAsync<DbUpdateException>(()=>app.CreateAsync(reattach));await using(var c=db.OpenContext()){Assert.Null((await c.Placements.SingleAsync(x=>x.Id==created.HeirPlacementId)).ParentPlacementId);Assert.Equal(target,(await c.Successions.SingleAsync()).HeirFatherId);}
        Assert.False(await app.HasReceiptAsync(reattach.OperationId));await using(var c=db.OpenContext())await c.Database.ExecuteSqlRawAsync("DROP TRIGGER FailReattach");
        await new SpouseApplication(new IsolatedSpouseStore(db)).AddAsync(new(Guid.NewGuid(),g,l,newFather,newPlace,RootNameMode.Named,"虚构配偶"));await Assert.ThrowsAsync<InvalidOperationException>(()=>app.CreateAsync(reattach));
    }
    [Fact]
    public async Task Heir_without_descendants_can_be_detached_and_reattached()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;
        await using(var c=db.OpenContext()){c.BiologicalParents.RemoveRange(await c.BiologicalParents.Where(x=>x.ChildId==desc).ToListAsync());c.Placements.RemoveRange(await c.Placements.Where(x=>x.PersonId==desc).ToListAsync());await c.SaveChangesAsync();}
        var app=new SuccessionApplication(new IsolatedSuccessionStore(db));var initial=await app.PreviewAsync(g,l,child,birth,tp);Assert.Equal(0,initial.DescendantCount);var created=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,initial.Evidence));
        var group=await new ChildOrderApplication(new IsolatedChildOrderStore(db)).ReadAsync(g,l,tp);await new BranchApplication(new IsolatedBranchStore(db)).DetachAsync(new(Guid.NewGuid(),g,l,child,created.HeirPlacementId,tp,group.Revision,group.Evidence));
        var preview=await app.PreviewAsync(g,l,child,created.HeirPlacementId,tp);Assert.Equal(0,preview.DescendantCount);await app.CreateAsync(new(Guid.NewGuid(),g,l,child,created.HeirPlacementId,tp,preview.Evidence));
        await using var c2=db.OpenContext();Assert.Equal(2,await c2.Placements.CountAsync(x=>x.PersonId==child));Assert.Equal(tp,(await c2.Placements.SingleAsync(x=>x.Id==created.HeirPlacementId)).ParentPlacementId);
    }
    [Fact]
    public async Task Reattachment_restarts_heir_mother_default_and_future_backfill_after_prior_clear()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;var app=new SuccessionApplication(new IsolatedSuccessionStore(db));
        var preview=await app.PreviewAsync(g,l,child,birth,tp);var created=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,preview.Evidence));var info=await app.ReadAsync(g,l,created.HeirPlacementId);Assert.NotNull(info);await app.SetMotherAsync(new(Guid.NewGuid(),g,l,created.SuccessionId,null,null,false,info.Evidence));
        var group=await new ChildOrderApplication(new IsolatedChildOrderStore(db)).ReadAsync(g,l,tp);await new BranchApplication(new IsolatedBranchStore(db)).DetachAsync(new(Guid.NewGuid(),g,l,child,created.HeirPlacementId,tp,group.Revision,group.Evidence));
        var newFather=Guid.NewGuid();var newPlace=Guid.NewGuid();await using(var c=db.OpenContext()){var p=new Person(newFather,l,"冯新嗣父",PersonSex.Male);p.SetGeneration(1);c.People.Add(p);c.Placements.Add(new Placement(newPlace,l,newFather));await c.SaveChangesAsync();}
        var retry=await app.PreviewAsync(g,l,child,created.HeirPlacementId,newPlace);await app.CreateAsync(new(Guid.NewGuid(),g,l,child,created.HeirPlacementId,newPlace,retry.Evidence));var reattached=await app.ReadAsync(g,l,created.HeirPlacementId);Assert.NotNull(reattached);Assert.Null(reattached.MotherId);Assert.False(reattached.MotherCleared);
        var wife=await new SpouseApplication(new IsolatedSpouseStore(db)).AddAsync(new(Guid.NewGuid(),g,l,newFather,newPlace,RootNameMode.Named,"虚构嗣母"));var filled=await app.ReadAsync(g,l,created.HeirPlacementId);Assert.NotNull(filled);Assert.Equal(wife.PersonId,filled.MotherId);
        await app.SetMotherAsync(new(Guid.NewGuid(),g,l,created.SuccessionId,null,wife.PersonId,false,filled.Evidence));var cleared=await app.ReadAsync(g,l,created.HeirPlacementId);Assert.NotNull(cleared);Assert.True(cleared.MotherCleared);Assert.Null(cleared.MotherId);
    }
    [Fact]
    public async Task Reattachment_defaults_new_first_spouse_even_after_prior_clear()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;var app=new SuccessionApplication(new IsolatedSuccessionStore(db));var preview=await app.PreviewAsync(g,l,child,birth,tp);var created=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,preview.Evidence));var info=await app.ReadAsync(g,l,created.HeirPlacementId);Assert.NotNull(info);await app.SetMotherAsync(new(Guid.NewGuid(),g,l,created.SuccessionId,null,null,false,info.Evidence));
        var group=await new ChildOrderApplication(new IsolatedChildOrderStore(db)).ReadAsync(g,l,tp);await new BranchApplication(new IsolatedBranchStore(db)).DetachAsync(new(Guid.NewGuid(),g,l,child,created.HeirPlacementId,tp,group.Revision,group.Evidence));
        var newFather=Guid.NewGuid();var newPlace=Guid.NewGuid();await using(var c=db.OpenContext()){var p=new Person(newFather,l,"冯新嗣父",PersonSex.Male);p.SetGeneration(1);c.People.Add(p);c.Placements.Add(new Placement(newPlace,l,newFather));await c.SaveChangesAsync();}var wife=await new SpouseApplication(new IsolatedSpouseStore(db)).AddAsync(new(Guid.NewGuid(),g,l,newFather,newPlace,RootNameMode.Named,"虚构嗣母"));
        var next=await app.PreviewAsync(g,l,child,created.HeirPlacementId,newPlace);await app.CreateAsync(new(Guid.NewGuid(),g,l,child,created.HeirPlacementId,newPlace,next.Evidence));var result=await app.ReadAsync(g,l,created.HeirPlacementId);Assert.NotNull(result);Assert.Equal(wife.PersonId,result.MotherId);Assert.False(result.MotherCleared);
    }
    [Fact]
    public async Task Reattachment_to_same_heir_father_preserves_explicit_clear()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;var spouses=new SpouseApplication(new IsolatedSpouseStore(db));var wife=await spouses.AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构嗣母"));var app=new SuccessionApplication(new IsolatedSuccessionStore(db));var preview=await app.PreviewAsync(g,l,child,birth,tp);var created=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,preview.Evidence));var info=await app.ReadAsync(g,l,created.HeirPlacementId);Assert.NotNull(info);await app.SetMotherAsync(new(Guid.NewGuid(),g,l,created.SuccessionId,null,wife.PersonId,false,info.Evidence));
        var group=await new ChildOrderApplication(new IsolatedChildOrderStore(db)).ReadAsync(g,l,tp);await new BranchApplication(new IsolatedBranchStore(db)).DetachAsync(new(Guid.NewGuid(),g,l,child,created.HeirPlacementId,tp,group.Revision,group.Evidence));var again=await app.PreviewAsync(g,l,child,created.HeirPlacementId,tp);Assert.Null(again.HeirMotherName);await app.CreateAsync(new(Guid.NewGuid(),g,l,child,created.HeirPlacementId,tp,again.Evidence));var result=await app.ReadAsync(g,l,created.HeirPlacementId);Assert.NotNull(result);Assert.Null(result.MotherId);Assert.True(result.MotherCleared);
    }
    private static async Task<(IsolatedTestDatabase db,Guid g,Guid l,Guid birthFather,Guid birthFatherPlace,Guid child,Guid birth,Guid target,Guid targetPlace,Guid descendant)> Setup()
    {
        var db=IsolatedTestDatabase.CreateNew();var g=Guid.NewGuid();var l=Guid.NewGuid();var f=Guid.NewGuid();var fp=Guid.NewGuid();var target=Guid.NewGuid();var tp=Guid.NewGuid();
        await using(var c=db.OpenContext()) {await c.Database.EnsureCreatedAsync();c.Genealogies.Add(new(g,"虚构","冯"));c.Lineages.Add(new(l,g,"虚构"));var a=new Person(f,l,"冯生父",PersonSex.Male);a.SetGeneration(1);var b=new Person(target,l,"冯嗣父",PersonSex.Male);b.SetGeneration(1);c.People.AddRange(a,b);c.Placements.AddRange(new Placement(fp,l,f),new Placement(tp,l,target));await c.SaveChangesAsync();}
        var app=new ChildApplication(new IsolatedChildStore(db));var child=await app.ExecuteAsync(new(Guid.NewGuid(),g,l,f,fp,RootNameMode.Named,"冯嗣子",0,2,1));var desc=await app.ExecuteAsync(new(Guid.NewGuid(),g,l,child.PersonId,child.PlacementId,RootNameMode.Named,"冯后代",0,3,2));
        return(db,g,l,f,fp,child.PersonId,child.PlacementId,target,tp,desc.PersonId);
    }
    [Fact]
    public async Task Succession_preserves_birth_and_moves_descendant_once_with_idempotence()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;
        var spouse=await new SpouseApplication(new IsolatedSpouseStore(db)).AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构嗣母"));
        var app=new SuccessionApplication(new IsolatedSuccessionStore(db));var preview=await app.PreviewAsync(g,l,child,birth,tp);Assert.Equal(1,preview.DescendantCount);
        var cmd=new CreateSuccession(Guid.NewGuid(),g,l,child,birth,tp,preview.Evidence);var result=await app.CreateAsync(cmd);Assert.True((await app.CreateAsync(cmd)).Replayed);
        await using var c=db.OpenContext();Assert.Equal(2,await c.Placements.CountAsync(x=>x.PersonId==child));Assert.Equal(fp,(await c.Placements.SingleAsync(x=>x.Id==birth)).ParentPlacementId);
        Assert.Equal(result.HeirPlacementId,(await c.Placements.SingleAsync(x=>x.PersonId==desc)).ParentPlacementId);Assert.Equal(2,(await c.People.SingleAsync(x=>x.Id==child)).ExplicitGeneration);Assert.Equal(3,(await c.People.SingleAsync(x=>x.Id==desc)).ExplicitGeneration);
        Assert.True(await c.BiologicalParents.AnyAsync(x=>x.ChildId==child && x.ParentId==f && x.Role==ParentRole.Father));Assert.False(await c.BiologicalParents.AnyAsync(x=>x.ChildId==child && x.ParentId==target));Assert.Equal(spouse.PersonId,(await c.Successions.SingleAsync()).HeirMotherId);
        var projected=await new ChildApplication(new IsolatedChildStore(db)).ReadAsync(g,l); Assert.Contains(projected,x=>x.PlacementId==birth && x.PositionKind==ChildPositionKind.BirthReference); Assert.Contains(projected,x=>x.PlacementId==result.HeirPlacementId && x.PositionKind==ChildPositionKind.Succession); Assert.Contains(projected,x=>x.PersonId==desc && x.ParentPlacementId==result.HeirPlacementId);
        var order=new ChildOrderApplication(new IsolatedChildOrderStore(db));Assert.Single((await order.ReadAsync(g,l,fp)).Items);Assert.Single((await order.ReadAsync(g,l,tp)).Items);Assert.Single((await order.ReadAsync(g,l,result.HeirPlacementId)).Items);
    }
    [Fact]
    public async Task Failure_rolls_back_and_stale_confirmation_rejects()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;var app=new SuccessionApplication(new IsolatedSuccessionStore(db));var preview=await app.PreviewAsync(g,l,child,birth,tp);
        await using(var c=db.OpenContext())await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailSuccession BEFORE INSERT ON Successions BEGIN SELECT RAISE(ABORT, 'injected'); END");
        var cmd=new CreateSuccession(Guid.NewGuid(),g,l,child,birth,tp,preview.Evidence);await Assert.ThrowsAsync<DbUpdateException>(()=>app.CreateAsync(cmd));
        await using(var c=db.OpenContext()){Assert.Equal(1,await c.Placements.CountAsync(x=>x.PersonId==child));Assert.Equal(birth,(await c.Placements.SingleAsync(x=>x.PersonId==desc)).ParentPlacementId);Assert.Empty(await c.Successions.ToListAsync());}
        await using(var c=db.OpenContext())await c.Database.ExecuteSqlRawAsync("DROP TRIGGER FailSuccession");
        await new SpouseApplication(new IsolatedSpouseStore(db)).AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构配偶"));await Assert.ThrowsAsync<InvalidOperationException>(()=>app.CreateAsync(cmd));
    }
    [Fact]
    public async Task First_spouse_fills_heir_mother_but_explicit_clear_is_protected()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;var app=new SuccessionApplication(new IsolatedSuccessionStore(db));
        var preview=await app.PreviewAsync(g,l,child,birth,tp);var result=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,preview.Evidence));
        var spouses=new SpouseApplication(new IsolatedSpouseStore(db));var wife=await spouses.AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构嗣母"));
        var info=await app.ReadAsync(g,l,result.HeirPlacementId);Assert.NotNull(info);Assert.Equal(wife.PersonId,info.MotherId);
        await app.SetMotherAsync(new(Guid.NewGuid(),g,l,result.SuccessionId,null,wife.PersonId,false,info.Evidence));
        var after=await app.ReadAsync(g,l,result.HeirPlacementId);Assert.NotNull(after);Assert.Null(after.MotherId);Assert.True(after.MotherCleared);
        await using var c=db.OpenContext();Assert.True(await c.BiologicalParents.AnyAsync(x=>x.ChildId==child && x.ParentId==f && x.Role==ParentRole.Father));
    }
    [Fact]
    public async Task Explicit_clear_before_first_spouse_blocks_backfill()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;var app=new SuccessionApplication(new IsolatedSuccessionStore(db));
        var preview=await app.PreviewAsync(g,l,child,birth,tp);var result=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,preview.Evidence));
        var info=await app.ReadAsync(g,l,result.HeirPlacementId);Assert.NotNull(info);await app.SetMotherAsync(new(Guid.NewGuid(),g,l,result.SuccessionId,null,null,false,info.Evidence));
        await new SpouseApplication(new IsolatedSpouseStore(db)).AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构嗣母"));
        var after=await app.ReadAsync(g,l,result.HeirPlacementId);Assert.NotNull(after);Assert.Null(after.MotherId);Assert.True(after.MotherCleared);
    }
    [Fact]
    public async Task First_spouse_and_heir_mother_backfill_roll_back_together()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;var app=new SuccessionApplication(new IsolatedSuccessionStore(db));
        var preview=await app.PreviewAsync(g,l,child,birth,tp);var result=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,preview.Evidence));
        await using(var c=db.OpenContext())await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailHeirMother BEFORE UPDATE ON Successions BEGIN SELECT RAISE(ABORT, 'injected'); END");
        var spouses=new SpouseApplication(new IsolatedSpouseStore(db));var command=new AddSpouse(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构嗣母");await Assert.ThrowsAsync<DbUpdateException>(()=>spouses.AddAsync(command));
        await using(var c=db.OpenContext()){Assert.Empty(await c.SpouseUnions.Where(x=>x.FatherId==target).ToListAsync());Assert.Null((await c.Successions.SingleAsync()).HeirMotherId);}
        Assert.False(await spouses.HasReceiptAsync(command.OperationId,false));await using(var c=db.OpenContext())await c.Database.ExecuteSqlRawAsync("DROP TRIGGER FailHeirMother");
        var saved=await spouses.AddAsync(command);var info=await app.ReadAsync(g,l,result.HeirPlacementId);Assert.NotNull(info);Assert.Equal(saved.PersonId,info.MotherId);
    }
    [Fact]
    public async Task Heir_mother_candidates_and_edits_preserve_biological_mother_and_reject_stale_snapshot()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;var spouses=new SpouseApplication(new IsolatedSpouseStore(db));
        var biological=await spouses.AddAsync(new(Guid.NewGuid(),g,l,f,fp,RootNameMode.Named,"虚构生母"));
        var first=await spouses.AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构嗣母甲"));var second=await spouses.AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构嗣母乙"));
        var app=new SuccessionApplication(new IsolatedSuccessionStore(db));var preview=await app.PreviewAsync(g,l,child,birth,tp);var result=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,preview.Evidence));
        var old=await app.ReadAsync(g,l,result.HeirPlacementId);Assert.NotNull(old);Assert.Equal(first.PersonId,old.MotherId);Assert.Equal(new[]{first.PersonId,second.PersonId},old.Candidates.Select(x=>x.PersonId));Assert.DoesNotContain(old.Candidates,x=>x.PersonId==biological.PersonId);
        await app.SetMotherAsync(new(Guid.NewGuid(),g,l,result.SuccessionId,second.PersonId,first.PersonId,false,old.Evidence));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.SetMotherAsync(new(Guid.NewGuid(),g,l,result.SuccessionId,null,first.PersonId,false,old.Evidence)));
        var changed=await app.ReadAsync(g,l,result.HeirPlacementId);Assert.NotNull(changed);await app.SetMotherAsync(new(Guid.NewGuid(),g,l,result.SuccessionId,first.PersonId,second.PersonId,false,changed.Evidence));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.SetMotherAsync(new(Guid.NewGuid(),g,l,result.SuccessionId,null,first.PersonId,false,old.Evidence)));
        await using var c=db.OpenContext();Assert.True(await c.BiologicalParents.AnyAsync(x=>x.ChildId==child && x.ParentId==biological.PersonId && x.Role==ParentRole.Mother));Assert.Equal(first.PersonId,(await c.Successions.SingleAsync()).HeirMotherId);
    }
    [Fact]
    public async Task Heir_order_changes_only_target_position_and_birth_reference_stays()
    {
        var (db,g,l,f,fp,child,birth,target,tp,desc)=await Setup();using var owned=db;var children=new ChildApplication(new IsolatedChildStore(db));
        var sibling=await children.ExecuteAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"冯原子",0,2,1));
        var app=new SuccessionApplication(new IsolatedSuccessionStore(db));var preview=await app.PreviewAsync(g,l,child,birth,tp);var result=await app.CreateAsync(new(Guid.NewGuid(),g,l,child,birth,tp,preview.Evidence));
        var orders=new ChildOrderApplication(new IsolatedChildOrderStore(db));var snapshot=await orders.ReadAsync(g,l,tp);Assert.Equal(2,snapshot.Items.Single(x=>x.PlacementId==result.HeirPlacementId).Position);
        await orders.MoveAsync(new(Guid.NewGuid(),g,l,tp,result.HeirPlacementId,sibling.PlacementId,snapshot.Revision,snapshot.Evidence));
        await using var c=db.OpenContext();Assert.Equal(1,(await c.Placements.SingleAsync(x=>x.Id==result.HeirPlacementId)).Position);Assert.Equal(1,(await c.Placements.SingleAsync(x=>x.Id==birth)).Position);
    }
}
