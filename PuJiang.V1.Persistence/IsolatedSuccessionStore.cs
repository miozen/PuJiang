using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
namespace PuJiang.V1.Persistence;

public sealed class IsolatedSuccessionStore(IsolatedTestDatabase database) : ISuccessionStore
{
    private sealed record State(List<Person> People, List<Placement> Places, List<BiologicalParent> Parents, HashSet<Guid> Branch, Person Child, Placement Birth, Placement Source, Succession? Detached, Person HeirFather, Placement HeirFatherPlace, Person? Mother, string Evidence);
    private static async Task<State> Load(V1Context db, Guid g, Guid l, Guid childId, Guid birthId, Guid targetId, CancellationToken ct)
    {
        if (!await db.Lineages.AnyAsync(x=>x.Id==l && x.GenealogyId==g,ct)) throw new InvalidOperationException("谱系已失效。");
        var people=await db.People.AsNoTracking().Where(x=>x.LineageId==l).ToListAsync(ct);
        var places=await db.Placements.AsNoTracking().Where(x=>x.LineageId==l).ToListAsync(ct);
        var parents=await db.BiologicalParents.AsNoTracking().Where(x=>x.LineageId==l).ToListAsync(ct);
        var existing=await db.Successions.AsNoTracking().Where(x=>x.LineageId==l).ToListAsync(ct);
        var source=places.SingleOrDefault(x=>x.Id==birthId && x.PersonId==childId) ?? throw new InvalidOperationException("来源位置无效，请先核实。");
        var detached=existing.SingleOrDefault(x=>x.ChildId==childId && x.HeirPlacementId==source.Id && source.ParentPlacementId==null);
        var birth=detached is null ? source : places.Single(x=>x.Id==detached.BirthPlacementId && x.PersonId==childId);
        if(birth.ParentPlacementId is null) throw new InvalidOperationException("来源出生位置无效，请先核实。");
        var target=places.SingleOrDefault(x=>x.Id==targetId) ?? throw new InvalidOperationException("嗣父位置已失效。");
        var child=people.Single(x=>x.Id==childId); var father=people.Single(x=>x.Id==target.PersonId);
        if (child.Sex is not (PersonSex.Male or PersonSex.Female) || father.Sex!=PersonSex.Male || child.ExplicitGeneration is null or <=0 || father.ExplicitGeneration is null or <=0) throw new InvalidOperationException("人物、嗣父角色或世代无效，请先核实。");
        if (places.Count(x=>x.PersonId==childId)!=(detached is null?1:2) || places.Count(x=>x.PersonId==father.Id)!=1 || existing.Any(x=>(x.ChildId==childId && x.Id!=detached?.Id) || x.ChildId==father.Id)) throw new InvalidOperationException("已有多位置或嗣继的情况不在本批，请先核实。");
        var birthParent=places.Single(x=>x.Id==birth.ParentPlacementId);
        if (target.Id==birthParent.Id) throw new InvalidOperationException("该人物已在目标父位置下，请选择其他嗣父位置。");
        if (existing.Any(x=>x.ChildId==birthParent.PersonId)) throw new InvalidOperationException("来源父位置涉及其他嗣继，暂不属于单一承嗣范围。");
        if (people.Single(x=>x.Id==birthParent.PersonId).Sex != PersonSex.Male) throw new InvalidOperationException("原生父角色资料不一致，请先核实。");
        if(!parents.Any(x=>x.ChildId==childId && x.ParentId==birthParent.PersonId && x.Role==ParentRole.Father)) throw new InvalidOperationException("出生位置与生父关系不一致。");
        if(child.MotherExplicitlyCleared && parents.Any(x=>x.ChildId==childId && x.Role==ParentRole.Mother)) throw new InvalidOperationException("生母清空状态与原关系不一致，请先核实。");
        var branch=new HashSet<Guid>{source.Id};var q=new Queue<Guid>();q.Enqueue(source.Id);
        while(q.TryDequeue(out var p)) foreach(var c in places.Where(x=>x.ParentPlacementId==p)) { if(!branch.Add(c.Id)) throw new InvalidOperationException("位置存在循环。");q.Enqueue(c.Id); }
        if(branch.Contains(targetId)) throw new InvalidOperationException("不能挂接到本人或后代。");
        var branchPeople=places.Where(x=>branch.Contains(x.Id)).Select(x=>x.PersonId).ToArray();
        if(branchPeople.Distinct().Count()!=branchPeople.Length || places.Any(x=>branchPeople.Contains(x.PersonId) && !branch.Contains(x.Id) && (detached is null || x.Id!=birth.Id)) || existing.Any(x=>branchPeople.Contains(x.ChildId) && x.Id!=detached?.Id)) throw new InvalidOperationException("分支涉及多位置或其他嗣继，本批暂不支持。");
        foreach(var p in places.Where(x=>branch.Contains(x.Id)))
        {
            var member=people.Single(x=>x.Id==p.PersonId);
            if(member.ExplicitGeneration is null or <=0) throw new InvalidOperationException("分支世代缺失，请先核实。");
            if(p.Id!=source.Id && p.ParentPlacementId is Guid parentId)
            {
                var parent=people.Single(x=>x.Id==places.Single(q=>q.Id==parentId).PersonId);
                if(parent.ExplicitGeneration is null or <=0 || member.ExplicitGeneration<=parent.ExplicitGeneration) throw new InvalidOperationException("分支世代异常，请先核实。");
            }
            if(parents.Any(x=>x.ParentId==p.PersonId && x.Role==ParentRole.Father && !places.Any(c=>c.PersonId==x.ChildId && c.ParentPlacementId==p.Id))) throw new InvalidOperationException("分支有范围外后代关系，请先核实。");
        }
        var targetGroup=await IsolatedChildOrderStore.Load(db,g,l,targetId,ct);
        var birthGroup=await IsolatedChildOrderStore.Load(db,g,l,birth.ParentPlacementId!.Value,ct);
        foreach(var parent in places.Where(x=>branch.Contains(x.Id)).Select(x=>x.Id))
            if(places.Any(x=>x.ParentPlacementId==parent)) await IsolatedChildOrderStore.Load(db,g,l,parent,ct);
        var (spouseGroup, spouses)=await SpouseOrderQueries.Read(db,l,father.Id,ct);
        var mother=spouses.Count==0 ? null : people.SingleOrDefault(x=>x.Id==spouses[0].Union.SpouseId);
        if(spouses.Count>0 && (mother is null || mother.Sex!=PersonSex.Female || mother.Id==childId)) throw new InvalidOperationException("首位配偶与嗣母角色不一致，请先核实。");
        if (detached is not null && detached.HeirFatherId == father.Id && detached.MotherExplicitlyCleared) mother = null;
        var versions=await db.ChildOrderVersions.AsNoTracking().Where(x=>x.LineageId==l).OrderBy(x=>x.ParentPlacementId).Select(x=>new{x.ParentPlacementId,x.Revision}).ToListAsync(ct);
        var revisions=await db.PersonProfileReceipts.AsNoTracking().Where(x=>x.LineageId==l).Select(x=>x.OperationId).ToListAsync(ct);
        var momRevisions=await db.MotherReceipts.AsNoTracking().Where(x=>x.LineageId==l).Select(x=>x.OperationId).ToListAsync(ct);
        var gener=await db.GenerationReceipts.AsNoTracking().Where(x=>x.Payload.Contains(l.ToString())).Select(x=>x.OperationId).ToListAsync(ct);
        var evidence=JsonSerializer.Serialize(new { People=people.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Name,x.Sex,x.ExplicitGeneration,x.MotherExplicitlyCleared}).ToArray(),Places=places.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.PersonId,x.ParentPlacementId,x.Position}).ToArray(),Parents=parents.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ChildId,x.ParentId,x.Role}).ToArray(),Existing=existing.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ChildId,x.BirthPlacementId,x.HeirPlacementId,x.HeirFatherId,x.HeirFatherPlacementId,x.HeirMotherId,x.MotherExplicitlyCleared}).ToArray(),Versions=versions,Profiles=revisions.Order().ToArray(),Mothers=momRevisions.Order().ToArray(),Generations=gener.Order().ToArray(),TargetOrder=IsolatedChildOrderStore.Evidence(targetGroup.Items),TargetRevision=targetGroup.Revision,BirthOrder=IsolatedChildOrderStore.Evidence(birthGroup.Items),BirthRevision=birthGroup.Revision,SpouseRevision=spouseGroup?.Revision,Spouses=spouses.Select(x=>new{x.Union.Id,x.Union.SpouseId,x.Position}).ToArray() });
        return new(people,places,parents,branch,child,birth,source,detached,father,target,mother,evidence);
    }
    public async Task<SuccessionPreview> PreviewAsync(Guid g,Guid l,Guid child,Guid birth,Guid target,CancellationToken ct)
    {
        await using var db=database.OpenContext();await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await Load(db,g,l,child,birth,target,ct);
        var parent=s.Places.Single(x=>x.Id==s.Birth.ParentPlacementId);var result=new SuccessionPreview(s.Child.Name,s.People.Single(x=>x.Id==parent.PersonId).Name,s.HeirFather.Name,s.Mother?.Name,s.Branch.Count-1,s.Evidence);await tx.CommitAsync(ct);return result;
    }
    public async Task<bool> HasReceiptAsync(Guid id,CancellationToken ct) {await using var db=database.OpenContext();return await db.ChildOrderReceipts.AsNoTracking().AnyAsync(x=>x.OperationId==id && x.Payload.StartsWith("succession:"),ct);}
    private static async Task<string> MotherEvidence(V1Context db, Succession relation, CancellationToken ct)
    {
        var (group, order)=await SpouseOrderQueries.Read(db,relation.LineageId,relation.HeirFatherId,ct);
        var spouseIds=order.Select(x=>x.Union.SpouseId).ToArray();
        var people=await db.People.AsNoTracking().Where(x=>x.LineageId==relation.LineageId && spouseIds.Contains(x.Id)).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Name,x.Sex}).ToListAsync(ct);
        var versions=await db.PersonProfileReceipts.AsNoTracking().Where(x=>x.LineageId==relation.LineageId && spouseIds.Contains(x.PersonId)).Select(x=>x.OperationId).ToListAsync(ct);
        var relationText=relation.Id.ToString();
        var edits=await db.ChildOrderReceipts.AsNoTracking().Where(x=>x.Payload.StartsWith("heir-mother:") && x.Payload.Contains(relationText)).Select(x=>x.OperationId).ToListAsync(ct);
        return JsonSerializer.Serialize(new{relation.Id,relation.HeirMotherId,relation.MotherExplicitlyCleared,Revision=group?.Revision,Order=order.Select(x=>new{x.Union.Id,x.Union.SpouseId,x.Position}).ToArray(),People=people,Profiles=versions.Order().ToArray(),Edits=edits.Order().ToArray()});
    }
    public async Task<SuccessionCreated> CreateAsync(CreateSuccession c,CancellationToken ct)
    {
        await using var db=database.OpenContext();await using var tx=await db.Database.BeginTransactionAsync(ct);var payload="succession:"+JsonSerializer.Serialize(c);
        var receipt=await db.ChildOrderReceipts.SingleOrDefaultAsync(x=>x.OperationId==c.OperationId,ct);
        if(receipt is not null){if(receipt.Payload!=payload)throw new InvalidOperationException("操作身份已用于不同输入。");var prior=await db.Successions.SingleAsync(x=>x.LineageId==c.LineageId && x.ChildId==c.ChildId,ct);return new(prior.Id,prior.HeirPlacementId,true);}
        var s=await Load(db,c.GenealogyId,c.LineageId,c.ChildId,c.BirthPlacementId,c.HeirFatherPlacementId,ct);
        if(s.Evidence!=c.ExpectedEvidence)throw new InvalidOperationException("承嗣范围已变化，请重新确认。");
        var order=await IsolatedChildOrderStore.Load(db,c.GenealogyId,c.LineageId,c.HeirFatherPlacementId,ct);
        var insert=order.Items.Count(x=>x.Person.Sex<=s.Child.Sex);for(var i=insert;i<order.Items.Count;i++)order.Items[i].Place.SetPosition(i+2);
        var heirId=s.Detached?.HeirPlacementId ?? Guid.NewGuid();var relationId=s.Detached?.Id ?? Guid.NewGuid();
        if(s.Detached is null) db.Placements.Add(new Placement(heirId,c.LineageId,c.ChildId,c.HeirFatherPlacementId,insert+1));
        else { var place=await db.Placements.SingleAsync(x=>x.Id==heirId && x.LineageId==c.LineageId,ct); place.Attach(c.HeirFatherPlacementId,insert+1); var relation=await db.Successions.SingleAsync(x=>x.Id==relationId && x.LineageId==c.LineageId,ct); relation.ChangeHeirParents(s.HeirFather.Id,c.HeirFatherPlacementId,s.Mother?.Id); }
        await db.SaveChangesAsync(ct);
        var children=await db.Placements.Where(x=>x.LineageId==c.LineageId && x.ParentPlacementId==c.BirthPlacementId).ToListAsync(ct);
        foreach(var child in children)child.TransferParent(heirId);
        if(s.Detached is null) db.Successions.Add(new Succession(relationId,c.LineageId,c.ChildId,c.BirthPlacementId,heirId,s.HeirFather.Id,c.HeirFatherPlacementId,s.Mother?.Id));
        await ChildOrderVersions.Touch(db,c.LineageId,c.HeirFatherPlacementId,ct);
        if(children.Count>0)await ChildOrderVersions.Touch(db,c.LineageId,heirId,ct);
        db.ChildOrderReceipts.Add(new(){OperationId=c.OperationId,Payload=payload});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(relationId,heirId,false);
    }
    public async Task<SuccessionInfo?> ReadAsync(Guid g,Guid l,Guid place,CancellationToken ct)
    {
        await using var db=database.OpenContext();await using var tx=await db.Database.BeginTransactionAsync(ct);
        if(!await db.Lineages.AnyAsync(x=>x.Id==l && x.GenealogyId==g,ct))throw new InvalidOperationException("谱系不存在。");
        var relation=await db.Successions.AsNoTracking().SingleOrDefaultAsync(x=>x.LineageId==l && (x.BirthPlacementId==place || x.HeirPlacementId==place),ct);if(relation is null)return null;
        var (_,order)=await SpouseOrderQueries.Read(db,l,relation.HeirFatherId,ct);var people=await db.People.AsNoTracking().Where(x=>x.LineageId==l).ToDictionaryAsync(x=>x.Id,ct);
        if(order.Any(x=>!people.ContainsKey(x.Union.SpouseId)))throw new InvalidOperationException("配偶身份缺失，请先核实。");
        var candidates=order.Select(x=>people[x.Union.SpouseId]).Where(x=>x.Sex==PersonSex.Female && x.Id!=relation.ChildId).Select(x=>new MotherCandidate(x.Id,x.Name)).ToArray();
        var biological=await db.BiologicalParents.AsNoTracking().Where(x=>x.LineageId==l && x.ChildId==relation.ChildId).ToListAsync(ct);
        string? ParentName(ParentRole role) { var link=biological.SingleOrDefault(x=>x.Role==role); return link is not null && people.TryGetValue(link.ParentId,out var p)?p.Name:null; }
        var evidence=await MotherEvidence(db,relation,ct);
        var result=new SuccessionInfo(relation.Id,relation.ChildId,relation.BirthPlacementId,relation.HeirPlacementId,relation.HeirFatherId,relation.HeirFatherPlacementId,ParentName(ParentRole.Father),ParentName(ParentRole.Mother),relation.HeirMotherId,relation.HeirMotherId is Guid m && people.TryGetValue(m,out var mother)?mother.Name:null,relation.MotherExplicitlyCleared,candidates,evidence);
        await tx.CommitAsync(ct);return result;
    }
    public async Task<bool> HasMotherReceiptAsync(Guid id,CancellationToken ct){await using var db=database.OpenContext();return await db.ChildOrderReceipts.AsNoTracking().AnyAsync(x=>x.OperationId==id && x.Payload.StartsWith("heir-mother:"),ct);}
    public async Task SetMotherAsync(SetHeirMother c,CancellationToken ct)
    {
        await using var db=database.OpenContext();await using var tx=await db.Database.BeginTransactionAsync(ct);var payload="heir-mother:"+JsonSerializer.Serialize(c);var receipt=await db.ChildOrderReceipts.SingleOrDefaultAsync(x=>x.OperationId==c.OperationId,ct);
        if(receipt is not null){if(receipt.Payload!=payload)throw new InvalidOperationException("操作身份已用于不同输入。");return;}
        if(!await db.Lineages.AnyAsync(x=>x.Id==c.LineageId && x.GenealogyId==c.GenealogyId,ct))throw new InvalidOperationException("谱系不存在。");
        var relation=await db.Successions.SingleOrDefaultAsync(x=>x.Id==c.SuccessionId && x.LineageId==c.LineageId,ct)??throw new InvalidOperationException("嗣继关系不存在。");
        if(relation.HeirMotherId!=c.ExpectedMotherId || relation.MotherExplicitlyCleared!=c.ExpectedCleared || await MotherEvidence(db,relation,ct)!=c.ExpectedEvidence)throw new InvalidOperationException("嗣母或配偶资料已变化，请重新读取。");
        if(c.MotherId is Guid id){var (_,order)=await SpouseOrderQueries.Read(db,c.LineageId,relation.HeirFatherId,ct);if(!order.Any(x=>x.Union.SpouseId==id) || !await db.People.AnyAsync(x=>x.Id==id && x.LineageId==c.LineageId && x.Sex==PersonSex.Female,ct))throw new InvalidOperationException("嗣母不在嗣父有效配偶范围。");}
        relation.SetMother(c.MotherId,c.MotherId is null);db.ChildOrderReceipts.Add(new(){OperationId=c.OperationId,Payload=payload});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
