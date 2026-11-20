using PuJiang.Domain;
namespace PuJiang.V1.Application;

public sealed record AddChild(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid ParentPersonId, Guid ParentPlacementId, RootNameMode NameMode, string Name, long ExpectedSpouseRevision = 0, int? Generation = null, int? ExpectedParentGeneration = null);
public sealed record ChildCreated(Guid PersonId, Guid PlacementId, bool Replayed);
public enum ChildPositionKind { Ordinary, BirthReference, Succession }
public sealed record ChildNode(Guid PersonId, Guid PlacementId, Guid ParentPlacementId, string Name, int Position, Guid FatherRelationshipId, Guid? MotherId, bool MotherExplicitlyCleared, PersonSex Sex, int? Generation = null, ChildPositionKind PositionKind = ChildPositionKind.Ordinary, Guid? SuccessionId = null);
public sealed record AddSibling(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid SourcePersonId, Guid SourcePlacementId, Guid ParentPersonId, Guid ParentPlacementId, Guid ExpectedFatherRelationshipId, Guid? ExpectedMotherId, bool ExpectedMotherCleared, string Name, int? Generation = null, int? ExpectedParentGeneration = null);
public interface IChildStore
{
    Task<ChildCreated> AddAsync(AddChild command, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildNode>> ReadAsync(Guid genealogyId, Guid lineageId, CancellationToken cancellationToken);
    Task<bool> HasReceiptAsync(Guid operationId, CancellationToken cancellationToken);
    Task<ChildCreated> AddSiblingAsync(AddSibling command, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task<bool> HasSiblingReceiptAsync(Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
public sealed class ChildApplication(IChildStore store)
{
    public Task<bool> HasReceiptAsync(Guid operationId, CancellationToken cancellationToken = default) => store.HasReceiptAsync(operationId, cancellationToken);
    public Task<bool> HasSiblingReceiptAsync(Guid operationId, CancellationToken cancellationToken = default) => store.HasSiblingReceiptAsync(operationId, cancellationToken);
    public Task<ChildCreated> ExecuteSiblingAsync(AddSibling command, CancellationToken cancellationToken = default)
    {
        ValidateSibling(command);
        return store.AddSiblingAsync(command, cancellationToken);
    }
    public static void ValidateSibling(AddSibling command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty || command.SourcePersonId == Guid.Empty || command.SourcePlacementId == Guid.Empty || command.ParentPersonId == Guid.Empty || command.ParentPlacementId == Guid.Empty || command.ExpectedFatherRelationshipId == Guid.Empty || command.ExpectedMotherId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || command.Generation is null or <= 0 || command.ExpectedParentGeneration is null or <= 0 || (long)command.Generation != (long)command.ExpectedParentGeneration + 1)
            throw new ArgumentException("兄弟姐妹录入或位置无效。");
    }
    public Task<ChildCreated> ExecuteAsync(AddChild command, CancellationToken cancellationToken = default)
    {
        Validate(command);
        return store.AddAsync(command, cancellationToken);
    }
    public static void Validate(AddChild command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ParentPersonId == Guid.Empty || command.ParentPlacementId == Guid.Empty || command.ExpectedSpouseRevision < 0) throw new ArgumentException("父亲、父位置或排序版本无效。");
        FirstRootApplication.ValidateName(new(command.OperationId, command.GenealogyId, command.LineageId, command.NameMode, command.Name));
        if (command.Generation is null or <= 0 || command.ExpectedParentGeneration is null or <= 0 || (long)command.Generation != (long)command.ExpectedParentGeneration + 1) throw new ArgumentException("普通子女世代必须为父亲的下一世。");
    }
    public Task<IReadOnlyList<ChildNode>> ReadAsync(Guid genealogyId, Guid lineageId, CancellationToken cancellationToken = default)
    {
        if (genealogyId == Guid.Empty || lineageId == Guid.Empty) throw new ArgumentException("目标身份不能为空。");
        return store.ReadAsync(genealogyId, lineageId, cancellationToken);
    }
}
