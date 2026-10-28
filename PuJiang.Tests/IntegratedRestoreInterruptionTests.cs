using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class IntegratedRestoreInterruptionTests
{
    [Theory]
    [InlineData("prepared-flushed", "unknown", false)]
    [InlineData("prepared-written", "before_replace", false)]
    [InlineData("pending-flushed", "unknown", false)]
    [InlineData("pending-written", "uncertain", false)]
    [InlineData("replace-completed", "uncertain", true)]
    [InlineData("replaced-flushed", "unknown", true)]
    [InlineData("replaced-written", "after_replace", true)]
    [Trait("Category", "Integration")]
    public async Task Real_coordinator_child_interruption_preserves_evidence(string pauseStage, string expectedStatus, bool replaced)
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await target.MigrateAsync();
        await source.MigrateAsync();
        await using (var context = target.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "原数据" });
        await using (var context = source.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "备份数据" });
        Directory.CreateDirectory(source.BackupDirectory);
        await OfflineBackupPackageService.CreateAsync(source.DatabasePath, Path.Combine(source.BackupDirectory, "source.zip"));
        var root = Path.GetDirectoryName(target.DatabasePath)!;
        var sourceRoot = Path.GetDirectoryName(source.DatabasePath)!;
        var original = await File.ReadAllBytesAsync(target.DatabasePath);

        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add(typeof(RestoreWorkerProgram).Assembly.Location);
        start.ArgumentList.Add(root);
        start.ArgumentList.Add(sourceRoot);
        start.ArgumentList.Add(pauseStage);
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("无法启动测试恢复子进程。");
        try
        {
            var ready = await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if (ready != $"READY:{pauseStage}")
                throw new InvalidOperationException($"测试子进程未到达边界：{ready}；{await worker.StandardError.ReadToEndAsync()}");
            worker.Kill();
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { if (!worker.HasExited) { worker.Kill(); await worker.WaitForExitAsync(); } }

        string originalPath;
        // Windows 在子进程终止后可能短暂延迟释放句柄；有界等待，超时仍视为失败。
        DataDirectoryLease? acquired = null;
        for (var attempt = 0; attempt < 50 && acquired is null; attempt++)
        {
            try { acquired = DataDirectoryLease.Acquire(root); }
            catch (InvalidOperationException) when (attempt < 49) { await Task.Delay(100); }
        }
        using (var lease = acquired!)
        {
            var filesBefore = Directory.GetFiles(root).Where(x => Path.GetFileName(x) != ".pujiang.lock").OrderBy(x => x).ToArray();
            var contentBefore = filesBefore.ToDictionary(x => x, x => SHA256.HashData(File.ReadAllBytes(x)));
            var inspection = RestoreJournalPrototype.Inspect(target);
            Assert.Equal(expectedStatus, inspection.Status);
            Assert.Contains("人工检查", inspection.Message);
            Assert.Equal(filesBefore, Directory.GetFiles(root).Where(x => Path.GetFileName(x) != ".pujiang.lock").OrderBy(x => x).ToArray());
            foreach (var file in filesBefore) Assert.Equal(contentBefore[file], SHA256.HashData(await File.ReadAllBytesAsync(file)));
            var rollback = Directory.GetFiles(root, "rollback-*.db");
            var staged = Directory.GetFiles(root, "staged-*.db");
            Assert.Equal(replaced, rollback.Length == 1);
            Assert.Equal(!replaced, staged.Length == 1);
            originalPath = replaced ? rollback[0] : target.DatabasePath;
            Assert.Equal(original, await File.ReadAllBytesAsync(originalPath));
            Assert.Single(Directory.GetFiles(target.BackupDirectory, "before-*.db"));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => new IsolatedRestorePrototype(target, () => Task.CompletedTask)
            .RunAsync(Path.Combine(source.BackupDirectory, "source.zip")));
        Assert.Equal(original, await File.ReadAllBytesAsync(originalPath));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Corrupt_journal_blocks_coordinator_without_changing_files()
    {
        await using var target = new TestDatabase();
        await using var source = new TestDatabase();
        await target.MigrateAsync();
        await source.MigrateAsync();
        Directory.CreateDirectory(source.BackupDirectory);
        var package = Path.Combine(source.BackupDirectory, "source.zip");
        await OfflineBackupPackageService.CreateAsync(source.DatabasePath, package);
        var root = Path.GetDirectoryName(target.DatabasePath)!;
        var journal = Path.Combine(root, RestoreJournalPrototype.JournalName);
        await File.WriteAllTextAsync(journal, "broken");
        var before = await File.ReadAllBytesAsync(target.DatabasePath);
        var inspection = RestoreJournalPrototype.Inspect(target);
        Assert.Equal("unknown", inspection.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new IsolatedRestorePrototype(target, () => Task.CompletedTask).RunAsync(package));
        Assert.Equal("broken", await File.ReadAllTextAsync(journal));
        Assert.Equal(before, await File.ReadAllBytesAsync(target.DatabasePath));
        Assert.Empty(Directory.GetFiles(root, "rollback-*.db"));
    }
}
