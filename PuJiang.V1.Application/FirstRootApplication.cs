namespace PuJiang.V1.Application;

public enum RootNameMode { Named = 1, Empty = 2 }
public sealed record AddFirstRoot(Guid OperationId, Guid GenealogyId, Guid LineageId, RootNameMode NameMode, string Name, int? Generation = null, string Title = "", string Notes = "");
public sealed record RootCreated(Guid PersonId, Guid PlacementId, bool Replayed);
public sealed record RootNode(Guid PersonId, Guid PlacementId, string Name, int? Generation = null, string Title = "", string Notes = "");
public sealed record UpdateRootDetails(Guid OperationId, Guid GenealogyId, Guid LineageId, Guid PersonId, Guid PlacementId, int? Generation, string Title, string Notes, int? ExpectedGeneration, string ExpectedTitle, string ExpectedNotes);
public sealed record RootProjection(Guid GenealogyId, Guid LineageId, IReadOnlyList<RootNode> Nodes);

// Implementations own the transaction and must persist the operation receipt atomically with the entities.
public interface IFirstRootStore
{
    Task<RootCreated> AddFirstAsync(AddFirstRoot command, CancellationToken cancellationToken);
    Task<RootProjection> ReadRootsAsync(Guid genealogyId, Guid lineageId, CancellationToken cancellationToken);
    Task UpdateDetailsAsync(UpdateRootDetails command, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task<bool> HasDetailsReceiptAsync(Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
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
        ValidateName(command);
        if (command.Generation is null or <= 0) throw new ArgumentException("首节点主世系必须为正整数。");
    }
    public static void ValidateName(AddFirstRoot command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.GenealogyId == Guid.Empty || command.LineageId == Guid.Empty)
            throw new ArgumentException("操作及目标身份不能为空。");
        if (string.IsNullOrWhiteSpace(command.Name) || command.Title is null || command.Notes is null || command.NameMode != RootNameMode.Named)
            throw new ArgumentException("姓名与新增方式不匹配。");
    }
    public Task<RootProjection> ReadAsync(Guid genealogyId, Guid lineageId, CancellationToken cancellationToken = default)
    {
        if (genealogyId == Guid.Empty || lineageId == Guid.Empty) throw new ArgumentException("目标身份不能为空。");
        return store.ReadRootsAsync(genealogyId, lineageId, cancellationToken);
    }
    public Task UpdateDetailsAsync(UpdateRootDetails command, CancellationToken ct=default)
    { if(command.OperationId==Guid.Empty||command.GenealogyId==Guid.Empty||command.LineageId==Guid.Empty||command.PersonId==Guid.Empty||command.PlacementId==Guid.Empty||command.Generation is <=0||command.Title is null||command.Notes is null||command.ExpectedTitle is null||command.ExpectedNotes is null)throw new ArgumentException("首节点资料无效。"); return store.UpdateDetailsAsync(command,ct); }
    public Task<bool> HasDetailsReceiptAsync(Guid operation,CancellationToken ct=default)=>store.HasDetailsReceiptAsync(operation,ct);
}
