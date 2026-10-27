using System.Data.Common;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ZupuPaiBan;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

// This worker is invoked only with an existing GUID-named test root.
internal static class WpfInflightWorker
{
    public static async Task<int> RunAsync(string directory)
    {
        var database = new TestDatabase(directory);
        Environment.SetEnvironmentVariable(AppDataPaths.TestRootEnvironmentVariable, directory);
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await VerifyAsync(database, directory); result.TrySetResult(0); }
                catch (Exception ex) { Console.Error.WriteLine(ex); result.TrySetResult(1); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await result.Task;
    }

    private static async Task VerifyAsync(TestDatabase database, string root)
    {
        Directory.CreateDirectory(database.BackupDirectory);
        using var lease = DataDirectoryLease.Acquire(root);
        var pause = new PauseInsertInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(pause).Options;
        var coordinator = new TargetContextCoordinator(options);
        var main = new MainWindow(coordinator, database.BackupDirectory)
        { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        main.Show();
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!main.IsEnabled && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (!main.IsEnabled) throw new TimeoutException("主窗口未完成初始化。");

        var service = new DataService(coordinator);
        var parent = await service.AddMemberAsync(new FamilyMember { Name = "虚构父" });
        var importPath = Path.Combine(database.BackupDirectory, "selected.json");
        await new CollaborationService().ExportSubtreeJson(parent.Id, [parent], importPath);
        var import = new CollaborationImportDialog([parent], service)
        { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        import.Show();
        var backup = new DatabaseBackupDialog(database.DatabasePath, database.BackupDirectory, coordinator)
        { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        backup.Show();

        pause.Enabled = true;
        var importing = import.ImportAsync(importPath, parent.Id);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var draining = main.QuiesceForMaintenanceAsync();
        if (draining.IsCompleted) throw new InvalidOperationException("排空未等待正在提交的导入。");
        try { await backup.CreateBackupAsync(Path.Combine(database.BackupDirectory, "blocked.zip")); throw new InvalidOperationException("排空后备份提交未被拒绝。"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("停用")) { }

        // Closing the owner is not proof that App.OnExit keeps its lock: the test owns this lease explicitly.
        main.Close();
        try { using var second = DataDirectoryLease.Acquire(root); throw new InvalidOperationException("测试租约未保持排他。"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("占用") || ex.Message.Contains("使用")) { }
        pause.Continue.TrySetResult();
        if (await importing != 1 || !await draining) throw new InvalidOperationException("关闭窗口后在途导入未完成或排空失败。");
        await using (var exclusive = new FileStream(database.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        if (File.Exists(Path.Combine(database.BackupDirectory, "blocked.zip"))) throw new InvalidOperationException("拒绝后的备份文件不应存在。");
        await using var verify = database.CreateContext();
        if (await verify.Members.CountAsync() != 2) throw new InvalidOperationException("导入结果与预期不符。");
    }

    private sealed class PauseInsertInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO \"Members\"", StringComparison.Ordinal))
            {
                Enabled = false;
                Entered.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
