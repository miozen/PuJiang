using Microsoft.EntityFrameworkCore;

namespace ZupuPaiBan.Data;

/// <summary>
/// Tracks only target-model contexts created through this coordinator. Stopping it
/// does not quiesce legacy, migration, backup, or external database connections.
/// </summary>
public sealed class TargetContextCoordinator
{
    private readonly object _sync = new();
    private readonly DbContextOptions<ZupuDbContext> _options;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AsyncLocal<Operation?> _currentOperation = new();
    private int _active;
    private int _operations;
    private State _state = State.Running;

    private enum State { Running, Draining, Stopped, Failed }

    public TargetContextCoordinator(DbContextOptions<ZupuDbContext> options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public ZupuDbContext CreateContext()
    {
        lock (_sync)
        {
            if (_state != State.Running && !(_state == State.Draining && IsCurrentOperationActive()))
                throw new InvalidOperationException("目标族谱数据库正在停用，不能开始新操作。");
            _active++;
        }

        try { return new ZupuDbContext(_options, Release); }
        catch { Release(false); throw; }
    }

    /// <summary>Registers a logical operation. Nested calls reuse its permission during draining.</summary>
    public IDisposable EnterOperation()
    {
        lock (_sync)
        {
            var parent = _currentOperation.Value;
            if (_state != State.Running && !(_state == State.Draining && IsCurrentOperationActive()))
                throw new InvalidOperationException("目标族谱数据库正在停用，不能开始新操作。");
            var operation = new Operation(this, parent);
            _operations++;
            _currentOperation.Value = operation;
            return operation;
        }
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (IsCurrentOperationActive())
                throw new InvalidOperationException("不能在已登记的目标业务操作内等待自身排空。");
            if (_state != State.Running)
                throw new InvalidOperationException("目标族谱数据库停用已开始或失败。");
            _state = State.Draining;
            SignalDrainedIfReady();
        }

        try
        {
            await _drained.Task.WaitAsync(cancellationToken);
            lock (_sync)
            {
                if (_state == State.Failed)
                    throw new InvalidOperationException("目标数据库上下文释放失败，不能继续停用。");
                _state = State.Stopped;
            }
        }
        catch
        {
            lock (_sync) _state = State.Failed;
            throw;
        }
    }

    private void Release(bool failed)
    {
        lock (_sync)
        {
            if (failed) _state = State.Failed;
            _active--;
            SignalDrainedIfReady();
        }
    }

    private bool IsCurrentOperationActive() => _currentOperation.Value is { IsActive: true };

    private void SignalDrainedIfReady()
    {
        if (_active == 0 && _operations == 0 && _state is State.Draining or State.Failed)
            _drained.TrySetResult();
    }

    private sealed class Operation(TargetContextCoordinator owner, Operation? parent) : IDisposable
    {
        private int _released;
        public bool IsActive => Volatile.Read(ref _released) == 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            lock (owner._sync)
            {
                if (ReferenceEquals(owner._currentOperation.Value, this))
                    owner._currentOperation.Value = parent;
                owner._operations--;
                owner.SignalDrainedIfReady();
            }
        }
    }

}
