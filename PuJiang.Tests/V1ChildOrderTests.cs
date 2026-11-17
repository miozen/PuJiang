using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using Xunit;
namespace PuJiang.Tests;

public sealed class V1ChildOrderTests
{
    private static async Task<(IsolatedTestDatabase Db,Guid G,Guid L,RootCreated Root,ChildCreated A,ChildCreated B,ChildCreated C)> Setup()
    {
        var db=IsolatedTestDatabase.CreateNew(); var g=Guid.NewGuid(); var l=Guid.NewGuid();
        await using(var c=db.OpenContext()) { await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g,"虚构","冯")); c.Lineages.Add(new(l,g,"虚构")); await c.SaveChangesAsync(); }
        var root=await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(),g,l,RootNameMode.Named,"冯父",1));
        var children=new ChildApplication(new IsolatedChildStore(db));
        async Task<ChildCreated> Add(string n)=>await children.ExecuteAsync(new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,n,0,2,1));
        var a=await Add("冯甲"); var b=await Add("冯乙"); var c1=await Add("冯丙");
        var profiles=new PersonProfileApplication(new IsolatedPersonProfileStore(db));
        await profiles.UpdateAsync(new(Guid.NewGuid(),g,l,c1.PersonId,c1.PlacementId,"冯丙",PersonSex.Female,"冯丙",PersonSex.Male));
        return(db,g,l,root,a,b,c1);
    }
    [Fact]
    public async Task Reorder_is_atomic_idempotent_grouped_and_survives_reopen()
    {
        var (db,g,l,root,a,b,c)=await Setup(); var app=new ChildOrderApplication(new IsolatedChildOrderStore(db));
        var snap=await app.ReadAsync(g,l,root.PlacementId); var command=new MoveChildOrder(Guid.NewGuid(),g,l,root.PlacementId,b.PlacementId,a.PlacementId,snap.Revision,snap.Evidence);
        await app.MoveAsync(command); await app.MoveAsync(command);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.MoveAsync(command with {TargetPlacementId=c.PlacementId}));
        var read=await new ChildOrderApplication(new IsolatedChildOrderStore(db)).ReadAsync(g,l,root.PlacementId);
        Assert.Equal(new[]{b.PlacementId,a.PlacementId,c.PlacementId},read.Items.Select(x=>x.PlacementId));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.MoveAsync(new(Guid.NewGuid(),g,l,root.PlacementId,a.PlacementId,c.PlacementId,read.Revision,read.Evidence)));
        await using var verify=db.OpenContext(); Assert.Equal(3,await verify.BiologicalParents.CountAsync()); Assert.Equal(4,await verify.Placements.CountAsync());
    }
    [Fact]
    public async Task Changed_back_and_failed_receipt_cannot_overwrite_or_partially_commit()
    {
        var (db,g,l,root,a,b,_)=await Setup(); var app=new ChildOrderApplication(new IsolatedChildOrderStore(db));
        var old=await app.ReadAsync(g,l,root.PlacementId);
        await app.MoveAsync(new(Guid.NewGuid(),g,l,root.PlacementId,b.PlacementId,a.PlacementId,old.Revision,old.Evidence));
        var middle=await app.ReadAsync(g,l,root.PlacementId);
        await app.MoveAsync(new(Guid.NewGuid(),g,l,root.PlacementId,a.PlacementId,b.PlacementId,middle.Revision,middle.Evidence));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.MoveAsync(new(Guid.NewGuid(),g,l,root.PlacementId,b.PlacementId,a.PlacementId,old.Revision,old.Evidence)));
        var current=await app.ReadAsync(g,l,root.PlacementId);
        await using(var x=db.OpenContext()) await x.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailChildOrder BEFORE INSERT ON ChildOrderReceipts BEGIN SELECT RAISE(ABORT, 'injected'); END");
        var fail=new MoveChildOrder(Guid.NewGuid(),g,l,root.PlacementId,b.PlacementId,a.PlacementId,current.Revision,current.Evidence);
        await Assert.ThrowsAsync<DbUpdateException>(()=>app.MoveAsync(fail));
        var after=await app.ReadAsync(g,l,root.PlacementId); Assert.Equal(current.Revision,after.Revision); Assert.Equal(current.Evidence,after.Evidence); Assert.False(await app.HasReceiptAsync(fail.OperationId));
    }
    [Fact]
    public async Task New_child_and_sex_change_invalidate_prior_snapshots()
    {
        var (db,g,l,root,a,b,_)=await Setup(); var app=new ChildOrderApplication(new IsolatedChildOrderStore(db));
        var old=await app.ReadAsync(g,l,root.PlacementId);
        await new ChildApplication(new IsolatedChildStore(db)).ExecuteAsync(new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"冯丁",0,2,1));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.MoveAsync(new(Guid.NewGuid(),g,l,root.PlacementId,b.PlacementId,a.PlacementId,old.Revision,old.Evidence)));
        var next=await app.ReadAsync(g,l,root.PlacementId);
        await new PersonProfileApplication(new IsolatedPersonProfileStore(db)).UpdateAsync(new(Guid.NewGuid(),g,l,b.PersonId,b.PlacementId,"冯乙",PersonSex.Female,"冯乙",PersonSex.Male));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.MoveAsync(new(Guid.NewGuid(),g,l,root.PlacementId,b.PlacementId,a.PlacementId,next.Revision,next.Evidence)));
    }
    [Fact]
    public async Task Foreign_lineage_and_parent_are_rejected_without_changing_order()
    {
        var (db,g,l,root,a,b,_)=await Setup(); var other=Guid.NewGuid();
        await using(var c=db.OpenContext()) { c.Lineages.Add(new(other,g,"另一虚构谱")); await c.SaveChangesAsync(); }
        var otherRoot=await new FirstRootApplication(new IsolatedFirstRootStore(db)).ExecuteAsync(new(Guid.NewGuid(),g,other,RootNameMode.Named,"冯另一",1));
        var app=new ChildOrderApplication(new IsolatedChildOrderStore(db)); var before=await app.ReadAsync(g,l,root.PlacementId);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.MoveAsync(new(Guid.NewGuid(),g,other,root.PlacementId,b.PlacementId,a.PlacementId,before.Revision,before.Evidence)));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.MoveAsync(new(Guid.NewGuid(),g,l,otherRoot.PlacementId,b.PlacementId,a.PlacementId,before.Revision,before.Evidence)));
        var after=await app.ReadAsync(g,l,root.PlacementId); Assert.Equal(before.Evidence,after.Evidence); Assert.Equal(before.Revision,after.Revision);
        Assert.Empty((await app.ReadAsync(g,other,otherRoot.PlacementId)).Items);
    }
    [Fact]
    public async Task Damaged_positions_are_preserved_and_adjustment_is_rejected()
    {
        var (db,g,l,root,a,b,_)=await Setup(); var app=new ChildOrderApplication(new IsolatedChildOrderStore(db)); var before=await app.ReadAsync(g,l,root.PlacementId);
        await using(var c=db.OpenContext()) { var p=await c.Placements.SingleAsync(x=>x.Id==b.PlacementId); p.SetPosition(1); await c.SaveChangesAsync(); }
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.MoveAsync(new(Guid.NewGuid(),g,l,root.PlacementId,b.PlacementId,a.PlacementId,before.Revision,before.Evidence)));
        await using var verify=db.OpenContext(); Assert.Equal(1,(await verify.Placements.SingleAsync(x=>x.Id==a.PlacementId)).Position); Assert.Equal(1,(await verify.Placements.SingleAsync(x=>x.Id==b.PlacementId)).Position);
    }
    [Fact]
    public async Task Duplicate_person_positions_under_same_parent_are_rejected()
    {
        var (db,g,l,root,a,b,daughter)=await Setup(); var app=new ChildOrderApplication(new IsolatedChildOrderStore(db)); var before=await app.ReadAsync(g,l,root.PlacementId);
        await using(var c=db.OpenContext()) { c.Placements.Add(new(Guid.NewGuid(),l,daughter.PersonId,root.PlacementId,4)); await c.SaveChangesAsync(); }
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.MoveAsync(new(Guid.NewGuid(),g,l,root.PlacementId,b.PlacementId,a.PlacementId,before.Revision,before.Evidence)));
        await using var verify=db.OpenContext(); Assert.Equal(2,await verify.Placements.CountAsync(x=>x.PersonId==daughter.PersonId));
    }
    [Fact]
    public async Task Failure_after_first_position_update_rolls_back_positions_version_and_receipt()
    {
        var (db,g,l,root,a,b,_)=await Setup(); var app=new ChildOrderApplication(new IsolatedChildOrderStore(db)); var before=await app.ReadAsync(g,l,root.PlacementId);
        await using(var c=db.OpenContext())
        {
            await c.Database.ExecuteSqlRawAsync("CREATE TABLE OrderFailureProbe (N INTEGER NOT NULL)");
            await c.Database.ExecuteSqlRawAsync("INSERT INTO OrderFailureProbe (N) VALUES (0)");
            await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailSecondOrderUpdate AFTER UPDATE OF Position ON Placements BEGIN UPDATE OrderFailureProbe SET N=N+1; SELECT CASE WHEN (SELECT N FROM OrderFailureProbe)=2 THEN RAISE(ABORT, 'injected after first position') END; END");
        }
        var command=new MoveChildOrder(Guid.NewGuid(),g,l,root.PlacementId,b.PlacementId,a.PlacementId,before.Revision,before.Evidence);
        await Assert.ThrowsAsync<DbUpdateException>(()=>app.MoveAsync(command));
        var after=await app.ReadAsync(g,l,root.PlacementId); Assert.Equal(before.Evidence,after.Evidence); Assert.Equal(before.Revision,after.Revision); Assert.False(await app.HasReceiptAsync(command.OperationId));
        await using var verify=db.OpenContext(); Assert.Equal(0L,await verify.Database.SqlQueryRaw<long>("SELECT N AS Value FROM OrderFailureProbe").SingleAsync());
    }
}
