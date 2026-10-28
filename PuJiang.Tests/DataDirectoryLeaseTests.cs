using System.Diagnostics;
using System.Text;
using Xunit;
using ZupuPaiBan.Infrastructure;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class DataDirectoryLeaseTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Independent_process_is_blocked_until_lease_is_released()
    {
        await using var database = new TestDatabase();
        var root = Path.GetDirectoryName(database.DatabasePath)!;
        using (DataDirectoryLease.Acquire(root))
        {
            Assert.Throws<InvalidOperationException>(() => DataDirectoryLease.Acquire(root));
            Assert.Equal(7, await TryOpenFromAnotherProcessAsync(root));
        }

        Assert.Equal(0, await TryOpenFromAnotherProcessAsync(root));
        using var reacquired = DataDirectoryLease.Acquire(root);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Different_data_roots_can_be_used_together()
    {
        await using var first = new TestDatabase();
        await using var second = new TestDatabase();
        using var firstLease = DataDirectoryLease.Acquire(Path.GetDirectoryName(first.DatabasePath)!);
        using var secondLease = DataDirectoryLease.Acquire(Path.GetDirectoryName(second.DatabasePath)!);
    }

    private static async Task<int> TryOpenFromAnotherProcessAsync(string root)
    {
        const string script = "try { $s = [System.IO.File]::Open($env:PUJIANG_LEASE_TEST_FILE, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None); $s.Dispose(); exit 0 } catch [System.IO.IOException] { exit 7 }";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        start.Environment["PUJIANG_LEASE_TEST_FILE"] = Path.Combine(root, ".pujiang.lock");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动锁测试进程。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        return process.ExitCode;
    }
}
