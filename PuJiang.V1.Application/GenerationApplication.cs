namespace PuJiang.V1.Application;

public sealed record GenerationSnapshot(Guid PersonId, Guid PlacementId, int Generation, string Evidence);
public sealed record UpdateGeneration(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid PersonId, Guid PlacementId, int Generation, string ExpectedEvidence);

public interface IGenerationStore
{
    Task<GenerationSnapshot> ReadAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken cancellationToken);
    Task UpdateAsync(UpdateGeneration command, CancellationToken cancellationToken);
    Task<bool> HasReceiptAsync(Guid operationId, CancellationToken cancellationToken);
}

public sealed class GenerationApplication(IGenerationStore store)
{
    public Task<GenerationSnapshot> ReadAsync(Guid genealogyId, Guid lineageId, Guid personId, Guid placementId, CancellationToken ct = default)
    {
        if (genealogyId == Guid.Empty || lineageId == Guid.Empty || personId == Guid.Empty || placementId == Guid.Empty) throw new ArgumentException("人物或位置无效。");
        return store.ReadAsync(genealogyId, lineageId, personId, placementId, ct);
    }
    public Task UpdateAsync(UpdateGeneration command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.PersonId == Guid.Empty || command.PlacementId == Guid.Empty || command.Generation <= 0 || string.IsNullOrEmpty(command.ExpectedEvidence)) throw new ArgumentException("世代或操作目标无效。");
        return store.UpdateAsync(command, ct);
    }
    public Task<bool> HasReceiptAsync(Guid operationId, CancellationToken ct = default) => store.HasReceiptAsync(operationId, ct);
}
