using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using Xunit;

namespace PuJiang.Tests;
public sealed class V1BranchAttachTests
{
    private static async Task<(IsolatedTestDatabase db, Guid g, Guid l, Guid source, Guid sourcePlace, Guid target, Guid targetPlace, Guid oldFather, Guid child)> Setup()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid(); var source = Guid.NewGuid(); var sp = Guid.NewGuid(); var target = Guid.NewGuid(); var tp = Guid.NewGuid(); var old = Guid.NewGuid();
        await using (var c = db.OpenContext())
        {
            await c.Database.EnsureCreatedAsync(); c.Genealogies.Add(new(g,"虚构","冯")); c.Lineages.Add(new(l,g,"虚构"));
            var a = new Person(source,l,"冯源",PersonSex.Male); a.SetGeneration(8); var b = new Person(target,l,"冯目标",PersonSex.Male); b.SetGeneration(3);
            c.People.AddRange(a,b,new Person(old,l,"冯原父",PersonSex.Male)); c.Placements.AddRange(new Placement(sp,l,source),new Placement(tp,l,target));
            c.BiologicalParents.Add(new(Guid.NewGuid(),l,source,old,ParentRole.Father)); await c.SaveChangesAsync();
        }
        var child = await new ChildApplication(new IsolatedChildStore(db)).ExecuteAsync(new(Guid.NewGuid(),g,l,source,sp,RootNameMode.Named,"冯后代",0,9,8));
        return (db,g,l,source,sp,target,tp,old,child.PersonId);
    }
    [Fact]
    public async Task Attach_updates_branch_and_history_atomically_and_replays()
    {
        var (db,g,l,source,sp,target,tp,old,child)=await Setup(); using var owned = db;
        var spouse = await new SpouseApplication(new IsolatedSpouseStore(db)).AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构母"));
        var app=new BranchApplication(new IsolatedBranchStore(db)); var preview=await app.PreviewAttachAsync(g,l,source,sp,tp); Assert.Equal(2,preview.PeopleCount); Assert.Equal(4,preview.NewGeneration);
        var command=new AttachBranch(Guid.NewGuid(),g,l,source,sp,tp,preview.Evidence); await app.AttachAsync(command); await app.AttachAsync(command);
        await using var c=db.OpenContext(); Assert.Equal(tp,(await c.Placements.SingleAsync(x=>x.Id==sp)).ParentPlacementId);
        Assert.Equal(4,(await c.People.SingleAsync(x=>x.Id==source)).ExplicitGeneration); Assert.Equal(5,(await c.People.SingleAsync(x=>x.Id==child)).ExplicitGeneration);
        Assert.True(await c.BiologicalParents.AnyAsync(x=>x.ChildId==source && x.ParentId==target && x.Role==ParentRole.Father));
        Assert.True(await c.BiologicalParents.AnyAsync(x=>x.ChildId==source && x.ParentId==spouse.PersonId && x.Role==ParentRole.Mother));
        Assert.False(await c.BiologicalParents.AnyAsync(x=>x.ChildId==source && x.ParentId==old)); Assert.Contains(old.ToString(),(await c.LineageActivities.SingleAsync(x=>x.OperationId==command.OperationId)).Detail,StringComparison.OrdinalIgnoreCase);
        Assert.True(await app.HasAttachReceiptAsync(command.OperationId));
    }
    [Fact]
    public async Task Failure_rolls_back_and_stale_preview_is_rejected()
    {
        var (db,g,l,source,sp,target,tp,old,child)=await Setup(); using var owned = db;
        var app=new BranchApplication(new IsolatedBranchStore(db)); var preview=await app.PreviewAttachAsync(g,l,source,sp,tp);
        await using(var c=db.OpenContext()) await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailAttach BEFORE INSERT ON LineageActivities WHEN NEW.Kind = '挂接分支' BEGIN SELECT RAISE(ABORT, 'injected'); END");
        var command=new AttachBranch(Guid.NewGuid(),g,l,source,sp,tp,preview.Evidence); await Assert.ThrowsAsync<DbUpdateException>(()=>app.AttachAsync(command));
        await using(var c=db.OpenContext()) { Assert.Null((await c.Placements.SingleAsync(x=>x.Id==sp)).ParentPlacementId); Assert.Equal(8,(await c.People.SingleAsync(x=>x.Id==source)).ExplicitGeneration); Assert.True(await c.BiologicalParents.AnyAsync(x=>x.ChildId==source && x.ParentId==old)); Assert.False(await c.LineageActivities.AnyAsync(x=>x.OperationId==command.OperationId)); }
        await using(var c=db.OpenContext()) await c.Database.ExecuteSqlRawAsync("DROP TRIGGER FailAttach");
        await new SpouseApplication(new IsolatedSpouseStore(db)).AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构母"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.AttachAsync(command)); Assert.False(await app.HasAttachReceiptAsync(command.OperationId));
    }
    [Fact]
    public async Task Rejects_target_in_own_branch()
    {
        var (db,g,l,source,sp,target,tp,old,child)=await Setup(); using var owned = db;
        await using var c=db.OpenContext(); var childPlace=await c.Placements.SingleAsync(x=>x.PersonId==child);
        var app=new BranchApplication(new IsolatedBranchStore(db)); await Assert.ThrowsAsync<InvalidOperationException>(()=>app.PreviewAttachAsync(g,l,source,sp,childPlace.Id));
    }
    [Fact]
    public async Task No_spouse_resets_old_clear_and_first_spouse_fills_mother()
    {
        var (db,g,l,source,sp,target,tp,old,child)=await Setup(); using var owned = db;
        await using(var c=db.OpenContext()) { var p=await c.People.SingleAsync(x=>x.Id==source); p.MarkMotherCleared(); await c.SaveChangesAsync(); }
        var app=new BranchApplication(new IsolatedBranchStore(db)); var preview=await app.PreviewAttachAsync(g,l,source,sp,tp);
        await app.AttachAsync(new(Guid.NewGuid(),g,l,source,sp,tp,preview.Evidence));
        var spouse=await new SpouseApplication(new IsolatedSpouseStore(db)).AddAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"虚构新母"));
        await using var verify=db.OpenContext(); Assert.True(await verify.BiologicalParents.AnyAsync(x=>x.ChildId==source && x.ParentId==spouse.PersonId && x.Role==ParentRole.Mother));
        Assert.False((await verify.People.SingleAsync(x=>x.Id==source)).MotherExplicitlyCleared);
    }
    [Fact]
    public async Task Attaching_appends_within_sex_group_and_preserves_other_children()
    {
        var (db,g,l,source,sp,target,tp,old,child)=await Setup(); using var owned = db;
        var childApp=new ChildApplication(new IsolatedChildStore(db)); var existing=await childApp.ExecuteAsync(new(Guid.NewGuid(),g,l,target,tp,RootNameMode.Named,"冯已有",0,4,3));
        var app=new BranchApplication(new IsolatedBranchStore(db)); var preview=await app.PreviewAttachAsync(g,l,source,sp,tp); await app.AttachAsync(new(Guid.NewGuid(),g,l,source,sp,tp,preview.Evidence));
        await using var c=db.OpenContext(); Assert.Equal(1,(await c.Placements.SingleAsync(x=>x.Id==existing.PlacementId)).Position); Assert.Equal(2,(await c.Placements.SingleAsync(x=>x.Id==sp)).Position);
        Assert.False(await c.People.AnyAsync(x=>x.Id==old)); Assert.Contains(old.ToString(),(await c.LineageActivities.SingleAsync(x=>x.Kind=="挂接分支")).Detail,StringComparison.OrdinalIgnoreCase);
    }
}
