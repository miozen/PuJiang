namespace PuJiang.V1.Application;

public sealed record SuccessionPreview(string ChildName, string BirthFatherName, string HeirFatherName, string? HeirMotherName, int DescendantCount, string Evidence);
public sealed record CreateSuccession(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid ChildId, Guid BirthPlacementId, Guid HeirFatherPlacementId, string ExpectedEvidence);
public sealed record SuccessionCreated(Guid SuccessionId, Guid HeirPlacementId, bool Replayed);
public sealed record SetHeirMother(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid SuccessionId, Guid? MotherId, Guid? ExpectedMotherId, bool ExpectedCleared, string ExpectedEvidence);
public sealed record SuccessionInfo(Guid Id, Guid ChildId, Guid BirthPlacementId, Guid HeirPlacementId, Guid HeirFatherId, Guid HeirFatherPlacementId, string? BirthFatherName, string? BirthMotherName, Guid? MotherId, string? MotherName, bool MotherCleared, IReadOnlyList<MotherCandidate> Candidates, string Evidence);
public interface ISuccessionStore
{
    Task<SuccessionPreview> PreviewAsync(Guid g, Guid l, Guid child, Guid birthPlace, Guid heirFatherPlace, CancellationToken ct);
    Task<SuccessionCreated> CreateAsync(CreateSuccession command, CancellationToken ct);
    Task<bool> HasReceiptAsync(Guid operationId, CancellationToken ct);
    Task<SuccessionInfo?> ReadAsync(Guid g, Guid l, Guid placement, CancellationToken ct);
    Task SetMotherAsync(SetHeirMother command, CancellationToken ct);
    Task<bool> HasMotherReceiptAsync(Guid operationId, CancellationToken ct);
}
public sealed class SuccessionApplication(ISuccessionStore store)
{
    public Task<SuccessionPreview> PreviewAsync(Guid g, Guid l, Guid child, Guid source, Guid target, CancellationToken ct=default)
    {
        if (g==Guid.Empty || l==Guid.Empty || child==Guid.Empty || source==Guid.Empty || target==Guid.Empty || source==target) throw new ArgumentException("承嗣位置无效。");
        return store.PreviewAsync(g,l,child,source,target,ct);
    }
    public Task<SuccessionCreated> CreateAsync(CreateSuccession c, CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(c);
        if(c.OperationId==Guid.Empty || c.GenealogyId==Guid.Empty || c.LineageId==Guid.Empty || c.ChildId==Guid.Empty || c.BirthPlacementId==Guid.Empty || c.HeirFatherPlacementId==Guid.Empty || c.BirthPlacementId==c.HeirFatherPlacementId || string.IsNullOrWhiteSpace(c.ExpectedEvidence)) throw new ArgumentException("承嗣上下文无效。");
        return store.CreateAsync(c,ct);
    }
    public Task<bool> HasReceiptAsync(Guid id,CancellationToken ct=default) => store.HasReceiptAsync(id,ct);
    public Task<SuccessionInfo?> ReadAsync(Guid g,Guid l,Guid place,CancellationToken ct=default) => store.ReadAsync(g,l,place,ct);
    public Task SetMotherAsync(SetHeirMother c,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(c);
        if(c.OperationId==Guid.Empty || c.GenealogyId==Guid.Empty || c.LineageId==Guid.Empty || c.SuccessionId==Guid.Empty || c.MotherId==Guid.Empty || c.ExpectedMotherId==Guid.Empty || string.IsNullOrWhiteSpace(c.ExpectedEvidence)) throw new ArgumentException("嗣母操作无效。");
        return store.SetMotherAsync(c,ct);
    }
    public Task<bool> HasMotherReceiptAsync(Guid id,CancellationToken ct=default) => store.HasMotherReceiptAsync(id,ct);
}
