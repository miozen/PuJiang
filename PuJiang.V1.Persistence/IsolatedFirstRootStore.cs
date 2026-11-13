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
}

// Restricted to a database capability created by IsolatedTestDatabase.
public sealed class IsolatedFirstRootStore(IsolatedTestDatabase database) : IFirstRootStore
{
    public async Task<RootCreated> AddFirstAsync(AddFirstRoot command, CancellationToken cancellationToken)
    {
        FirstRootApplication.Validate(command);
        await using var db = database.OpenContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var receipt = await db.FirstRootReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.GenealogyId != command.GenealogyId || receipt.LineageId != command.LineageId ||
                receipt.NameMode != command.NameMode || receipt.Name != command.Name)
                throw new InvalidOperationException("操作身份已用于不同的输入。");
            return new RootCreated(receipt.PersonId, receipt.PlacementId, true);
        }
        if (!await db.Lineages.AnyAsync(x => x.Id == command.LineageId && x.GenealogyId == command.GenealogyId, cancellationToken))
            throw new InvalidOperationException("目标谱系不存在或不属于该项目。");
        if (await db.People.AnyAsync(x => x.LineageId == command.LineageId, cancellationToken) ||
            await db.Placements.AnyAsync(x => x.LineageId == command.LineageId, cancellationToken))
            throw new InvalidOperationException("添加首节点要求空谱系。");
        var person = new Person(Guid.NewGuid(), command.LineageId, command.Name);
        var placement = new Placement(Guid.NewGuid(), command.LineageId, person.Id);
        db.People.Add(person);
        db.Placements.Add(placement);
        db.FirstRootReceipts.Add(new FirstRootReceipt { OperationId = command.OperationId, GenealogyId = command.GenealogyId,
            LineageId = command.LineageId, NameMode = command.NameMode, Name = command.Name, PersonId = person.Id, PlacementId = placement.Id });
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
                           where placement.LineageId == lineageId
                           select new RootNode(person.Id, placement.Id, person.Name)).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new RootProjection(genealogyId, lineageId, nodes.AsReadOnly());
    }
}
