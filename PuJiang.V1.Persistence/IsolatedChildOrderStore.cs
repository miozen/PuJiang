using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
namespace PuJiang.V1.Persistence;

internal sealed class ChildOrderVersion { public Guid LineageId {get;set;} public Guid ParentPlacementId {get;set;} public long Revision {get;set;} }
internal sealed class ChildOrderReceipt { public Guid OperationId {get;set;} public string Payload {get;set;} = ""; }
internal static class ChildOrderVersions
{
    public static async Task Touch(V1Context db, Guid lineage, Guid parent, CancellationToken ct)
    {
        var v=await db.ChildOrderVersions.SingleOrDefaultAsync(x=>x.LineageId==lineage && x.ParentPlacementId==parent,ct);
        if(v is null) db.ChildOrderVersions.Add(new(){LineageId=lineage,ParentPlacementId=parent,Revision=1});
        else v.Revision=checked(v.Revision+1);
    }
}
public sealed class IsolatedChildOrderStore(IsolatedTestDatabase database) : IChildOrderStore
{
    internal static string Evidence(IEnumerable<(Placement Place,Person Person)> items) => JsonSerializer.Serialize(items.Select(x=>new {x.Place.Id,x.Place.PersonId,x.Person.Sex,x.Place.Position}));
    internal static async Task<(long Revision,List<(Placement Place,Person Person)> Items)> Load(V1Context db, Guid g, Guid l, Guid parent, CancellationToken ct)
    {
        if(!await db.Lineages.AnyAsync(x=>x.Id==l && x.GenealogyId==g,ct)) throw new InvalidOperationException("谱系已失效。");
        var parentPlace=await db.Placements.SingleOrDefaultAsync(x=>x.Id==parent && x.LineageId==l,ct) ?? throw new InvalidOperationException("父位置已失效。");
        var rows=await (from p in db.Placements where p.LineageId==l && p.ParentPlacementId==parent join person in db.People on new {p.LineageId,Id=p.PersonId} equals new {person.LineageId,person.Id} select new {Place=p,Person=person}).ToListAsync(ct);
        var items=rows.OrderBy(x=>x.Place.Position).Select(x=>(x.Place,x.Person)).ToList();
        if(items.Any(x=>x.Person.Sex is not (PersonSex.Male or PersonSex.Female) || x.Place.Position is null) || !items.Select(x=>x.Place.Position!.Value).SequenceEqual(Enumerable.Range(1,items.Count)) || !items.Select(x=>x.Person.Sex).SequenceEqual(items.Select(x=>x.Person.Sex).Order())) throw new InvalidOperationException("子女顺序或性别异常，请先核实。");
        var personIds=items.Select(x=>x.Person.Id).ToArray();
        if(personIds.Distinct().Count()!=personIds.Length || await db.Placements.CountAsync(x=>x.LineageId==l && personIds.Contains(x.PersonId),ct)!=items.Count || await db.Placements.CountAsync(x=>x.LineageId==l && x.PersonId==parentPlace.PersonId,ct)!=1) throw new InvalidOperationException("多位置排行暂不支持。");
        var fathers=await db.BiologicalParents.Where(x=>x.LineageId==l && x.Role==ParentRole.Father && personIds.Contains(x.ChildId)).ToListAsync(ct);
        if(items.Any(x=>!fathers.Any(f=>f.ChildId==x.Person.Id && f.ParentId==parentPlace.PersonId))) throw new InvalidOperationException("父子关系与位置不一致，请先核实。");
        var version=await db.ChildOrderVersions.SingleOrDefaultAsync(x=>x.LineageId==l && x.ParentPlacementId==parent,ct);
        if(version is null && items.Count>0 || version is not null && version.Revision<=0) throw new InvalidOperationException("子女排行版本缺失或无效，请先核实。");
        return (version?.Revision ?? 0,items);
    }
    public async Task<ChildOrderSnapshot> ReadAsync(Guid g,Guid l,Guid parent,CancellationToken ct)
    { await using var db=database.OpenContext(); await using var tx=await db.Database.BeginTransactionAsync(ct); var s=await Load(db,g,l,parent,ct); await tx.CommitAsync(ct); return new(parent,s.Revision,s.Items.Select(x=>new ChildOrderItem(x.Person.Id,x.Place.Id,x.Person.Sex,x.Place.Position!.Value)).ToArray(),Evidence(s.Items)); }
    public async Task<bool> HasReceiptAsync(Guid id,CancellationToken ct) { await using var db=database.OpenContext(); return await db.ChildOrderReceipts.AsNoTracking().AnyAsync(x=>x.OperationId==id,ct); }
    public async Task MoveAsync(MoveChildOrder command,CancellationToken ct)
    {
        if(command.OperationId==Guid.Empty || command.GenealogyId==Guid.Empty || command.LineageId==Guid.Empty || command.ParentPlacementId==Guid.Empty || command.SourcePlacementId==Guid.Empty || command.TargetPlacementId==Guid.Empty || command.SourcePlacementId==command.TargetPlacementId || command.ExpectedRevision<0 || string.IsNullOrWhiteSpace(command.ExpectedEvidence)) throw new ArgumentException("排行操作无效。");
        await using var db=database.OpenContext(); await using var tx=await db.Database.BeginTransactionAsync(ct);
        var payload=JsonSerializer.Serialize(command); var old=await db.ChildOrderReceipts.SingleOrDefaultAsync(x=>x.OperationId==command.OperationId,ct);
        if(old is not null) { if(old.Payload!=payload) throw new InvalidOperationException("操作身份已用于不同输入。"); return; }
        var s=await Load(db,command.GenealogyId,command.LineageId,command.ParentPlacementId,ct);
        if(s.Revision!=command.ExpectedRevision || Evidence(s.Items)!=command.ExpectedEvidence) throw new InvalidOperationException("同胞排行已变化，请重新读取。");
        var from=s.Items.FindIndex(x=>x.Place.Id==command.SourcePlacementId); var to=s.Items.FindIndex(x=>x.Place.Id==command.TargetPlacementId);
        if(from<0 || to<0 || from==to || s.Items[from].Person.Sex!=s.Items[to].Person.Sex) throw new InvalidOperationException("只能在同一父位置、同性别组内调整排行。");
        var item=s.Items[from]; s.Items.RemoveAt(from); s.Items.Insert(to,item);
        for(int i=0;i<s.Items.Count;i++) s.Items[i].Place.SetPosition(i+1);
        await ChildOrderVersions.Touch(db,command.LineageId,command.ParentPlacementId,ct);
        db.ChildOrderReceipts.Add(new(){OperationId=command.OperationId,Payload=payload}); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
