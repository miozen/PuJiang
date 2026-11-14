using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;

namespace PuJiang.V1.Persistence;

internal sealed class SpouseReceipt
{
    public Guid OperationId { get; set; } public Guid GenealogyId { get; set; } public Guid LineageId { get; set; }
    public Guid FatherPersonId { get; set; } public Guid FatherPlacementId { get; set; }
    public RootNameMode NameMode { get; set; } public string Name { get; set; } = "";
    public Guid PersonId { get; set; } public Guid UnionId { get; set; }
}
internal sealed class MotherReceipt
{
    public Guid OperationId { get; set; } public Guid GenealogyId { get; set; } public Guid LineageId { get; set; }
    public Guid FatherPersonId { get; set; } public Guid FatherPlacementId { get; set; } public Guid ChildPersonId { get; set; }
    public Guid? MotherId { get; set; } public Guid? ExpectedMotherId { get; set; } public bool ExpectedCleared { get; set; }
}

public sealed class IsolatedSpouseStore(IsolatedTestDatabase database) : ISpouseStore
{
    public async Task<IReadOnlyList<MotherCandidate>> ReadMotherCandidatesAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, Guid childId, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await ReadFather(db, genealogyId, lineageId, fatherId, placementId, cancellationToken);
        if (!await db.Placements.AnyAsync(x => x.LineageId == lineageId && x.PersonId == childId && x.ParentPlacementId == placementId, cancellationToken) ||
            !await db.BiologicalParents.AnyAsync(x => x.LineageId == lineageId && x.ChildId == childId && x.ParentId == fatherId && x.Role == ParentRole.Father, cancellationToken))
            throw new InvalidOperationException("子女不属于当前父位置。");
        var candidates = await (from union in db.SpouseUnions.AsNoTracking()
                                join person in db.People.AsNoTracking() on new { union.LineageId, Id = union.SpouseId } equals new { person.LineageId, person.Id }
                                where union.LineageId == lineageId && union.FatherId == fatherId && person.Sex == PersonSex.Female && person.Id != childId
                                orderby union.Position
                                select new MotherCandidate(person.Id, person.Name)).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return candidates.AsReadOnly();
    }
    public async Task<bool> HasReceiptAsync(Guid operationId, bool motherOperation, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        return motherOperation
            ? await db.MotherReceipts.AsNoTracking().AnyAsync(x => x.OperationId == operationId, cancellationToken)
            : await db.SpouseReceipts.AsNoTracking().AnyAsync(x => x.OperationId == operationId, cancellationToken);
    }
    public async Task<IReadOnlyList<SpouseNode>> ReadAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await ReadFather(db, genealogyId, lineageId, fatherId, placementId, cancellationToken);
        var rows = await (from union in db.SpouseUnions.AsNoTracking()
                          join person in db.People.AsNoTracking() on new { union.LineageId, Id = union.SpouseId } equals new { person.LineageId, person.Id }
                          where union.LineageId == lineageId && union.FatherId == fatherId
                          orderby union.Position
                          select new { person.Id, person.Name, person.Sex, UnionId = union.Id, union.Position }).ToListAsync(cancellationToken);
        var count = await db.SpouseUnions.CountAsync(x => x.LineageId == lineageId && x.FatherId == fatherId, cancellationToken);
        if (rows.Count != count) throw new InvalidOperationException("配偶身份资料缺失，请先核实。");
        if (!rows.Select(x => x.Position).SequenceEqual(Enumerable.Range(1, rows.Count))) throw new InvalidOperationException("配偶排序缺失或损坏，请先核实并重新确认排序。");
        await tx.CommitAsync(cancellationToken);
        return rows.Select(x => new SpouseNode(x.Id, x.UnionId, x.Name, x.Position)).ToArray();
    }
    private static async Task<(Person Person, Placement Placement)> ReadFather(V1Context db, Guid g, Guid l, Guid p, Guid place, CancellationToken ct)
    {
        if (!await db.Lineages.AnyAsync(x => x.Id == l && x.GenealogyId == g, ct)) throw new InvalidOperationException("目标谱系不存在。");
        var father = await db.People.SingleOrDefaultAsync(x => x.Id == p && x.LineageId == l, ct) ?? throw new InvalidOperationException("父亲不存在。");
        var location = await db.Placements.SingleOrDefaultAsync(x => x.Id == place && x.LineageId == l, ct) ?? throw new InvalidOperationException("父位置不存在。");
        OrdinaryChildRules.ValidateFather(father, location, l);
        if (await db.Placements.CountAsync(x => x.LineageId == l && x.PersonId == p, ct) != 1) throw new InvalidOperationException("多位置承接尚未接入，请先核实。");
        return (father, location);
    }
    public async Task<SpouseCreated> AddAsync(AddSpouse command, CancellationToken cancellationToken)
    {
        SpouseApplication.Validate(command); await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var old = await db.SpouseReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);
        if (old is not null)
        {
            if (old.GenealogyId != command.GenealogyId || old.LineageId != command.LineageId || old.FatherPersonId != command.FatherPersonId || old.FatherPlacementId != command.FatherPlacementId || old.NameMode != command.NameMode || old.Name != command.Name) throw new InvalidOperationException("操作身份已用于不同输入。");
            return new(old.PersonId, old.UnionId, true);
        }
        var (father, location) = await ReadFather(db, command.GenealogyId, command.LineageId, command.FatherPersonId, command.FatherPlacementId, cancellationToken);
        var unions = await db.SpouseUnions.Where(x => x.LineageId == command.LineageId && x.FatherId == father.Id).OrderBy(x => x.Position).ToListAsync(cancellationToken);
        if (!unions.Select(x => x.Position).SequenceEqual(Enumerable.Range(1, unions.Count))) throw new InvalidOperationException("配偶排序缺失或损坏，请先核实并重新确认排序。");
        var spouse = new Person(Guid.NewGuid(), command.LineageId, command.Name, PersonSex.Female);
        var union = new SpouseUnion(Guid.NewGuid(), command.LineageId, father.Id, spouse.Id, unions.Count + 1);
        db.People.Add(spouse); db.SpouseUnions.Add(union);
        if (unions.Count == 0)
        {
            var children = await (from place in db.Placements where place.LineageId == command.LineageId && place.ParentPlacementId == location.Id
                                  join child in db.People on new { place.LineageId, Id = place.PersonId } equals new { child.LineageId, child.Id }
                                  select child).ToListAsync(cancellationToken);
            foreach (var child in children)
            {
                if (!await db.BiologicalParents.AnyAsync(r => r.LineageId == command.LineageId && r.ChildId == child.Id && r.ParentId == father.Id && r.Role == ParentRole.Father, cancellationToken)) throw new InvalidOperationException("子位置与父亲关系不一致，请先核实。");
                var hasMother = await db.BiologicalParents.AnyAsync(r => r.LineageId == command.LineageId && r.ChildId == child.Id && r.Role == ParentRole.Mother, cancellationToken);
                if (child.MotherExplicitlyCleared && hasMother) throw new InvalidOperationException("生母清空状态与关系不一致，请先核实。");
                if (!child.MotherExplicitlyCleared && !hasMother)
                    db.BiologicalParents.Add(new BiologicalParent(Guid.NewGuid(), command.LineageId, child.Id, spouse.Id, ParentRole.Mother));
            }
        }
        db.SpouseReceipts.Add(new SpouseReceipt { OperationId=command.OperationId, GenealogyId=command.GenealogyId, LineageId=command.LineageId,
            FatherPersonId=command.FatherPersonId, FatherPlacementId=command.FatherPlacementId, NameMode=command.NameMode, Name=command.Name, PersonId=spouse.Id, UnionId=union.Id });
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return new(spouse.Id, union.Id, false);
    }
    public async Task SetMotherAsync(SetChildMother command, CancellationToken cancellationToken)
    {
        SpouseApplication.Validate(command); await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var old = await db.MotherReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);
        if (old is not null)
        {
            if (old.GenealogyId != command.GenealogyId || old.LineageId != command.LineageId || old.FatherPersonId != command.FatherPersonId || old.FatherPlacementId != command.FatherPlacementId || old.ChildPersonId != command.ChildPersonId || old.MotherId != command.MotherId || old.ExpectedMotherId != command.ExpectedMotherId || old.ExpectedCleared != command.ExpectedCleared) throw new InvalidOperationException("操作身份已用于不同输入。");
            return;
        }
        var (father, location) = await ReadFather(db, command.GenealogyId, command.LineageId, command.FatherPersonId, command.FatherPlacementId, cancellationToken);
        var child = await db.People.SingleOrDefaultAsync(x => x.Id == command.ChildPersonId && x.LineageId == command.LineageId, cancellationToken) ?? throw new InvalidOperationException("子女不存在。");
        if (!await db.Placements.AnyAsync(x => x.LineageId == command.LineageId && x.PersonId == child.Id && x.ParentPlacementId == location.Id, cancellationToken) ||
            !await db.BiologicalParents.AnyAsync(x => x.LineageId == command.LineageId && x.ChildId == child.Id && x.ParentId == father.Id && x.Role == ParentRole.Father, cancellationToken)) throw new InvalidOperationException("子女不属于当前父位置。");
        var link = await db.BiologicalParents.SingleOrDefaultAsync(x => x.LineageId == command.LineageId && x.ChildId == child.Id && x.Role == ParentRole.Mother, cancellationToken);
        if (link?.ParentId != command.ExpectedMotherId || child.MotherExplicitlyCleared != command.ExpectedCleared) throw new InvalidOperationException("子女生母资料已变化，请重新读取。");
        if (command.MotherId is Guid motherId)
        {
            if (motherId == child.Id || motherId == father.Id) throw new InvalidOperationException("生母与子女或生父不能是同一人物。");
            if (!await db.SpouseUnions.AnyAsync(x => x.LineageId == command.LineageId && x.FatherId == father.Id && x.SpouseId == motherId, cancellationToken))
                throw new InvalidOperationException("所选生母不是当前父亲的配偶，请重新选择。");
            var mother = await db.People.SingleOrDefaultAsync(x => x.Id == motherId && x.LineageId == command.LineageId, cancellationToken) ?? throw new InvalidOperationException("所选生母不属于当前谱系。");
            if (mother.Sex != PersonSex.Female) throw new InvalidOperationException("所选人物与生母角色不一致，请先核实资料。");
            var relations = await db.BiologicalParents.AsNoTracking().Where(x => x.LineageId == command.LineageId).Select(x => new { x.ParentId, x.ChildId }).ToListAsync(cancellationToken);
            var descendants = new HashSet<Guid> { child.Id }; var pending = new Queue<Guid>(); pending.Enqueue(child.Id);
            while (pending.TryDequeue(out var ancestor))
                foreach (var descendant in relations.Where(x => x.ParentId == ancestor).Select(x => x.ChildId))
                    if (descendants.Add(descendant)) pending.Enqueue(descendant);
            if (descendants.Contains(motherId)) throw new InvalidOperationException("所选生母会使亲属关系形成循环。");
            if (link is null) db.BiologicalParents.Add(new BiologicalParent(Guid.NewGuid(), command.LineageId, child.Id, motherId, ParentRole.Mother));
            else if (link.ParentId != motherId) link.ChangeParent(motherId);
            child.MarkMotherKnown();
        }
        else { if (link is not null) db.BiologicalParents.Remove(link); child.MarkMotherCleared(); }
        db.MotherReceipts.Add(new MotherReceipt { OperationId=command.OperationId, GenealogyId=command.GenealogyId, LineageId=command.LineageId,
            FatherPersonId=command.FatherPersonId, FatherPlacementId=command.FatherPlacementId, ChildPersonId=command.ChildPersonId, MotherId=command.MotherId, ExpectedMotherId=command.ExpectedMotherId, ExpectedCleared=command.ExpectedCleared });
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
    }
}
