using PuJiang.Domain;
namespace PuJiang.V1.Application;

public sealed record ChildOrderItem(Guid PersonId, Guid PlacementId, PersonSex Sex, int Position);
public sealed record ChildOrderSnapshot(Guid ParentPlacementId, long Revision, IReadOnlyList<ChildOrderItem> Items, string Evidence);
public sealed record MoveChildOrder(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid ParentPlacementId, Guid SourcePlacementId, Guid TargetPlacementId, long ExpectedRevision, string ExpectedEvidence);
public interface IChildOrderStore
{
    Task<ChildOrderSnapshot> ReadAsync(Guid genealogyId, Guid lineageId, Guid parentPlacementId, CancellationToken ct);
    Task MoveAsync(MoveChildOrder command, CancellationToken ct);
    Task<bool> HasReceiptAsync(Guid operationId, CancellationToken ct);
}
public sealed class ChildOrderApplication(IChildOrderStore store)
{
    public Task<ChildOrderSnapshot> ReadAsync(Guid g, Guid l, Guid parent, CancellationToken ct = default)
    { if (g == Guid.Empty || l == Guid.Empty || parent == Guid.Empty) throw new ArgumentException("谱系或父位置无效。"); return store.ReadAsync(g, l, parent, ct); }
    public Task MoveAsync(MoveChildOrder c, CancellationToken ct = default)
    { ArgumentNullException.ThrowIfNull(c); if(c.OperationId==Guid.Empty || c.GenealogyId==Guid.Empty || c.LineageId==Guid.Empty || c.ParentPlacementId==Guid.Empty || c.SourcePlacementId==Guid.Empty || c.TargetPlacementId==Guid.Empty || c.SourcePlacementId==c.TargetPlacementId || c.ExpectedRevision<0 || string.IsNullOrWhiteSpace(c.ExpectedEvidence)) throw new ArgumentException("排行操作无效。"); return store.MoveAsync(c,ct); }
    public Task<bool> HasReceiptAsync(Guid id, CancellationToken ct = default) => store.HasReceiptAsync(id,ct);
}
