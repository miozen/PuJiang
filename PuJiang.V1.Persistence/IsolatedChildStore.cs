using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;

namespace PuJiang.V1.Persistence;

internal sealed class ChildReceipt
{
    public Guid OperationId { get; set; }
    public Guid GenealogyId { get; set; }
    public Guid LineageId { get; set; }
    public Guid ParentPersonId { get; set; }
    public Guid ParentPlacementId { get; set; }
    public RootNameMode NameMode { get; set; }
    public string Name { get; set; } = "";
    public Guid PersonId { get; set; }
    public Guid PlacementId { get; set; }
}

// Isolated ordinary-child path; unsupported multiple-position cases fail closed.
public sealed class IsolatedChildStore(IsolatedTestDatabase database) : IChildStore
{
    public async Task<bool> HasReceiptAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        return await db.ChildReceipts.AsNoTracking().AnyAsync(x => x.OperationId == operationId, cancellationToken);
    }
    public async Task<ChildCreated> AddAsync(AddChild command, CancellationToken cancellationToken)
    {
        ChildApplication.Validate(command);
        await using var db = database.OpenContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var receipt = await db.ChildReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.GenealogyId != command.GenealogyId || receipt.LineageId != command.LineageId || receipt.ParentPersonId != command.ParentPersonId ||
                receipt.ParentPlacementId != command.ParentPlacementId || receipt.NameMode != command.NameMode || receipt.Name != command.Name)
                throw new InvalidOperationException("操作身份已用于不同的输入。");
            return new(receipt.PersonId, receipt.PlacementId, true);
        }
        if (!await db.Lineages.AnyAsync(x => x.Id == command.LineageId && x.GenealogyId == command.GenealogyId, cancellationToken))
            throw new InvalidOperationException("目标谱系不存在或不属于该项目。");
        var parent = await db.People.SingleOrDefaultAsync(x => x.Id == command.ParentPersonId && x.LineageId == command.LineageId, cancellationToken)
            ?? throw new InvalidOperationException("父人物不存在或不属于当前谱系。");
        var placement = await db.Placements.SingleOrDefaultAsync(x => x.Id == command.ParentPlacementId && x.LineageId == command.LineageId, cancellationToken)
            ?? throw new InvalidOperationException("当前父位置不存在或不属于当前谱系。");
        OrdinaryChildRules.ValidateFather(parent, placement, command.LineageId);
        if (await db.Placements.CountAsync(x => x.LineageId == command.LineageId && x.PersonId == parent.Id, cancellationToken) != 1)
            throw new InvalidOperationException("多位置后代承接尚未接入，请先核实当前父位置。");
        var spouses = await db.SpouseUnions.Where(x => x.LineageId == command.LineageId && x.FatherId == parent.Id)
            .OrderBy(x => x.Position).ToListAsync(cancellationToken);
        if (!spouses.Select(x => x.Position).SequenceEqual(Enumerable.Range(1, spouses.Count)))
            throw new InvalidOperationException("配偶排序缺失或损坏，请先核实并重新确认排序。");
        Person? mother = null;
        if (spouses.Count > 0)
        {
            mother = await db.People.SingleOrDefaultAsync(x => x.Id == spouses[0].SpouseId && x.LineageId == command.LineageId, cancellationToken)
                ?? throw new InvalidOperationException("首位配偶资料不存在，请先核实。");
            if (mother.Sex != PersonSex.Female) throw new InvalidOperationException("首位配偶与生母角色不一致，请先核实资料。");
        }
        var siblings = await (from p in db.Placements where p.LineageId == command.LineageId && p.ParentPlacementId == placement.Id
            join person in db.People on new { p.LineageId, Id = p.PersonId } equals new { person.LineageId, person.Id }
            select new { person.Sex, Placement = p }).ToListAsync(cancellationToken);
        var ordered = siblings.OrderBy(x => x.Placement.Position).ToArray();
        if (ordered.Any(x => x.Sex == PersonSex.Unknown || x.Placement.Position is null) ||
            !ordered.Select(x => x.Placement.Position!.Value).SequenceEqual(Enumerable.Range(1, siblings.Count)) ||
            !ordered.Select(x => x.Sex).SequenceEqual(ordered.Select(x => x.Sex).Order()))
            throw new InvalidOperationException("子女排序或性别尚不能安全确认，请先核实。");
        var position = ordered.Count(x => x.Sex == PersonSex.Male) + 1;
        foreach (var sibling in ordered.Where(x => x.Sex == PersonSex.Female)) sibling.Placement.SetPosition(sibling.Placement.Position!.Value + 1);
        var child = new Person(Guid.NewGuid(), command.LineageId, command.Name, PersonSex.Male);
        var childPlacement = new Placement(Guid.NewGuid(), command.LineageId, child.Id, placement.Id, position);
        db.People.Add(child); db.Placements.Add(childPlacement);
        db.BiologicalParents.Add(new BiologicalParent(Guid.NewGuid(), command.LineageId, child.Id, parent.Id, ParentRole.Father));
        if (mother is not null) db.BiologicalParents.Add(new BiologicalParent(Guid.NewGuid(), command.LineageId, child.Id, mother.Id, ParentRole.Mother));
        db.ChildReceipts.Add(new ChildReceipt { OperationId=command.OperationId, GenealogyId=command.GenealogyId, LineageId=command.LineageId,
            ParentPersonId=command.ParentPersonId, ParentPlacementId=command.ParentPlacementId, NameMode=command.NameMode, Name=command.Name,
            PersonId=child.Id, PlacementId=childPlacement.Id });
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        return new(child.Id, childPlacement.Id, false);
    }
    public async Task<IReadOnlyList<ChildNode>> ReadAsync(Guid genealogyId, Guid lineageId, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Lineages.AsNoTracking().AnyAsync(x => x.Id == lineageId && x.GenealogyId == genealogyId, cancellationToken)) throw new InvalidOperationException("目标谱系不存在。");
        var placements = await (from p in db.Placements.AsNoTracking()
                           join person in db.People.AsNoTracking() on new { p.LineageId, Id = p.PersonId } equals new { person.LineageId, person.Id }
                           where p.LineageId == lineageId && p.ParentPlacementId != null
                           select new { person.Id, PlacementId = p.Id, ParentPlacementId = p.ParentPlacementId!.Value, person.Name, Position = p.Position!.Value, person.MotherExplicitlyCleared, person.Sex }).ToListAsync(cancellationToken);
        var allPlacements = await db.Placements.AsNoTracking().Where(x => x.LineageId == lineageId).ToListAsync(cancellationToken);
        var relations = await db.BiologicalParents.AsNoTracking().Where(x => x.LineageId == lineageId).ToListAsync(cancellationToken);
        var nodes = new List<ChildNode>();
        foreach (var p in placements)
        {
            var parent = allPlacements.SingleOrDefault(x => x.Id == p.ParentPlacementId) ?? throw new InvalidOperationException("父位置不存在，无法读取子女。");
            var father = relations.SingleOrDefault(x => x.ChildId == p.Id && x.ParentId == parent.PersonId && x.Role == ParentRole.Father) ?? throw new InvalidOperationException("父子关系与位置不一致，无法读取子女。");
            var mother = relations.SingleOrDefault(x => x.ChildId == p.Id && x.Role == ParentRole.Mother);
            if (p.MotherExplicitlyCleared && mother is not null) throw new InvalidOperationException("生母清空状态与关系不一致，请先核实。");
            nodes.Add(new(p.Id, p.PlacementId, p.ParentPlacementId, p.Name, p.Position, father.Id, mother?.ParentId, p.MotherExplicitlyCleared, p.Sex));
        }
        await tx.CommitAsync(cancellationToken); return nodes.AsReadOnly();
    }
}
