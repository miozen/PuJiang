using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;

namespace PuJiang.V1.Persistence;

internal sealed class FirstRootReceipt
{
    public Guid OperationId { get; set; }
    public Guid GenealogyId { get; set; }
    public Guid LineageId { get; set; }
    public RootNameMode NameMode { get; set; }
    public string Name { get; set; } = "";
    public Guid PersonId { get; set; }
    public Guid PlacementId { get; set; }
    public int? Generation { get; set; }
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
}
internal sealed class RootDetailsReceipt { public Guid OperationId { get; set; } public string Payload { get; set; } = ""; }

// Restricted to a database capability created by IsolatedTestDatabase.
public sealed class IsolatedFirstRootStore(IsolatedTestDatabase database) : IFirstRootStore
{
    public async Task<bool> HasDetailsReceiptAsync(Guid operationId,CancellationToken ct) { await using var db=database.OpenContext(); return await db.RootDetailsReceipts.AsNoTracking().AnyAsync(x=>x.OperationId==operationId,ct); }
    public async Task UpdateDetailsAsync(UpdateRootDetails command,CancellationToken ct)
    {
        await using var db=database.OpenContext();await using var tx=await db.Database.BeginTransactionAsync(ct);var payload=System.Text.Json.JsonSerializer.Serialize(command);var old=await db.RootDetailsReceipts.SingleOrDefaultAsync(x=>x.OperationId==command.OperationId,ct);
        if(old is not null){if(old.Payload!=payload)throw new InvalidOperationException("操作身份已用于不同输入。");return;}
        if(!await db.Lineages.AnyAsync(x=>x.Id==command.LineageId&&x.GenealogyId==command.GenealogyId,ct))throw new InvalidOperationException("谱系不存在。");
        var place=await db.Placements.SingleOrDefaultAsync(x=>x.Id==command.PlacementId&&x.LineageId==command.LineageId&&x.PersonId==command.PersonId&&x.ParentPlacementId==null,ct)??throw new InvalidOperationException("首节点位置失效。");
        var person=await db.People.SingleAsync(x=>x.Id==place.PersonId&&x.LineageId==command.LineageId,ct);
        if(person.ExplicitGeneration!=command.ExpectedGeneration||person.Title!=command.ExpectedTitle||person.Notes!=command.ExpectedNotes)throw new InvalidOperationException("首节点资料已变化，请重新读取。");
        if(command.Generation!=command.ExpectedGeneration)throw new InvalidOperationException("世代请使用统一世代更正入口，以同步后代。");
        person.SetRootInformation(command.Generation,command.Title,command.Notes);db.RootDetailsReceipts.Add(new(){OperationId=command.OperationId,Payload=payload});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<RootCreated> AddFirstAsync(AddFirstRoot command, CancellationToken cancellationToken)
    {
        FirstRootApplication.Validate(command);
        await using var db = database.OpenContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var receipt = await db.FirstRootReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.GenealogyId != command.GenealogyId || receipt.LineageId != command.LineageId ||
                receipt.NameMode != command.NameMode || receipt.Name != command.Name || receipt.Generation != command.Generation || receipt.Title != command.Title || receipt.Notes != command.Notes)
                throw new InvalidOperationException("操作身份已用于不同的输入。");
            return new RootCreated(receipt.PersonId, receipt.PlacementId, true);
        }
        if (!await db.Lineages.AnyAsync(x => x.Id == command.LineageId && x.GenealogyId == command.GenealogyId, cancellationToken))
            throw new InvalidOperationException("目标谱系不存在或不属于该项目。");
        if (await db.Placements.AnyAsync(x => x.LineageId == command.LineageId, cancellationToken))
            throw new InvalidOperationException("添加首节点要求空谱系。");
        var person = new Person(Guid.NewGuid(), command.LineageId, command.Name, PersonSex.Male);
        person.SetRootInformation(command.Generation, command.Title, command.Notes);
        var placement = new Placement(Guid.NewGuid(), command.LineageId, person.Id);
        db.People.Add(person);
        db.Placements.Add(placement);
        db.FirstRootReceipts.Add(new FirstRootReceipt { OperationId = command.OperationId, GenealogyId = command.GenealogyId,
            LineageId = command.LineageId, NameMode = command.NameMode, Name = command.Name, PersonId = person.Id, PlacementId = placement.Id, Generation = command.Generation, Title = command.Title, Notes = command.Notes });
        db.LineageActivities.Add(new() { Id=Guid.NewGuid(), LineageId=command.LineageId, OperationId=command.OperationId, Kind="创建首节点", OccurredAt=DateTimeOffset.UtcNow, Detail=command.Name });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RootCreated(person.Id, placement.Id, false);
    }

    public async Task<RootProjection> ReadRootsAsync(Guid genealogyId, Guid lineageId, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Lineages.AsNoTracking().AnyAsync(x => x.Id == lineageId && x.GenealogyId == genealogyId, cancellationToken))
            throw new InvalidOperationException("目标谱系不存在或不属于该项目。");
        var nodes = await (from placement in db.Placements.AsNoTracking()
                           join person in db.People.AsNoTracking() on new { placement.LineageId, Id = placement.PersonId } equals new { person.LineageId, person.Id }
                           where placement.LineageId == lineageId && placement.ParentPlacementId == null
                           select new RootNode(person.Id, placement.Id, person.Name, person.ExplicitGeneration, person.Title, person.Notes)).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new RootProjection(genealogyId, lineageId, nodes.AsReadOnly());
    }
}
