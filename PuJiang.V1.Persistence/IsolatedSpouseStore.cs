using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using System.Text.Json;

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
    private static async Task<RepairSnapshot> RepairSnapshot(V1Context db, Guid lineage, Guid father, CancellationToken ct)
    {
        var group = await db.SpouseOrderGroups.AsNoTracking().SingleOrDefaultAsync(x => x.LineageId == lineage && x.FatherId == father, ct);
        var entries = await db.SpouseOrderEntries.AsNoTracking().Where(x => x.LineageId == lineage && x.FatherId == father).OrderBy(x => x.UnionId).ToListAsync(ct);
        var unions = await db.SpouseUnions.AsNoTracking().Where(x => x.LineageId == lineage && x.FatherId == father).OrderBy(x => x.Id).ToListAsync(ct);
        var people = await db.People.AsNoTracking().Where(x => x.LineageId == lineage).ToDictionaryAsync(x => x.Id, ct);
        if (unions.Any(x => !people.ContainsKey(x.SpouseId))) throw new InvalidOperationException("配偶人物资料缺失，无法安全修复排序。");
        var candidates = unions.Select(x => new RepairCandidate(x.Id, x.SpouseId, people[x.SpouseId].Name)).ToArray();
        // This canonical snapshot is retained in full; its order is evidence only, never the proposed display order.
        var evidence = JsonSerializer.Serialize(new { Revision = group?.Revision, Entries = entries.Select(x => new { x.UnionId, x.Position }).ToArray(), Unions = candidates });
        return new(evidence, candidates);
    }
    public async Task<RepairSnapshot> ReadRepairAsync(Guid g, Guid l, Guid p, Guid place, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ReadFather(db, g, l, p, place, ct);
        var result = await RepairSnapshot(db, l, p, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<bool> HasRepairReceiptAsync(Guid operationId, CancellationToken ct)
    { await using var db = database.OpenContext(); return await db.SpouseOrderRepairReceipts.AsNoTracking().AnyAsync(x => x.OperationId == operationId, ct); }
    public async Task RepairAsync(RepairSpouseOrder command, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ordered = JsonSerializer.Serialize(command.OrderedUnionIds);
        var old = await db.SpouseOrderRepairReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, ct);
        if (old is not null)
        {
            if (old.GenealogyId != command.GenealogyId || old.LineageId != command.LineageId || old.FatherId != command.FatherPersonId || old.PlacementId != command.FatherPlacementId || old.OriginalSnapshot != command.ExpectedEvidence || old.OrderedUnionIds != ordered) throw new InvalidOperationException("操作身份已用于不同输入。");
            return;
        }
        await ReadFather(db, command.GenealogyId, command.LineageId, command.FatherPersonId, command.FatherPlacementId, ct);
        var snapshot = await RepairSnapshot(db, command.LineageId, command.FatherPersonId, ct);
        if (snapshot.Evidence != command.ExpectedEvidence) throw new InvalidOperationException("原始排序或配偶资料已变化，请重新读取后确认。");
        if (command.OrderedUnionIds.Count != snapshot.Candidates.Count || !command.OrderedUnionIds.ToHashSet().SetEquals(snapshot.Candidates.Select(x => x.UnionId))) throw new InvalidOperationException("必须由用户明确指定全部配偶，且每位恰好一次。");
        var group = await db.SpouseOrderGroups.SingleOrDefaultAsync(x => x.LineageId == command.LineageId && x.FatherId == command.FatherPersonId, ct);
        var previousRepair = await db.SpouseOrderRepairReceipts.Where(x => x.LineageId == command.LineageId && x.FatherId == command.FatherPersonId).Select(x => (long?)x.NewRevision).MaxAsync(ct) ?? 0;
        var previousMove = await db.SpouseOrderReceipts.Where(x => x.LineageId == command.LineageId && x.FatherId == command.FatherPersonId).Select(x => (long?)x.ExpectedRevision).MaxAsync(ct) ?? -1;
        var adds = await db.SpouseReceipts.CountAsync(x => x.LineageId == command.LineageId && x.FatherPersonId == command.FatherPersonId, ct);
        var moves = await db.SpouseOrderReceipts.CountAsync(x => x.LineageId == command.LineageId && x.FatherId == command.FatherPersonId, ct);
        var revision = checked(Math.Max(Math.Max(group?.Revision ?? 0, previousRepair), Math.Max(checked(previousMove + 1), (long)adds + moves + previousRepair)) + 1);
        db.SpouseOrderRepairReceipts.Add(new() { OperationId=command.OperationId, GenealogyId=command.GenealogyId, LineageId=command.LineageId, FatherId=command.FatherPersonId, PlacementId=command.FatherPlacementId, OriginalSnapshot=snapshot.Evidence, OrderedUnionIds=ordered, NewRevision=revision });
        await db.SaveChangesAsync(ct);
        await db.SpouseOrderEntries.Where(x => x.LineageId == command.LineageId && x.FatherId == command.FatherPersonId).ExecuteDeleteAsync(ct);
        if (group is null) { group = new() { LineageId=command.LineageId, FatherId=command.FatherPersonId }; db.SpouseOrderGroups.Add(group); }
        group.Revision = revision;
        for (var i = 0; i < command.OrderedUnionIds.Count; i++) db.SpouseOrderEntries.Add(new() { LineageId=command.LineageId, FatherId=command.FatherPersonId, UnionId=command.OrderedUnionIds[i], Position=i+1 });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<MotherCandidate>> ReadMotherCandidatesAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, Guid childId, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await ReadFather(db, genealogyId, lineageId, fatherId, placementId, cancellationToken);
        if (!await db.Placements.AnyAsync(x => x.LineageId == lineageId && x.PersonId == childId && x.ParentPlacementId == placementId, cancellationToken) ||
            !await db.BiologicalParents.AnyAsync(x => x.LineageId == lineageId && x.ChildId == childId && x.ParentId == fatherId && x.Role == ParentRole.Father, cancellationToken))
            throw new InvalidOperationException("子女不属于当前父位置。");
        var (_, order) = await SpouseOrderQueries.Read(db, lineageId, fatherId, cancellationToken);
        var people = await db.People.AsNoTracking().Where(x => x.LineageId == lineageId).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (order.Any(x => !people.ContainsKey(x.Union.SpouseId))) throw new InvalidOperationException("配偶身份资料缺失，请先核实。");
        var candidates = order.Select(x => people[x.Union.SpouseId]).Where(x => x.Sex == PersonSex.Female && x.Id != childId)
            .Select(x => new MotherCandidate(x.Id, x.Name)).ToList();
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
        => (await ReadSnapshotAsync(genealogyId, lineageId, fatherId, placementId, cancellationToken)).Nodes;
    public async Task<SpouseSnapshot> ReadSnapshotAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await ReadFather(db, genealogyId, lineageId, fatherId, placementId, cancellationToken);
        var (group, order) = await SpouseOrderQueries.Read(db, lineageId, fatherId, cancellationToken);
        var people = await db.People.AsNoTracking().Where(x => x.LineageId == lineageId).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (order.Any(x => !people.ContainsKey(x.Union.SpouseId))) throw new InvalidOperationException("配偶身份资料缺失，请先核实。");
        var rows = order.Select(x => new SpouseNode(x.Union.SpouseId, x.Union.Id, people[x.Union.SpouseId].Name, x.Position)).ToArray();
        await tx.CommitAsync(cancellationToken);
        return new(group?.Revision ?? 0, rows);
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
        var (group, unions) = await SpouseOrderQueries.Read(db, command.LineageId, father.Id, cancellationToken);
        var spouse = new Person(Guid.NewGuid(), command.LineageId, command.Name, PersonSex.Female);
        var union = new SpouseUnion(Guid.NewGuid(), command.LineageId, father.Id, spouse.Id);
        db.People.Add(spouse); db.SpouseUnions.Add(union);
        if (group is null) { group = new() { LineageId = command.LineageId, FatherId = father.Id }; db.SpouseOrderGroups.Add(group); }
        group.Revision = checked(group.Revision + 1);
        db.SpouseOrderEntries.Add(new() { LineageId=command.LineageId, FatherId=father.Id, UnionId=union.Id, Position=unions.Count + 1 });
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
    public async Task<bool> HasOrderReceiptAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext();
        return await db.SpouseOrderReceipts.AsNoTracking().AnyAsync(x => x.OperationId == operationId, cancellationToken);
    }
    public async Task MoveAsync(MoveSpouse command, CancellationToken cancellationToken)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var receipt = await db.SpouseOrderReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.GenealogyId != command.GenealogyId || receipt.LineageId != command.LineageId || receipt.FatherId != command.FatherPersonId ||
                receipt.PlacementId != command.FatherPlacementId || receipt.SourceUnionId != command.SourceUnionId || receipt.TargetUnionId != command.TargetUnionId ||
                receipt.ExpectedRevision != command.ExpectedRevision) throw new InvalidOperationException("操作身份已用于不同输入。");
            return;
        }
        await ReadFather(db, command.GenealogyId, command.LineageId, command.FatherPersonId, command.FatherPlacementId, cancellationToken);
        var (group, order) = await SpouseOrderQueries.Read(db, command.LineageId, command.FatherPersonId, cancellationToken);
        if (group is null || group.Revision != command.ExpectedRevision) throw new InvalidOperationException("配偶顺序已变化，请重新读取。");
        var list = order.Select(x => x.Union.Id).ToList();
        var from = list.IndexOf(command.SourceUnionId); var to = list.IndexOf(command.TargetUnionId);
        if (from < 0 || to < 0) throw new InvalidOperationException("配偶不属于当前排序。");
        list.RemoveAt(from); list.Insert(to, command.SourceUnionId);
        var entries = await db.SpouseOrderEntries.Where(x => x.LineageId == command.LineageId && x.FatherId == command.FatherPersonId).ToListAsync(cancellationToken);
        foreach (var entry in entries) entry.Position = checked(entry.Position + entries.Count);
        await db.SaveChangesAsync(cancellationToken);
        for (var i = 0; i < list.Count; i++) entries.Single(x => x.UnionId == list[i]).Position = i + 1;
        group.Revision = checked(group.Revision + 1);
        db.SpouseOrderReceipts.Add(new() { OperationId=command.OperationId, GenealogyId=command.GenealogyId, LineageId=command.LineageId,
            FatherId=command.FatherPersonId, PlacementId=command.FatherPlacementId, SourceUnionId=command.SourceUnionId,
            TargetUnionId=command.TargetUnionId, ExpectedRevision=command.ExpectedRevision });
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
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
