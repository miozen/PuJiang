using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using Xunit;

namespace PuJiang.Tests;
public sealed class V1LineageSliceTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task Create_root_and_home_are_isolated_idempotent_and_persistent()
    {
        var db=IsolatedTestDatabase.CreateNew();var g=Guid.NewGuid();
        await using(var c=db.OpenContext()){await c.Database.EnsureCreatedAsync();c.Genealogies.Add(new(g,"虚构","冯"));await c.SaveChangesAsync();}
        var lines=new LineageApplication(new IsolatedLineageStore(db)); var roots=new FirstRootApplication(new IsolatedFirstRootStore(db));
        var command=new CreateLineage(Guid.NewGuid(),g,"朝阳","一房","虚构地区","主题","堂号","备注");
        var a=await lines.CreateAsync(command); Assert.Equal(a,await lines.CreateAsync(command));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>lines.CreateAsync(command with {Name="不同"}));
        var b=await lines.CreateAsync(command with {OperationId=Guid.NewGuid(),Name="朝明"});
        var empty=await lines.ReadHomeAsync(g,a);Assert.Equal(0,empty.Total);Assert.Empty(empty.Roots);Assert.Equal("冯",empty.Surname);Assert.Single(empty.Activities);
        var rootCommand=new AddFirstRoot(Guid.NewGuid(),g,a,RootNameMode.Named,"冯甲",8,"公","备注");
        var root=await roots.ExecuteAsync(rootCommand);Assert.True((await roots.ExecuteAsync(rootCommand)).Replayed);
        var spouse=new SpouseApplication(new IsolatedSpouseStore(db));await spouse.AddAsync(new(Guid.NewGuid(),g,a,root.PersonId,root.PlacementId,RootNameMode.Named,"李氏"));
        var children=new ChildApplication(new IsolatedChildStore(db));await children.ExecuteAsync(new(Guid.NewGuid(),g,a,root.PersonId,root.PlacementId,RootNameMode.Named,"冯子",1));
        var home=await new LineageApplication(new IsolatedLineageStore(db)).ReadHomeAsync(g,a);
        Assert.Equal(3,home.Total);Assert.Equal(2,home.Male);Assert.Equal(1,home.Female);Assert.Equal(2,home.Activities.Count);Assert.Equal(root.PersonId,Assert.Single(home.Roots).PersonId);
        var update=new UpdateLineage(Guid.NewGuid(),g,a,"朝阳更正","一房","新地区","主题","堂号","备注",LineageApplication.Evidence(home));await lines.UpdateAsync(update);await lines.UpdateAsync(update);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>lines.UpdateAsync(update with {OperationId=Guid.NewGuid()}));
        var rootUpdate=new UpdateRootDetails(Guid.NewGuid(),g,a,root.PersonId,root.PlacementId,9,"先生","更正",8,"公","备注");await roots.UpdateDetailsAsync(rootUpdate);await roots.UpdateDetailsAsync(rootUpdate);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>roots.UpdateDetailsAsync(rootUpdate with {OperationId=Guid.NewGuid()}));
        var corrected=await lines.ReadHomeAsync(g,a);Assert.Equal("朝阳更正",corrected.Name);Assert.Equal("新地区",corrected.Region);Assert.Equal(9,Assert.Single(corrected.Roots).Generation);
        Assert.Equal(0,(await lines.ReadHomeAsync(g,b)).Total); await Assert.ThrowsAsync<InvalidOperationException>(()=>lines.ReadHomeAsync(Guid.NewGuid(),a));
        await using var verify=db.OpenContext();var person=await verify.People.SingleAsync(x=>x.Id==root.PersonId);Assert.Equal(9,person.ExplicitGeneration);Assert.Equal("先生",person.Title);Assert.Equal("更正",person.Notes);
    }
    [Fact, Trait("Category", "Integration")]
    public async Task Create_failure_rolls_back_lineage_and_log_and_retry_succeeds()
    {
        var db=IsolatedTestDatabase.CreateNew();var g=Guid.NewGuid();await using(var c=db.OpenContext()){await c.Database.EnsureCreatedAsync();c.Genealogies.Add(new(g,"虚构","冯"));await c.SaveChangesAsync();await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailLog BEFORE INSERT ON LineageActivities BEGIN SELECT RAISE(ABORT, 'injected'); END");}
        var lines=new LineageApplication(new IsolatedLineageStore(db));var cmd=new CreateLineage(Guid.NewGuid(),g,"新谱","分类","地区","主题","","");
        await Assert.ThrowsAsync<DbUpdateException>(()=>lines.CreateAsync(cmd));
        await using(var c=db.OpenContext()){Assert.Empty(await c.Lineages.ToListAsync());Assert.Empty(await c.LineageActivities.ToListAsync());await c.Database.ExecuteSqlRawAsync("DROP TRIGGER FailLog");}
        var id=await lines.CreateAsync(cmd);Assert.Equal(id,await lines.CreateAsync(cmd));Assert.Single((await lines.ReadHomeAsync(g,id)).Activities);
    }
}
