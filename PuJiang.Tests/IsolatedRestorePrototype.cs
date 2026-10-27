using System.IO.Compression;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;

namespace PuJiang.Tests;

// 仅用于 TestDatabase 的隔离实验；连接停止由测试替身模拟，不支持生产使用或断电恢复。
internal sealed class IsolatedRestorePrototype
{
    private readonly TestDatabase _target;
    private readonly Func<Task> _stopConnections;
    private readonly Action<string> _fault;

    public IsolatedRestorePrototype(TestDatabase target, Func<Task> stopConnections, Action<string>? fault = null)
    {
        _target = target;
        _stopConnections = stopConnections;
        _fault = fault ?? (_ => { });
    }

    public async Task RunAsync(string sourcePackage)
    {
        var root = Path.GetDirectoryName(_target.DatabasePath)!;
        using var lease = DataDirectoryLease.Acquire(root);
        Directory.CreateDirectory(_target.BackupDirectory);
        var id = Guid.NewGuid().ToString("N");
        var fixedPackage = Path.Combine(root, $"package-{id}.zip");
        var snapshot = Path.Combine(_target.BackupDirectory, $"before-{id}.db");
        var staged = Path.Combine(root, $"staged-{id}.db");
        var rollback = Path.Combine(root, $"rollback-{id}.db");
        var rejected = Path.Combine(root, $"rejected-{id}.db");

        await using (var input = new FileStream(sourcePackage, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var output = new FileStream(fixedPackage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await input.CopyToAsync(output);
        await new BackupRestorePreflightService(_target.BackupDirectory).CheckAsync(fixedPackage);
        await _stopConnections();
        _fault("connections-stopped");
        if (File.Exists(_target.DatabasePath + "-wal") || File.Exists(_target.DatabasePath + "-shm"))
            throw new InvalidOperationException("检测到 WAL 或 SHM，停止恢复实验。");
        _fault("before-snapshot");
        await DatabaseSnapshotService.CreateAsync(_target.DatabasePath, snapshot);
        _fault("after-snapshot");
        using (var package = ZipFile.OpenRead(fixedPackage))
        await using (var source = package.GetEntry("database/pujiang.db")!.Open())
        await using (var target = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await source.CopyToAsync(target);
        _fault("before-replace");
        File.Replace(staged, _target.DatabasePath, rollback);
        try { _fault("after-replace"); }
        catch (Exception failure)
        {
            try
            {
                _fault("before-rollback");
                File.Replace(rollback, _target.DatabasePath, rejected);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException($"回滚失败；原文件：{rollback}；恢复前快照：{snapshot}；目标：{_target.DatabasePath}", failure, rollbackError);
            }
            throw new InvalidOperationException($"替换后检查失败，已回滚；恢复前快照：{snapshot}；失败版本：{rejected}", failure);
        }
    }
}
