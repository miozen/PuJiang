namespace PuJiang.Tests;

// 仅验证拟议停用协议；尚未接入任何应用入口，不能作为生产恢复安全保证。
internal sealed class DatabaseQuiescencePrototype
{
    private readonly object _sync = new();
    private readonly List<Func<ValueTask>> _closeOwners = [];
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _state = "running";
    private int _active;

    public string State { get { lock (_sync) return _state; } }

    public void RegisterOwner(Func<ValueTask> close)
    {
        lock (_sync)
        {
            if (_state != "running") throw new InvalidOperationException("数据库停用中，不能注册连接持有者。");
            _closeOwners.Add(close);
        }
    }

    public IDisposable EnterOperation()
    {
        lock (_sync)
        {
            if (_state != "running") throw new InvalidOperationException("数据库正在停用，不能开始新操作。");
            _active++;
            return new Operation(this);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state != "running") throw new InvalidOperationException("停用已开始或失败，不能重新进入。");
            _state = "draining";
            if (_active == 0) _drained.TrySetResult();
        }
        try
        {
            await _drained.Task.WaitAsync(cancellationToken);
            foreach (var close in _closeOwners) await close();
            lock (_sync) _state = "stopped";
        }
        catch
        {
            lock (_sync) _state = "failed";
            throw;
        }
    }

    private void Exit()
    {
        lock (_sync)
        {
            _active--;
            if (_state == "draining" && _active == 0) _drained.TrySetResult();
        }
    }

    private sealed class Operation(DatabaseQuiescencePrototype owner) : IDisposable
    {
        private DatabaseQuiescencePrototype? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
}
