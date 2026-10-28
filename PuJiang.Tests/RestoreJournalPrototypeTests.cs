using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class RestoreJournalPrototypeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Interrupted_process_before_or_after_replace_keeps_ambiguous_record_and_files(bool replaceBeforeStop)
    {
        await using var target = new TestDatabase();
        await using var incoming = new TestDatabase();
        await target.MigrateAsync();
        await incoming.MigrateAsync();
        await using (var context = target.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "原数据" });
        await using (var context = incoming.CreateContext())
            await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "备份数据" });
        var root = Path.GetDirectoryName(target.DatabasePath)!;
        var id = Guid.NewGuid().ToString("N");
        var staged = Path.Combine(root, $"staged-{id}.db");
        var rollback = Path.Combine(root, $"rollback-{id}.db");
        await DatabaseSnapshotService.CreateAsync(incoming.DatabasePath, staged);
        RestoreJournalPrototype.Write(target, new(1, id, "replace_pending"));
        var expectedOriginal = await File.ReadAllBytesAsync(target.DatabasePath);
        var expectedIncoming = await File.ReadAllBytesAsync(staged);
        string Quote(string path) => "'" + path.Replace("'", "''") + "'";
        var replace = replaceBeforeStop ? $"[System.IO.File]::Replace({Quote(staged)}, {Quote(target.DatabasePath)}, {Quote(rollback)}); " : "";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(replace + "[Console]::Out.WriteLine('READY'); [Console]::Out.Flush(); [Console]::In.ReadLine() | Out-Null");
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("无法启动中断测试进程。");
        try
        {
            Assert.Equal("READY", await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            worker.Kill();
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            if (!worker.HasExited) { worker.Kill(); await worker.WaitForExitAsync(); }
        }

        var inspection = RestoreJournalPrototype.Inspect(target);
        Assert.Equal("uncertain", inspection.Status);
        Assert.Contains("人工检查", inspection.Message);
        Assert.Equal(replaceBeforeStop ? expectedIncoming : expectedOriginal, await File.ReadAllBytesAsync(target.DatabasePath));
        Assert.Equal(replaceBeforeStop ? expectedOriginal : expectedIncoming, await File.ReadAllBytesAsync(replaceBeforeStop ? rollback : staged));
        Assert.Contains(replaceBeforeStop ? rollback : staged, inspection.Evidence);
        Assert.True(File.Exists(Path.Combine(root, RestoreJournalPrototype.JournalName)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Recognizes_prepared_and_replaced_only_with_consistent_evidence()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var root = Path.GetDirectoryName(database.DatabasePath)!;
        var id = Guid.NewGuid().ToString("N");
        var staged = Path.Combine(root, $"staged-{id}.db");
        var rollback = Path.Combine(root, $"rollback-{id}.db");
        await DatabaseSnapshotService.CreateAsync(database.DatabasePath, staged);
        RestoreJournalPrototype.Write(database, new(1, id, "prepared"));
        Assert.Equal("before_replace", RestoreJournalPrototype.Inspect(database).Status);
        RestoreJournalPrototype.Write(database, new(1, id, "replaced"));
        Assert.Equal("unknown", RestoreJournalPrototype.Inspect(database).Status);
        File.Replace(staged, database.DatabasePath, rollback);
        var inspection = RestoreJournalPrototype.Inspect(database);
        Assert.Equal("after_replace", inspection.Status);
        Assert.Contains(rollback, inspection.Evidence);
        Assert.True(File.Exists(rollback));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Version\":99,\"OperationId\":\"00000000000000000000000000000000\",\"Phase\":\"replaced\"}")]
    [InlineData("{\"Version\":1,\"OperationId\":\"../outside\",\"Phase\":\"replaced\"}")]
    [Trait("Category", "Integration")]
    public async Task Invalid_record_is_reported_without_modifying_database_or_residue(string record)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var root = Path.GetDirectoryName(database.DatabasePath)!;
        var journal = Path.Combine(root, RestoreJournalPrototype.JournalName);
        await File.WriteAllTextAsync(journal, record);
        var before = await File.ReadAllBytesAsync(database.DatabasePath);
        var inspection = RestoreJournalPrototype.Inspect(database);
        Assert.Equal("unknown", inspection.Status);
        Assert.Contains("人工检查", inspection.Message);
        Assert.Equal(record, await File.ReadAllTextAsync(journal));
        Assert.Equal(before, await File.ReadAllBytesAsync(database.DatabasePath));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Missing_record_with_residue_is_not_treated_as_clean()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var root = Path.GetDirectoryName(database.DatabasePath)!;
        var staged = Path.Combine(root, $"staged-{Guid.NewGuid():N}.db");
        await DatabaseSnapshotService.CreateAsync(database.DatabasePath, staged);
        var inspection = RestoreJournalPrototype.Inspect(database);
        Assert.Equal("unknown", inspection.Status);
        Assert.Contains(staged, inspection.Evidence);
        Assert.True(File.Exists(staged));
    }
}
