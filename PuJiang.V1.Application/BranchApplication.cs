namespace PuJiang.V1.Application;

public sealed record DetachBranch(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid PersonId, Guid PlacementId, Guid ParentPlacementId, long ExpectedRevision, string ExpectedEvidence);
public sealed record BranchDeletionPreview(Guid PersonId, Guid PlacementId, IReadOnlyList<string> People, IReadOnlyList<string> Spouses, IReadOnlyList<string> RetainedPeople, IReadOnlyList<string> AutoCleanedPeople, string Evidence);
public sealed record DeleteBranch(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid PersonId, Guid PlacementId, string ExpectedEvidence);
public sealed record SpouseDeletionPreview(Guid SpouseId, Guid UnionId, string SpouseName, IReadOnlyList<string> AffectedChildren, string Evidence);
public sealed record DeleteSpouse(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid FatherId, Guid FatherPlacementId, Guid SpouseId, Guid UnionId, string ExpectedEvidence);
public sealed record RootParentInfo(Guid? FatherId, string? FatherName, Guid? MotherId, string? MotherName, string Evidence);
public sealed record UpdateRootParents(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid PersonId, Guid PlacementId, string? FatherName, string? MotherName, string ExpectedEvidence);
public sealed record AttachBranch(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid PersonId, Guid PlacementId, Guid TargetPlacementId, string ExpectedEvidence);
public sealed record AttachPreview(string SourceName, string TargetName, string? OldFatherName, string? OldMotherName, string? NewMotherName, int OldGeneration, int NewGeneration, int PeopleCount, string Evidence);
public interface IBranchStore
{
    Task<AttachPreview> PreviewAttachAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, Guid targetPlacementId, CancellationToken ct);
    Task AttachAsync(AttachBranch command, CancellationToken ct);
    Task<bool> HasAttachReceiptAsync(Guid operationId, CancellationToken ct);
    Task DetachAsync(DetachBranch command, CancellationToken ct);
    Task<bool> HasDetachReceiptAsync(Guid operationId, CancellationToken ct);
    Task<BranchDeletionPreview> PreviewDeleteAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken ct);
    Task DeleteAsync(DeleteBranch command, CancellationToken ct);
    Task<bool> HasDeleteReceiptAsync(Guid operationId, CancellationToken ct);
    Task<SpouseDeletionPreview> PreviewSpouseAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid fatherPlacementId, Guid spouseId, Guid unionId, CancellationToken ct);
    Task DeleteSpouseAsync(DeleteSpouse command, CancellationToken ct);
    Task<bool> HasSpouseDeleteReceiptAsync(Guid operationId, CancellationToken ct);
    Task<RootParentInfo> ReadRootParentsAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken ct);
    Task UpdateRootParentsAsync(UpdateRootParents command, CancellationToken ct);
    Task<bool> HasRootParentsReceiptAsync(Guid operationId, CancellationToken ct);
}
public sealed class BranchApplication(IBranchStore store)
{
    public Task<AttachPreview> PreviewAttachAsync(Guid g, Guid l, Guid person, Guid place, Guid target, CancellationToken ct = default)
    {
        if (g == Guid.Empty || l == Guid.Empty || person == Guid.Empty || place == Guid.Empty || target == Guid.Empty || place == target) throw new ArgumentException("挂接目标无效。");
        return store.PreviewAttachAsync(g, l, person, place, target, ct);
    }
    public Task AttachAsync(AttachBranch command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.PersonId == Guid.Empty || command.PlacementId == Guid.Empty || command.TargetPlacementId == Guid.Empty || command.PlacementId == command.TargetPlacementId || string.IsNullOrWhiteSpace(command.ExpectedEvidence)) throw new ArgumentException("挂接上下文无效。");
        return store.AttachAsync(command, ct);
    }
    public Task<bool> HasAttachReceiptAsync(Guid id, CancellationToken ct = default) => store.HasAttachReceiptAsync(id, ct);
    public Task DetachAsync(DetachBranch command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.PersonId == Guid.Empty || command.PlacementId == Guid.Empty || command.ParentPlacementId == Guid.Empty || command.ExpectedRevision < 0 || string.IsNullOrWhiteSpace(command.ExpectedEvidence)) throw new ArgumentException("拆支操作上下文无效。");
        return store.DetachAsync(command, ct);
    }
    public Task<bool> HasDetachReceiptAsync(Guid operationId, CancellationToken ct = default) => store.HasDetachReceiptAsync(operationId, ct);
    public Task<BranchDeletionPreview> PreviewDeleteAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken ct = default)
    {
        if (genealogyId == Guid.Empty || lineageId == Guid.Empty || personId == Guid.Empty || placementId == Guid.Empty) throw new ArgumentException("删除目标无效。");
        return store.PreviewDeleteAsync(genealogyId, lineageId, personId, placementId, ct);
    }
    public Task DeleteAsync(DeleteBranch command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.PersonId == Guid.Empty || command.PlacementId == Guid.Empty || string.IsNullOrWhiteSpace(command.ExpectedEvidence)) throw new ArgumentException("删除操作上下文无效。");
        return store.DeleteAsync(command, ct);
    }
    public Task<bool> HasDeleteReceiptAsync(Guid operationId, CancellationToken ct = default) => store.HasDeleteReceiptAsync(operationId, ct);
    public Task<SpouseDeletionPreview> PreviewSpouseAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid fatherPlacementId, Guid spouseId, Guid unionId, CancellationToken ct = default)
    {
        if (genealogyId == Guid.Empty || lineageId == Guid.Empty || fatherId == Guid.Empty || fatherPlacementId == Guid.Empty || spouseId == Guid.Empty || unionId == Guid.Empty) throw new ArgumentException("配偶删除目标无效。");
        return store.PreviewSpouseAsync(genealogyId, lineageId, fatherId, fatherPlacementId, spouseId, unionId, ct);
    }
    public Task DeleteSpouseAsync(DeleteSpouse command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.FatherId == Guid.Empty || command.FatherPlacementId == Guid.Empty || command.SpouseId == Guid.Empty || command.UnionId == Guid.Empty || string.IsNullOrWhiteSpace(command.ExpectedEvidence)) throw new ArgumentException("配偶删除上下文无效。");
        return store.DeleteSpouseAsync(command, ct);
    }
    public Task<bool> HasSpouseDeleteReceiptAsync(Guid operationId, CancellationToken ct = default) => store.HasSpouseDeleteReceiptAsync(operationId, ct);
    public Task<RootParentInfo> ReadRootParentsAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken ct = default)
    {
        if (genealogyId == Guid.Empty || lineageId == Guid.Empty || personId == Guid.Empty || placementId == Guid.Empty) throw new ArgumentException("人物位置无效。");
        return store.ReadRootParentsAsync(genealogyId, lineageId, personId, placementId, ct);
    }
    public Task UpdateRootParentsAsync(UpdateRootParents command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.PersonId == Guid.Empty || command.PlacementId == Guid.Empty || string.IsNullOrWhiteSpace(command.ExpectedEvidence)) throw new ArgumentException("首节点父母资料无效。");
        return store.UpdateRootParentsAsync(command, ct);
    }
    public Task<bool> HasRootParentsReceiptAsync(Guid operationId, CancellationToken ct = default) => store.HasRootParentsReceiptAsync(operationId, ct);
}
