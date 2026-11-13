namespace PuJiang.V1.Application;

public enum RootNameMode { Named = 1, Empty = 2 }
public sealed record AddFirstRoot(Guid OperationId, Guid GenealogyId, Guid LineageId, RootNameMode NameMode, string Name);
public sealed record RootCreated(Guid PersonId, Guid PlacementId, bool Replayed);
public sealed record RootNode(Guid PersonId, Guid PlacementId, string Name);
public sealed record RootProjection(Guid GenealogyId, Guid LineageId, IReadOnlyList<RootNode> Nodes);

// Implementations own the transaction and must persist the operation receipt atomically with the entities.
public interface IFirstRootStore
{
    Task<RootCreated> AddFirstAsync(AddFirstRoot command, CancellationToken cancellationToken);
    Task<RootProjection> ReadRootsAsync(Guid genealogyId, Guid lineageId, CancellationToken cancellationToken);
}

public sealed class FirstRootApplication(IFirstRootStore store)
{
    public async Task<RootCreated> ExecuteAsync(AddFirstRoot command, CancellationToken cancellationToken = default)
    {
        Validate(command);
        return await store.AddFirstAsync(command, cancellationToken);
    }
    public static void Validate(AddFirstRoot command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty)
            throw new ArgumentException("操作及目标身份不能为空。");
        if (command.Name is null || !Enum.IsDefined(command.NameMode) ||
            (command.NameMode == RootNameMode.Named && string.IsNullOrWhiteSpace(command.Name)) ||
            (command.NameMode == RootNameMode.Empty && command.Name != ""))
            throw new ArgumentException("姓名与新增方式不匹配。");
    }
    public Task<RootProjection> ReadAsync(Guid genealogyId, Guid lineageId, CancellationToken cancellationToken = default)
    {
        if (genealogyId == Guid.Empty || lineageId == Guid.Empty) throw new ArgumentException("目标身份不能为空。");
        return store.ReadRootsAsync(genealogyId, lineageId, cancellationToken);
    }
}
