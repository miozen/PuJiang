namespace PuJiang.V1.Application;

public sealed record AddSpouse(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid FatherPersonId, Guid FatherPlacementId, RootNameMode NameMode, string Name);
public sealed record SpouseCreated(Guid PersonId, Guid UnionId, bool Replayed);
public sealed record SpouseNode(Guid PersonId, Guid UnionId, string Name, int Position);
public sealed record MotherCandidate(Guid PersonId, string Name);
public sealed record SetChildMother(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid FatherPersonId, Guid FatherPlacementId, Guid ChildPersonId, Guid? MotherId, Guid? ExpectedMotherId, bool ExpectedCleared);
public interface ISpouseStore
{
    Task<SpouseCreated> AddAsync(AddSpouse command, CancellationToken cancellationToken);
    Task SetMotherAsync(SetChildMother command, CancellationToken cancellationToken);
    Task<IReadOnlyList<SpouseNode>> ReadAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MotherCandidate>> ReadMotherCandidatesAsync(Guid genealogyId, Guid lineageId, Guid fatherId, Guid placementId, Guid childId, CancellationToken cancellationToken);
    Task<bool> HasReceiptAsync(Guid operationId, bool motherOperation, CancellationToken cancellationToken);
}
public sealed class SpouseApplication(ISpouseStore store)
{
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
