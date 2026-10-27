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
    private int _active;
    private State _state = State.Running;

    private enum State { Running, Draining, Stopped, Failed }

    public TargetContextCoordinator(DbContextOptions<ZupuDbContext> options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public ZupuDbContext CreateContext()
    {
        lock (_sync)
        {
            if (_state != State.Running)
                throw new InvalidOperationException("目标族谱数据库正在停用，不能开始新操作。");
            _active++;
        }

        try { return new RegisteredContext(_options, this); }
        catch { Release(false); throw; }
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state != State.Running)
                throw new InvalidOperationException("目标族谱数据库停用已开始或失败。");
            _state = State.Draining;
            if (_active == 0) _drained.TrySetResult();
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
            if (_active == 0 && _state is State.Draining or State.Failed)
                _drained.TrySetResult();
        }
    }

    private sealed class RegisteredContext(DbContextOptions<ZupuDbContext> options, TargetContextCoordinator owner)
        : ZupuDbContext(options)
    {
        private int _released;

        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            var failed = true;
            try { base.Dispose(); failed = false; }
            finally { owner.Release(failed); }
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            var failed = true;
            try { await base.DisposeAsync(); failed = false; }
            finally { owner.Release(failed); }
        }
    }
}
