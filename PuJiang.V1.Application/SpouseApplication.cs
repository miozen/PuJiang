namespace PuJiang.V1.Application;

public sealed record AddSpouse(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid FatherPersonId, Guid FatherPlacementId, RootNameMode NameMode, string Name);
public sealed record SpouseCreated(Guid PersonId, Guid UnionId, bool Replayed);
public sealed record SpouseNode(Guid PersonId, Guid UnionId, string Name, int Position);
public sealed record SpouseSnapshot(long Revision, IReadOnlyList<SpouseNode> Nodes);
public sealed record MoveSpouse(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid FatherPersonId, Guid FatherPlacementId, Guid SourceUnionId, Guid TargetUnionId, long ExpectedRevision);
public sealed record RepairCandidate(Guid UnionId, Guid PersonId, string Name);
public sealed record RepairSnapshot(string Evidence, IReadOnlyList<RepairCandidate> Candidates);
public sealed record RepairSpouseOrder(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid FatherPersonId, Guid FatherPlacementId, string ExpectedEvidence, IReadOnlyList<Guid> OrderedUnionIds);
public sealed record MotherCandidate(Guid PersonId, string Name);
public sealed record SetChildMother(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid FatherPersonId, Guid FatherPlacementId, Guid ChildPersonId, Guid? MotherId, Guid? ExpectedMotherId, bool ExpectedCleared);
public interface ISpouseStore
{
    Task<SpouseCreated> AddAsync(AddSpouse command, CancellationToken cancellationToken);
    Task SetMotherAsync(SetChildMother command, CancellationToken cancellationToken);
    Task<IReadOnlyList<SpouseNode>> ReadAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MotherCandidate>> ReadMotherCandidatesAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, Guid childId, CancellationToken cancellationToken);
    Task<bool> HasReceiptAsync(Guid operationId, bool motherOperation, CancellationToken cancellationToken);
    Task<SpouseSnapshot> ReadSnapshotAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, CancellationToken cancellationToken);
    Task MoveAsync(MoveSpouse command, CancellationToken cancellationToken);
    Task<bool> HasOrderReceiptAsync(Guid operationId, CancellationToken cancellationToken);
    Task<RepairSnapshot> ReadRepairAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, CancellationToken cancellationToken);
    Task RepairAsync(RepairSpouseOrder command, CancellationToken cancellationToken);
    Task<bool> HasRepairReceiptAsync(Guid operationId, CancellationToken cancellationToken);
}
public sealed class SpouseApplication(ISpouseStore store)
{
    public Task<RepairSnapshot> ReadRepairAsync(Guid g, Guid l, Guid p, Guid place, CancellationToken ct = default) => store.ReadRepairAsync(g, l, p, place, ct);
    public Task<bool> HasRepairReceiptAsync(Guid id, CancellationToken ct = default) => store.HasRepairReceiptAsync(id, ct);
    public Task RepairAsync(RepairSpouseOrder command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.FatherPersonId == Guid.Empty || command.FatherPlacementId == Guid.Empty || string.IsNullOrEmpty(command.ExpectedEvidence) || command.OrderedUnionIds is null || command.OrderedUnionIds.Any(x => x == Guid.Empty) || command.OrderedUnionIds.Distinct().Count() != command.OrderedUnionIds.Count) throw new ArgumentException("修复操作或指定顺序无效。");
        return store.RepairAsync(command, ct);
    }
    public Task<SpouseSnapshot> ReadSnapshotAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, CancellationToken cancellationToken = default)
        => store.ReadSnapshotAsync(genealogyId, lineageId, fatherId, placementId, cancellationToken);
    public Task<bool> HasOrderReceiptAsync(Guid operationId, CancellationToken cancellationToken = default) => store.HasOrderReceiptAsync(operationId, cancellationToken);
    public Task MoveAsync(MoveSpouse command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.FatherPersonId == Guid.Empty ||
            command.FatherPlacementId == Guid.Empty || command.SourceUnionId == Guid.Empty || command.TargetUnionId == Guid.Empty || command.SourceUnionId == command.TargetUnionId || command.ExpectedRevision < 0)
            throw new ArgumentException("排序操作或目标无效。");
        return store.MoveAsync(command, cancellationToken);
    }
    public Task<bool> HasReceiptAsync(Guid operationId, bool motherOperation, CancellationToken cancellationToken = default) => store.HasReceiptAsync(operationId, motherOperation, cancellationToken);
    public Task<IReadOnlyList<SpouseNode>> ReadAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, CancellationToken cancellationToken = default)
        => store.ReadAsync(genealogyId, lineageId, fatherId, placementId, cancellationToken);
    public Task<IReadOnlyList<MotherCandidate>> ReadMotherCandidatesAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, Guid childId, CancellationToken cancellationToken = default)
        => store.ReadMotherCandidatesAsync(genealogyId, lineageId, fatherId, placementId, childId, cancellationToken);
    public Task<SpouseCreated> AddAsync(AddSpouse command, CancellationToken cancellationToken = default)
    {
        Validate(command); return store.AddAsync(command, cancellationToken);
    }
    public static void Validate(AddSpouse command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.FatherPersonId == Guid.Empty || command.FatherPlacementId == Guid.Empty) throw new ArgumentException("父亲和父位置不能为空。");
        FirstRootApplication.Validate(new(command.OperationId, command.GenealogyId, command.LineageId, command.NameMode, command.Name));
    }
    public Task SetMotherAsync(SetChildMother command, CancellationToken cancellationToken = default)
    {
        Validate(command); return store.SetMotherAsync(command, cancellationToken);
    }
    public static void Validate(SetChildMother command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.FatherPersonId == Guid.Empty || command.FatherPlacementId == Guid.Empty || command.ChildPersonId == Guid.Empty || command.MotherId == Guid.Empty || command.ExpectedMotherId == Guid.Empty)
            throw new ArgumentException("操作或目标身份不能为空。");
    }
}
