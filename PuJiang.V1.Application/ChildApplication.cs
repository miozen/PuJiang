using PuJiang.Domain;
namespace PuJiang.V1.Application;

public sealed record AddChild(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid ParentPersonId, Guid ParentPlacementId, RootNameMode NameMode, string Name, long ExpectedSpouseRevision = 0);
public sealed record ChildCreated(Guid PersonId, Guid PlacementId, bool Replayed);
public sealed record ChildNode(Guid PersonId, Guid PlacementId, Guid ParentPlacementId, string Name, int Position, Guid FatherRelationshipId, Guid? MotherId, bool MotherExplicitlyCleared, PersonSex Sex);
public interface IChildStore
{
    Task<ChildCreated> AddAsync(AddChild command, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildNode>> ReadAsync(Guid genealogyId, Guid lineageId, CancellationToken cancellationToken);
    Task<bool> HasReceiptAsync(Guid operationId, CancellationToken cancellationToken);
}
public sealed class ChildApplication(IChildStore store)
{
    public Task<bool> HasReceiptAsync(Guid operationId, CancellationToken cancellationToken = default) => store.HasReceiptAsync(operationId, cancellationToken);
    public Task<ChildCreated> ExecuteAsync(AddChild command, CancellationToken cancellationToken = default)
    {
        Validate(command);
        return store.AddAsync(command, cancellationToken);
    }
    public static void Validate(AddChild command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ParentPersonId == Guid.Empty || command.ParentPlacementId == Guid.Empty || command.ExpectedSpouseRevision < 0) throw new ArgumentException("父亲、父位置或排序版本无效。");
        FirstRootApplication.Validate(new(command.OperationId, command.GenealogyId, command.LineageId, command.NameMode, command.Name));
    }
    public Task<IReadOnlyList<ChildNode>> ReadAsync(Guid genealogyId, Guid lineageId, CancellationToken cancellationToken = default)
    {
        if (genealogyId == Guid.Empty || lineageId == Guid.Empty) throw new ArgumentException("目标身份不能为空。");
        return store.ReadAsync(genealogyId, lineageId, cancellationToken);
    }
}
