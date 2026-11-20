using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;

namespace PuJiang.V1.Persistence;

public sealed class IsolatedBranchStore(IsolatedTestDatabase database) : IBranchStore
{
    private sealed record AttachState(Snapshot Snapshot, Person Source, Person Target, Person? Mother, int Generation, string Evidence);
    private static async Task<AttachState> LoadAttach(V1Context db, Guid g, Guid l, Guid person, Guid place, Guid targetPlace, CancellationToken ct)
    {
        var s = await LoadSnapshot(db, g, l, person, place, ct);
        if (s.ParentPlacementId is not null) throw new InvalidOperationException("请先将分支拆出，再挂接。");
        var targetPosition = s.Places.SingleOrDefault(x => x.Id == targetPlace) ?? throw new InvalidOperationException("目标位置已失效。");
        if (s.BranchPlaceIds.Contains(targetPlace)) throw new InvalidOperationException("不能挂接到本分支或其后代。");
        var source = s.People.Single(x => x.Id == person);
        var target = s.People.Single(x => x.Id == targetPosition.PersonId);
        if (target.Sex != PersonSex.Male || source.Sex is not (PersonSex.Male or PersonSex.Female) || target.ExplicitGeneration is null or <= 0 || source.ExplicitGeneration is null or <= 0) throw new InvalidOperationException("目标父亲或分支资料不符合普通挂接条件。");
        if (s.Places.Count(x => x.PersonId == target.Id) != 1) throw new InvalidOperationException("目标多位置暂不支持。");
        var order = await IsolatedChildOrderStore.Load(db, g, l, targetPlace, ct);
        var (spouseGroup, spouses) = await SpouseOrderQueries.Read(db, l, target.Id, ct);
        var mother = spouses.Count == 0 ? null : s.People.SingleOrDefault(x => x.Id == spouses[0].Union.SpouseId);
        if (spouses.Count > 0 && (mother is null || mother.Sex != PersonSex.Female || mother.Id == person)) throw new InvalidOperationException("首位配偶生母角色无效，请先核实。");
        var delta = (long)target.ExplicitGeneration.Value + 1 - source.ExplicitGeneration.Value;
        foreach (var id in s.BranchPersonIds)
        {
            var member = s.People.Single(x => x.Id == id);
            if (member.ExplicitGeneration is null or <= 0 || member.ExplicitGeneration.Value + delta is <= 0 or > int.MaxValue) throw new InvalidOperationException("分支世代无效或超出范围，请先核实。");
            var childPlace = s.Places.Single(x => x.PersonId == id);
            if (childPlace.ParentPlacementId is Guid parentId)
            {
                var parent = s.Places.Single(x => x.Id == parentId);
                var parentPerson = s.People.Single(x => x.Id == parent.PersonId);
                if (!s.Parents.Any(x => x.ChildId == id && x.ParentId == parent.PersonId && x.Role == ParentRole.Father) || member.ExplicitGeneration != parentPerson.ExplicitGeneration + 1) throw new InvalidOperationException("分支含非普通父子世代或关系，请先核实。");
            }
            if (s.Parents.Any(x => x.ParentId == id && x.Role == ParentRole.Father && !s.Places.Any(p => p.PersonId == x.ChildId && p.ParentPlacementId == childPlace.Id))) throw new InvalidOperationException("分支存在范围外后代关系，请先核实。");
        }
        var evidence = JsonSerializer.Serialize(new { Snapshot = s.Evidence, Target = targetPlace, OrderRevision = order.Revision, Order = IsolatedChildOrderStore.Evidence(order.Items), SpouseRevision = spouseGroup?.Revision, Spouses = spouses.Select(x => new { x.Union.Id, x.Union.SpouseId, x.Position }).ToArray() });
        return new(s, source, target, mother, checked(target.ExplicitGeneration.Value + 1), evidence);
    }
    public async Task<AttachPreview> PreviewAttachAsync(Guid g, Guid l, Guid person, Guid place, Guid targetPlace, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var a = await LoadAttach(db, g, l, person, place, targetPlace, ct);
        var father = a.Snapshot.Parents.SingleOrDefault(x => x.ChildId == person && x.Role == ParentRole.Father);
        var mother = a.Snapshot.Parents.SingleOrDefault(x => x.ChildId == person && x.Role == ParentRole.Mother);
        string? Name(Guid? id) => id is null ? null : a.Snapshot.People.Single(x => x.Id == id).Name;
        var result = new AttachPreview(a.Source.Name, a.Target.Name, Name(father?.ParentId), Name(mother?.ParentId), a.Mother?.Name, a.Source.ExplicitGeneration!.Value, a.Generation, a.Snapshot.BranchPersonIds.Count, a.Evidence);
        await tx.CommitAsync(ct); return result;
    }
    public async Task<bool> HasAttachReceiptAsync(Guid id, CancellationToken ct)
    { await using var db = database.OpenContext(); return await db.ChildOrderReceipts.AsNoTracking().AnyAsync(x => x.OperationId == id && x.Payload.StartsWith("attach:"), ct); }
    public async Task AttachAsync(AttachBranch command, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payload = "attach:" + JsonSerializer.Serialize(command);
        var receipt = await db.ChildOrderReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, ct);
        if (receipt is not null) { if (receipt.Payload != payload) throw new InvalidOperationException("操作身份已用于不同输入。"); return; }
        var a = await LoadAttach(db, command.GenealogyId, command.LineageId, command.PersonId, command.PlacementId, command.TargetPlacementId, ct);
        if (a.Evidence != command.ExpectedEvidence) throw new InvalidOperationException("挂接范围已变化，请重新查看并确认。");
        var oldParents = await db.BiologicalParents.Where(x => x.LineageId == command.LineageId && x.ChildId == command.PersonId).ToListAsync(ct);
        var history = oldParents.Select(x => new { x.Id, x.Role, x.ParentId, Name = a.Snapshot.People.Single(p => p.Id == x.ParentId).Name }).ToArray();
        var group = await IsolatedChildOrderStore.Load(db, command.GenealogyId, command.LineageId, command.TargetPlacementId, ct);
        var insert = group.Items.Count(x => x.Person.Sex <= a.Source.Sex);
        for (var i = insert; i < group.Items.Count; i++) group.Items[i].Place.SetPosition(i + 2);
        var root = await db.Placements.SingleAsync(x => x.LineageId == command.LineageId && x.Id == command.PlacementId, ct);
        root.Attach(command.TargetPlacementId, insert + 1);
        var ids = a.Snapshot.BranchPersonIds.ToArray();
        var people = await db.People.Where(x => x.LineageId == command.LineageId && ids.Contains(x.Id)).ToListAsync(ct);
        var delta = a.Generation - a.Source.ExplicitGeneration!.Value;
        foreach (var member in people) member.SetGeneration(checked(member.ExplicitGeneration!.Value + delta));
        people.Single(x => x.Id == command.PersonId).MarkMotherKnown();
        db.BiologicalParents.RemoveRange(oldParents); await db.SaveChangesAsync(ct);
        db.BiologicalParents.Add(new(Guid.NewGuid(), command.LineageId, command.PersonId, a.Target.Id, ParentRole.Father));
        if (a.Mother is not null) db.BiologicalParents.Add(new(Guid.NewGuid(), command.LineageId, command.PersonId, a.Mother.Id, ParentRole.Mother));
        await db.SaveChangesAsync(ct);
        foreach (var oldParent in oldParents.Select(x => x.ParentId).Distinct()) await CleanupCandidate(db, command.LineageId, oldParent, ct);
        db.LineageActivities.Add(new() { Id = Guid.NewGuid(), LineageId = command.LineageId, OperationId = command.OperationId, Kind = "挂接分支", OccurredAt = DateTimeOffset.UtcNow, Detail = JsonSerializer.Serialize(new { ChildId = command.PersonId, ChildName = a.Source.Name, PreviousParents = history, NewFatherId = a.Target.Id, NewFather = a.Target.Name, NewMotherId = a.Mother?.Id }) });
        await ChildOrderVersions.Touch(db, command.LineageId, command.TargetPlacementId, ct);
        db.ChildOrderReceipts.Add(new() { OperationId = command.OperationId, Payload = payload });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<RootParentInfo> ReadRootParentsAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await LoadRootParents(db, genealogyId, lineageId, personId, placementId, ct);
        await tx.CommitAsync(ct); return result;
    }
    private static async Task<RootParentInfo> LoadRootParents(V1Context db, Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken ct)
    {
        if (!await db.Lineages.AnyAsync(x => x.Id == lineageId && x.GenealogyId == genealogyId, ct) || !await db.Placements.AnyAsync(x => x.LineageId == lineageId && x.Id == placementId && x.PersonId == personId && x.ParentPlacementId == null, ct)) throw new InvalidOperationException("首节点位置已失效。");
        var child = await db.People.AsNoTracking().SingleAsync(x => x.LineageId == lineageId && x.Id == personId, ct);
        var parents = await (from relation in db.BiologicalParents.AsNoTracking()
                             join parent in db.People.AsNoTracking() on new { relation.LineageId, Id = relation.ParentId } equals new { parent.LineageId, parent.Id }
                             where relation.LineageId == lineageId && relation.ChildId == personId
                             select new { relation.Id, relation.Role, parent.Name, ParentId = parent.Id }).ToListAsync(ct);
        var father = parents.SingleOrDefault(x => x.Role == ParentRole.Father); var mother = parents.SingleOrDefault(x => x.Role == ParentRole.Mother);
        if (mother is not null && child.MotherExplicitlyCleared) throw new InvalidOperationException("生母清空状态与关系不一致。");
        var parentIds = parents.Select(x => x.ParentId).ToArray(); var personText = personId.ToString();
        var profiles = await db.PersonProfileReceipts.AsNoTracking().Where(x => x.LineageId == lineageId && parentIds.Contains(x.PersonId)).Select(x => x.OperationId).ToListAsync(ct);
        var motherReceipts = await db.MotherReceipts.AsNoTracking().Where(x => x.LineageId == lineageId && x.ChildPersonId == personId).Select(x => x.OperationId).ToListAsync(ct);
        var edits = await db.ChildOrderReceipts.AsNoTracking().Where(x => x.Payload.StartsWith("root-parents:") && x.Payload.Contains(personText)).Select(x => x.OperationId).ToListAsync(ct);
        var evidence = JsonSerializer.Serialize(new { Parents = parents.OrderBy(x => x.Role).ToArray(), child.MotherExplicitlyCleared, Profiles = profiles.Order().ToArray(), MotherReceipts = motherReceipts.Order().ToArray(), Edits = edits.Order().ToArray() });
        return new(father?.ParentId, father?.Name, mother?.ParentId, mother?.Name, evidence);
    }
    public async Task<bool> HasRootParentsReceiptAsync(Guid operationId, CancellationToken ct)
    { await using var db = database.OpenContext(); return await db.ChildOrderReceipts.AsNoTracking().AnyAsync(x => x.OperationId == operationId && x.Payload.StartsWith("root-parents:"), ct); }
    public async Task UpdateRootParentsAsync(UpdateRootParents command, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payload = "root-parents:" + JsonSerializer.Serialize(command);
        var receipt = await db.ChildOrderReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, ct);
        if (receipt is not null) { if (receipt.Payload != payload) throw new InvalidOperationException("操作身份已用于不同输入。"); return; }
        var current = await LoadRootParents(db, command.GenealogyId, command.LineageId, command.PersonId, command.PlacementId, ct);
        if (current.Evidence != command.ExpectedEvidence) throw new InvalidOperationException("生父母资料已变化，请重新读取。");
        var fatherName = string.IsNullOrWhiteSpace(command.FatherName) ? null : command.FatherName.Trim();
        var motherName = string.IsNullOrWhiteSpace(command.MotherName) ? null : command.MotherName.Trim();
        var changeFather = fatherName != current.FatherName; var changeMother = motherName != current.MotherName;
        var child = await db.People.SingleAsync(x => x.LineageId == command.LineageId && x.Id == command.PersonId, ct);
        var relations = await db.BiologicalParents.Where(x => x.LineageId == command.LineageId && x.ChildId == command.PersonId).ToListAsync(ct);
        foreach (var relation in relations.Where(x => changeFather && x.Role == ParentRole.Father || changeMother && x.Role == ParentRole.Mother)) db.BiologicalParents.Remove(relation);
        if (changeMother) { if (motherName is null) child.MarkMotherCleared(); else child.MarkMotherKnown(); }
        await db.SaveChangesAsync(ct); // Remove old role first; the whole transaction still rolls back on failure.
        if (changeFather && fatherName is not null) { var parent = new Person(Guid.NewGuid(), command.LineageId, fatherName, PersonSex.Male); db.People.Add(parent); db.BiologicalParents.Add(new BiologicalParent(Guid.NewGuid(), command.LineageId, command.PersonId, parent.Id, ParentRole.Father)); }
        if (changeMother && motherName is not null) { var parent = new Person(Guid.NewGuid(), command.LineageId, motherName, PersonSex.Female); db.People.Add(parent); db.BiologicalParents.Add(new BiologicalParent(Guid.NewGuid(), command.LineageId, command.PersonId, parent.Id, ParentRole.Mother)); }
        await db.SaveChangesAsync(ct);
        if (changeFather && current.FatherId is Guid oldFather) await CleanupCandidate(db, command.LineageId, oldFather, ct);
        if (changeMother && current.MotherId is Guid oldMother) await CleanupCandidate(db, command.LineageId, oldMother, ct);
        db.ChildOrderReceipts.Add(new() { OperationId = command.OperationId, Payload = payload });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private static async Task CleanupCandidate(V1Context db, Guid lineageId, Guid personId, CancellationToken ct)
    {
        var referenced = await db.Placements.AnyAsync(x => x.LineageId == lineageId && x.PersonId == personId, ct) ||
            await db.BiologicalParents.AnyAsync(x => x.LineageId == lineageId && (x.ParentId == personId || x.ChildId == personId), ct) ||
            await db.SpouseUnions.AnyAsync(x => x.LineageId == lineageId && (x.FatherId == personId || x.SpouseId == personId), ct) ||
            await db.SpouseOrderGroups.AnyAsync(x => x.LineageId == lineageId && x.FatherId == personId, ct);
        if (!referenced) await db.People.Where(x => x.LineageId == lineageId && x.Id == personId).ExecuteDeleteAsync(ct);
    }
    private sealed record Snapshot(List<Placement> Places, List<Person> People, List<BiologicalParent> Parents, List<SpouseUnion> Unions, List<SpouseOrderEntry> Orders, List<ChildOrderVersion> Revisions, HashSet<Guid> BranchPlaceIds, HashSet<Guid> BranchPersonIds, Guid? ParentPlacementId, string Evidence);

    private static async Task<Snapshot> LoadSnapshot(V1Context db, Guid g, Guid l, Guid person, Guid placement, CancellationToken ct)
    {
        if (!await db.Lineages.AnyAsync(x => x.Id == l && x.GenealogyId == g, ct)) throw new InvalidOperationException("谱系已失效。");
        var places = await db.Placements.AsNoTracking().Where(x => x.LineageId == l).ToListAsync(ct);
        var people = await db.People.AsNoTracking().Where(x => x.LineageId == l).ToListAsync(ct);
        var parents = await db.BiologicalParents.AsNoTracking().Where(x => x.LineageId == l).ToListAsync(ct);
        var unions = await db.SpouseUnions.AsNoTracking().Where(x => x.LineageId == l).ToListAsync(ct);
        var orders = await db.SpouseOrderEntries.AsNoTracking().Where(x => x.LineageId == l).ToListAsync(ct);
        var spouseGroups = await db.SpouseOrderGroups.AsNoTracking().Where(x => x.LineageId == l).ToListAsync(ct);
        var revisions = await db.ChildOrderVersions.AsNoTracking().Where(x => x.LineageId == l).ToListAsync(ct);
        var lineageText = l.ToString();
        var profileReceipts = await db.PersonProfileReceipts.AsNoTracking().Where(x => x.LineageId == l).Select(x => x.OperationId).ToListAsync(ct);
        var motherReceipts = await db.MotherReceipts.AsNoTracking().Where(x => x.LineageId == l).Select(x => x.OperationId).ToListAsync(ct);
        var generationReceipts = await db.GenerationReceipts.AsNoTracking().Where(x => x.Payload.Contains(lineageText)).Select(x => x.OperationId).ToListAsync(ct);
        var rootReceipts = await db.RootDetailsReceipts.AsNoTracking().Where(x => x.Payload.Contains(lineageText)).Select(x => x.OperationId).ToListAsync(ct);
        var root = places.SingleOrDefault(x => x.Id == placement && x.PersonId == person) ?? throw new InvalidOperationException("当前人物位置已失效。");
        var ids = new HashSet<Guid> { root.Id }; var queue = new Queue<Guid>(); queue.Enqueue(root.Id);
        while (queue.TryDequeue(out var parent)) foreach (var child in places.Where(x => x.ParentPlacementId == parent)) { if (!ids.Add(child.Id)) throw new InvalidOperationException("位置结构异常。"); queue.Enqueue(child.Id); }
        var personIds = places.Where(x => ids.Contains(x.Id)).Select(x => x.PersonId).ToHashSet();
        if (places.Count(x => ids.Contains(x.Id)) != personIds.Count || places.Any(x => personIds.Contains(x.PersonId) && !ids.Contains(x.Id))) throw new InvalidOperationException("多位置删除尚未纳入本批，请先核实。");
        if (people.Count(x => personIds.Contains(x.Id)) != personIds.Count) throw new InvalidOperationException("分支人物资料缺失。");
        foreach (var id in personIds) await SpouseOrderQueries.Read(db, l, id, ct);
        foreach (var parentId in places.Where(x => ids.Contains(x.Id) && x.ParentPlacementId is not null).Select(x => x.ParentPlacementId!.Value).Distinct())
            await IsolatedChildOrderStore.Load(db, g, l, parentId, ct);
        var evidence = JsonSerializer.Serialize(new {
            People = people.OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.Sex, x.MotherExplicitlyCleared, x.ExplicitGeneration, x.Title, x.Notes }).ToArray(),
            Places = places.OrderBy(x => x.Id).Select(x => new { x.Id, x.PersonId, x.ParentPlacementId, x.Position }).ToArray(),
            Parents = parents.OrderBy(x => x.Id).Select(x => new { x.Id, x.ChildId, x.ParentId, x.Role }).ToArray(),
            Unions = unions.OrderBy(x => x.Id).Select(x => new { x.Id, x.FatherId, x.SpouseId }).ToArray(),
            Orders = orders.OrderBy(x => x.FatherId).ThenBy(x => x.UnionId).Select(x => new { x.FatherId, x.UnionId, x.Position }).ToArray(),
            SpouseGroups = spouseGroups.OrderBy(x => x.FatherId).Select(x => new { x.FatherId, x.Revision }).ToArray(),
            Revisions = revisions.OrderBy(x => x.ParentPlacementId).Select(x => new { x.ParentPlacementId, x.Revision }).ToArray(),
            ProfileReceipts = profileReceipts.Order().ToArray(), MotherReceipts = motherReceipts.Order().ToArray(),
            GenerationReceipts = generationReceipts.Order().ToArray(), RootReceipts = rootReceipts.Order().ToArray()
        });
        return new(places, people, parents, unions, orders, revisions, ids, personIds, root.ParentPlacementId, evidence);
    }

    public async Task<BranchDeletionPreview> PreviewDeleteAsync(Guid g, Guid l, Guid person, Guid placement, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = await LoadSnapshot(db, g, l, person, placement, ct);
        var names = s.People.Where(x => s.BranchPersonIds.Contains(x.Id)).OrderBy(x => x.Name).Select(x => x.Name).ToArray();
        var spouseIds = s.Unions.Where(x => s.BranchPersonIds.Contains(x.FatherId)).Select(x => x.SpouseId).ToHashSet();
        if (s.Places.Any(x => spouseIds.Contains(x.PersonId))) throw new InvalidOperationException("配偶存在独立位置，请先核实。");
        var spouses = s.People.Where(x => spouseIds.Contains(x.Id)).OrderBy(x => x.Name).Select(x => x.Name).ToArray();
        var candidates = s.BranchPersonIds.Concat(spouseIds).ToHashSet();
        var retainedGroups = await db.SpouseOrderGroups.AsNoTracking().Where(x => x.LineageId == l && !s.BranchPersonIds.Contains(x.FatherId)).Select(x => x.FatherId).ToListAsync(ct);
        var retained = s.People.Where(x => candidates.Contains(x.Id) && (
            s.Parents.Any(p => !s.BranchPersonIds.Contains(p.ChildId) && (p.ParentId == x.Id || p.ChildId == x.Id)) ||
            s.Unions.Any(u => !s.BranchPersonIds.Contains(u.FatherId) && (u.FatherId == x.Id || u.SpouseId == x.Id)) ||
            s.Places.Any(p => !s.BranchPlaceIds.Contains(p.Id) && p.PersonId == x.Id) || retainedGroups.Contains(x.Id)))
            .OrderBy(x => x.Name).Select(x => x.Name).ToArray();
        var linkedParents = s.Parents.Where(p => s.BranchPersonIds.Contains(p.ChildId) && !candidates.Contains(p.ParentId)).Select(p => p.ParentId).ToHashSet();
        var autoCleaned = s.People.Where(x => linkedParents.Contains(x.Id) &&
            !s.Places.Any(p => !s.BranchPlaceIds.Contains(p.Id) && p.PersonId == x.Id) &&
            !s.Parents.Any(p => !s.BranchPersonIds.Contains(p.ChildId) && (p.ParentId == x.Id || p.ChildId == x.Id)) &&
            !s.Unions.Any(u => !s.BranchPersonIds.Contains(u.FatherId) && (u.FatherId == x.Id || u.SpouseId == x.Id)) &&
            !retainedGroups.Contains(x.Id)).OrderBy(x => x.Name).Select(x => x.Name).ToArray();
        await tx.CommitAsync(ct); return new(person, placement, names, spouses, retained, autoCleaned, s.Evidence);
    }

    public async Task<bool> HasDeleteReceiptAsync(Guid operationId, CancellationToken ct)
    { await using var db = database.OpenContext(); return await db.ChildOrderReceipts.AsNoTracking().AnyAsync(x => x.OperationId == operationId && x.Payload.StartsWith("delete:"), ct); }

    public async Task DeleteAsync(DeleteBranch command, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payload = "delete:" + JsonSerializer.Serialize(command);
        var receipt = await db.ChildOrderReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, ct);
        if (receipt is not null) { if (receipt.Payload != payload) throw new InvalidOperationException("操作身份已用于不同输入。"); return; }
        var s = await LoadSnapshot(db, command.GenealogyId, command.LineageId, command.PersonId, command.PlacementId, ct);
        if (s.Evidence != command.ExpectedEvidence) throw new InvalidOperationException("删除范围已变化，请重新查看并确认。");
        var placeIds = s.BranchPlaceIds.ToArray(); var personIds = s.BranchPersonIds.ToArray();
        var spouseIds = s.Unions.Where(x => s.BranchPersonIds.Contains(x.FatherId)).Select(x => x.SpouseId).Distinct().ToArray();
        var linkedParentIds = s.Parents.Where(x => s.BranchPersonIds.Contains(x.ChildId)).Select(x => x.ParentId).Distinct().ToArray();
        if (s.Places.Any(x => spouseIds.Contains(x.PersonId))) throw new InvalidOperationException("配偶存在独立位置，请先核实。");
        var unionIds = s.Unions.Where(x => s.BranchPersonIds.Contains(x.FatherId)).Select(x => x.Id).ToArray();
        if (s.ParentPlacementId is Guid parentId)
        {
            var group = await IsolatedChildOrderStore.Load(db, command.GenealogyId, command.LineageId, parentId, ct);
            var remaining = group.Items.Where(x => x.Place.Id != command.PlacementId).ToArray();
            for (var i = 0; i < remaining.Length; i++) remaining[i].Place.SetPosition(i + 1);
            await ChildOrderVersions.Touch(db, command.LineageId, parentId, ct);
        }
        await db.SpouseOrderEntries.Where(x => x.LineageId == command.LineageId && unionIds.Contains(x.UnionId)).ExecuteDeleteAsync(ct);
        await db.SpouseOrderGroups.Where(x => x.LineageId == command.LineageId && personIds.Contains(x.FatherId)).ExecuteDeleteAsync(ct);
        await db.SpouseUnions.Where(x => x.LineageId == command.LineageId && unionIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.BiologicalParents.Where(x => x.LineageId == command.LineageId && personIds.Contains(x.ChildId)).ExecuteDeleteAsync(ct);
        await db.ChildOrderVersions.Where(x => x.LineageId == command.LineageId && placeIds.Contains(x.ParentPlacementId)).ExecuteDeleteAsync(ct);
        var pending = s.Places.Where(x => s.BranchPlaceIds.Contains(x.Id)).ToDictionary(x => x.Id);
        while (pending.Count > 0)
        {
            var leaves = pending.Values.Where(x => !pending.Values.Any(y => y.ParentPlacementId == x.Id)).Select(x => x.Id).ToArray();
            if (leaves.Length == 0) throw new InvalidOperationException("位置结构循环，无法删除。");
            await db.Placements.Where(x => x.LineageId == command.LineageId && leaves.Contains(x.Id)).ExecuteDeleteAsync(ct);
            foreach (var leaf in leaves) pending.Remove(leaf);
        }
        foreach (var id in personIds.Concat(spouseIds).Concat(linkedParentIds).Distinct()) await CleanupCandidate(db, command.LineageId, id, ct);
        db.ChildOrderReceipts.Add(new() { OperationId = command.OperationId, Payload = payload });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<SpouseDeletionPreview> PreviewSpouseAsync(Guid g, Guid l, Guid father, Guid placement, Guid spouse, Guid union, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = await LoadSnapshot(db, g, l, father, placement, ct);
        if (!s.Unions.Any(x => x.Id == union && x.FatherId == father && x.SpouseId == spouse)) throw new InvalidOperationException("配偶关系已失效。");
        var target = s.People.SingleOrDefault(x => x.Id == spouse) ?? throw new InvalidOperationException("配偶资料不存在。");
        var attached = s.Places.Where(x => x.ParentPlacementId == placement).Select(x => x.PersonId).ToHashSet();
        var affectedIds = s.Parents.Where(x => x.ParentId == spouse && x.Role == ParentRole.Mother && attached.Contains(x.ChildId)).Select(x => x.ChildId).ToHashSet();
        var names = s.People.Where(x => affectedIds.Contains(x.Id)).Select(x => x.Name).OrderBy(x => x).ToArray();
        await tx.CommitAsync(ct); return new(spouse, union, target.Name, names, s.Evidence);
    }

    public async Task<bool> HasSpouseDeleteReceiptAsync(Guid operationId, CancellationToken ct)
    { await using var db = database.OpenContext(); return await db.ChildOrderReceipts.AsNoTracking().AnyAsync(x => x.OperationId == operationId && x.Payload.StartsWith("delete-spouse:"), ct); }

    public async Task DeleteSpouseAsync(DeleteSpouse command, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payload = "delete-spouse:" + JsonSerializer.Serialize(command);
        var receipt = await db.ChildOrderReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, ct);
        if (receipt is not null) { if (receipt.Payload != payload) throw new InvalidOperationException("操作身份已用于不同输入。"); return; }
        var s = await LoadSnapshot(db, command.GenealogyId, command.LineageId, command.FatherId, command.FatherPlacementId, ct);
        if (s.Evidence != command.ExpectedEvidence) throw new InvalidOperationException("配偶或关系已变化，请重新查看并确认。");
        if (!s.Unions.Any(x => x.Id == command.UnionId && x.FatherId == command.FatherId && x.SpouseId == command.SpouseId)) throw new InvalidOperationException("配偶关系已失效。");
        var (group, order) = await SpouseOrderQueries.Read(db, command.LineageId, command.FatherId, ct);
        if (group is null) throw new InvalidOperationException("配偶排序不存在。");
        var attached = s.Places.Where(x => x.ParentPlacementId == command.FatherPlacementId).Select(x => x.PersonId).ToArray();
        if (s.People.Any(x => attached.Contains(x.Id) && x.MotherExplicitlyCleared && s.Parents.Any(p => p.ChildId == x.Id && p.Role == ParentRole.Mother))) throw new InvalidOperationException("生母清空状态与关系不一致。");
        await db.BiologicalParents.Where(x => x.LineageId == command.LineageId && x.ParentId == command.SpouseId && x.Role == ParentRole.Mother && attached.Contains(x.ChildId)).ExecuteDeleteAsync(ct);
        await db.SpouseOrderEntries.Where(x => x.LineageId == command.LineageId && x.FatherId == command.FatherId && x.UnionId == command.UnionId).ExecuteDeleteAsync(ct);
        await db.SpouseUnions.Where(x => x.LineageId == command.LineageId && x.Id == command.UnionId && x.FatherId == command.FatherId).ExecuteDeleteAsync(ct);
        var remaining = order.Where(x => x.Union.Id != command.UnionId).Select(x => x.Union.Id).ToArray();
        var offset = order.Count + 1;
        await db.SpouseOrderEntries.Where(x => x.LineageId == command.LineageId && x.FatherId == command.FatherId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Position, x => x.Position + offset), ct);
        for (var i = 0; i < remaining.Length; i++) { var position = i + 1; var id = remaining[i]; await db.SpouseOrderEntries.Where(x => x.LineageId == command.LineageId && x.FatherId == command.FatherId && x.UnionId == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Position, position), ct); }
        group.Revision = checked(group.Revision + 1);
        await CleanupCandidate(db, command.LineageId, command.SpouseId, ct);
        db.ChildOrderReceipts.Add(new() { OperationId = command.OperationId, Payload = payload });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<bool> HasDetachReceiptAsync(Guid operationId, CancellationToken ct) =>
        await HasReceiptAsync(operationId, ct);
    private async Task<bool> HasReceiptAsync(Guid id, CancellationToken ct)
    { await using var db = database.OpenContext(); return await db.ChildOrderReceipts.AsNoTracking().AnyAsync(x => x.OperationId == id && x.Payload.StartsWith("detach:"), ct); }
    public async Task DetachAsync(DetachBranch command, CancellationToken ct)
    {
        await using var db = database.OpenContext(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payload = "detach:" + JsonSerializer.Serialize(command);
        var receipt = await db.ChildOrderReceipts.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, ct);
        if (receipt is not null) { if (receipt.Payload != payload) throw new InvalidOperationException("操作身份已用于不同输入。"); return; }
        var group = await IsolatedChildOrderStore.Load(db, command.GenealogyId, command.LineageId, command.ParentPlacementId, ct);
        if (group.Revision != command.ExpectedRevision || IsolatedChildOrderStore.Evidence(group.Items) != command.ExpectedEvidence) throw new InvalidOperationException("同胞或父位置已变化，请重新发起拆支。");
        var selected = group.Items.SingleOrDefault(x => x.Place.Id == command.PlacementId && x.Person.Id == command.PersonId);
        if (selected.Place is null) throw new InvalidOperationException("目标位置已失效。");
        selected.Place.Detach(); group.Items.Remove(selected);
        for (var i = 0; i < group.Items.Count; i++) group.Items[i].Place.SetPosition(i + 1);
        await ChildOrderVersions.Touch(db, command.LineageId, command.ParentPlacementId, ct);
        db.ChildOrderReceipts.Add(new() { OperationId = command.OperationId, Payload = payload });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
