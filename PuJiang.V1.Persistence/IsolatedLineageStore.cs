using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
namespace PuJiang.V1.Persistence;

public sealed class LineageActivity
{
    public Guid Id { get; set; } public Guid LineageId { get; set; } public Guid OperationId { get; set; }
    public string Kind { get; set; } = ""; public string Detail { get; set; } = ""; public DateTimeOffset OccurredAt { get; set; }
}
internal sealed class LineageCreateReceipt
{
    public Guid OperationId { get; set; } public Guid GenealogyId { get; set; } public Guid LineageId { get; set; }
    public string Name { get; set; } = ""; public string Category { get; set; } = ""; public string Region { get; set; } = ""; public string Topic { get; set; } = ""; public string Hall { get; set; } = ""; public string Notes { get; set; } = "";
}
internal sealed class LineageUpdateReceipt { public Guid OperationId { get; set; } public string Payload { get; set; } = ""; }
public sealed class IsolatedLineageStore(IsolatedTestDatabase database) : ILineageStore
{
    public async Task<bool> HasReceiptAsync(Guid operation, CancellationToken ct) { await using var db=database.OpenContext(); return await db.LineageCreateReceipts.AsNoTracking().AnyAsync(x=>x.OperationId==operation,ct); }
    public async Task<bool> HasUpdateReceiptAsync(Guid operation, CancellationToken ct) { await using var db=database.OpenContext(); return await db.LineageUpdateReceipts.AsNoTracking().AnyAsync(x=>x.OperationId==operation,ct); }
    public async Task UpdateAsync(UpdateLineage command, CancellationToken ct)
    {
        await using var db=database.OpenContext();await using var tx=await db.Database.BeginTransactionAsync(ct);
        var payload=System.Text.Json.JsonSerializer.Serialize(command);var old=await db.LineageUpdateReceipts.SingleOrDefaultAsync(x=>x.OperationId==command.OperationId,ct);
        if(old is not null){if(old.Payload!=payload)throw new InvalidOperationException("操作身份已用于不同输入。");return;}
        var line=await db.Lineages.SingleOrDefaultAsync(x=>x.Id==command.LineageId && x.GenealogyId==command.GenealogyId,ct) ?? throw new InvalidOperationException("谱系不存在。");
        var evidence=System.Text.Json.JsonSerializer.Serialize(new[]{line.Name,line.Category,line.Region,line.Topic,line.Hall,line.Notes});
        if(evidence!=command.ExpectedEvidence)throw new InvalidOperationException("谱系资料已变化，请重新读取。");
        line.Rename(command.Name);line.SetInformation(command.Category,command.Region,command.Topic,command.Hall,command.Notes);
        db.LineageUpdateReceipts.Add(new(){OperationId=command.OperationId,Payload=payload});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<Guid> CreateAsync(CreateLineage command, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var receipt = await db.LineageCreateReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, ct);
        if (receipt is not null)
        {
            if (receipt.GenealogyId != command.GenealogyId || receipt.Name != command.Name || receipt.Category != command.Category || receipt.Region != command.Region || receipt.Topic != command.Topic || receipt.Hall != command.Hall || receipt.Notes != command.Notes) throw new InvalidOperationException("操作身份已用于不同输入。");
            return receipt.LineageId;
        }
        var project = await db.Genealogies.SingleOrDefaultAsync(x => x.Id == command.GenealogyId, ct) ?? throw new InvalidOperationException("修谱项目不存在。");
        if (string.IsNullOrWhiteSpace(project.Surname)) throw new InvalidOperationException("项目主姓氏尚未填写，请先完善项目资料。");
        var line = new Lineage(Guid.NewGuid(), project.Id, command.Name); line.SetInformation(command.Category, command.Region, command.Topic, command.Hall, command.Notes);
        db.Lineages.Add(line); db.LineageCreateReceipts.Add(new() { OperationId=command.OperationId, GenealogyId=project.Id, LineageId=line.Id, Name=command.Name, Category=command.Category, Region=command.Region, Topic=command.Topic, Hall=command.Hall, Notes=command.Notes });
        db.LineageActivities.Add(new() { Id=Guid.NewGuid(), LineageId=line.Id, OperationId=command.OperationId, Kind="创建谱系", Detail=command.Name, OccurredAt=DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return line.Id;
    }
    public async Task<IReadOnlyList<LineageSummary>> ListAsync(Guid g, CancellationToken ct)
    {
        await using var db=database.OpenContext();
        return await db.Lineages.AsNoTracking().Where(x=>x.GenealogyId==g).OrderBy(x=>x.Name).ThenBy(x=>x.Id).Select(x=>new LineageSummary(x.Id,x.Name)).ToListAsync(ct);
    }
    public async Task<LineageHome> ReadHomeAsync(Guid g, Guid l, CancellationToken ct)
    {
        await using var db=database.OpenContext(); await using var tx=await db.Database.BeginTransactionAsync(ct);
        var line=await db.Lineages.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==l && x.GenealogyId==g,ct) ?? throw new InvalidOperationException("谱系不存在或不属于当前项目。");
        var project=await db.Genealogies.AsNoTracking().SingleAsync(x=>x.Id==g,ct);
        var people=await db.People.AsNoTracking().Where(x=>x.LineageId==l).Select(x=>x.Sex).ToListAsync(ct);
        var roots=await (from p in db.Placements.AsNoTracking() join person in db.People.AsNoTracking() on new {p.LineageId, Id=p.PersonId} equals new {person.LineageId, person.Id} where p.LineageId==l && p.ParentPlacementId==null select new RootNode(person.Id,p.Id,person.Name,person.ExplicitGeneration,person.Title,person.Notes)).ToListAsync(ct);
        var entries=await db.LineageActivities.AsNoTracking().Where(x=>x.LineageId==l).ToListAsync(ct);
        var result=new LineageHome(line.Id,line.Name,line.Category,line.Region,line.Topic,line.Hall,line.Notes,project.Surname??"",people.Count,people.Count(x=>x==PersonSex.Male),people.Count(x=>x==PersonSex.Female),people.Count(x=>x==PersonSex.Unknown),roots,entries.OrderByDescending(x=>x.OccurredAt).Select(x=>new LineageActivityView(x.Kind,x.Detail,x.OccurredAt)).ToArray());
        await tx.CommitAsync(ct); return result;
    }
}
