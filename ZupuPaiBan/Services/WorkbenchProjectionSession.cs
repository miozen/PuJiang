using ZupuPaiBan.Data;

namespace ZupuPaiBan.Services;

/// <summary>Owns a replaceable read-only projection session for one genealogy.</summary>
public sealed class WorkbenchProjectionSession(TargetContextCoordinator coordinator, Guid genealogyId) : IAsyncDisposable
{
    public sealed record PeopleResult(long Revision, ProjectionReadSession.PersonPage Page);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProjectionReadSession? _session;
    private long _revision;
    private long _readyRevision;
    private int _disposed;
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    internal Func<Task>? BeforeReadForTest { get; set; }

    public bool IsCurrent(long revision) => revision != 0 && Volatile.Read(ref _disposed) == 0 &&
        Interlocked.Read(ref _revision) == revision && Interlocked.Read(ref _readyRevision) == revision;

    /// <summary>Immediately invalidates outstanding results; a failed reopen leaves no usable session.</summary>
    public async Task<long> ReopenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var revision = Interlocked.Increment(ref _revision);
        Interlocked.Exchange(ref _readyRevision, 0);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Interlocked.Read(ref _revision) != revision) throw new InvalidOperationException("读取请求已被更新的请求取代。");
            using var operation = coordinator.EnterOperation();
            var old = _session; _session = null;
            if (old is not null) await old.DisposeAsync();
            var next = await ProjectionReadSession.OpenAsync(coordinator, genealogyId, cancellationToken);
            if (Volatile.Read(ref _disposed) != 0 || Interlocked.Read(ref _revision) != revision)
            {
                await next.DisposeAsync();
                throw new InvalidOperationException("读取请求已失效。");
            }
            _session = next;
            Interlocked.Exchange(ref _readyRevision, revision);
            return revision;
        }
        finally { _gate.Release(); }
    }

    public async Task<PeopleResult> ReadPeopleAsync(long revision, Guid? after, int limit, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrent(revision) || _session is null) throw new InvalidOperationException("读取会话已失效，请重新打开。");
            using var operation = coordinator.EnterOperation();
            try
            {
                if (BeforeReadForTest is not null) await BeforeReadForTest();
                var page = await _session.ReadPeopleAsync(_session.Token, after, limit, cancellationToken);
                if (!IsCurrent(revision)) throw new InvalidOperationException("读取结果已失效。");
                return new PeopleResult(revision, page);
            }
            catch
            {
                Interlocked.CompareExchange(ref _readyRevision, 0, revision);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task ValidateAsync(long revision, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrent(revision) || _session is null) throw new InvalidOperationException("读取会话已失效，请重新打开。");
            using var operation = coordinator.EnterOperation();
            try
            {
                await _session.ValidateAsync(_session.Token, cancellationToken);
                if (!IsCurrent(revision)) throw new InvalidOperationException("读取结果已失效。");
            }
            catch { Interlocked.CompareExchange(ref _readyRevision, 0, revision); throw; }
        }
        finally { _gate.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Interlocked.Increment(ref _revision); Interlocked.Exchange(ref _readyRevision, 0);
        await _gate.WaitAsync();
        try { if (_session is not null) { await _session.DisposeAsync(); _session = null; } }
        finally { _gate.Release(); }
    }
}
