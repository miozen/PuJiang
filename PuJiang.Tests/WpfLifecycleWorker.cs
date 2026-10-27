using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

// Dedicated process: process-wide data root and WPF dispatcher never touch the user's application.
internal static class WpfLifecycleWorker
{
    public static async Task<int> RunAsync(string directory)
    {
        var database = new TestDatabase(directory); // Validates the existing GUID-named temporary root.
        Environment.SetEnvironmentVariable(AppDataPaths.TestRootEnvironmentVariable, directory);
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await VerifyAsync(database); result.TrySetResult(0); }
                catch (Exception exception) { Console.Error.WriteLine(exception); result.TrySetResult(1); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await result.Task;
    }

    private static async Task VerifyAsync(TestDatabase database)
    {
        Directory.CreateDirectory(database.BackupDirectory);
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
        var coordinator = new TargetContextCoordinator(options);
        var main = new MainWindow(coordinator, database.BackupDirectory);
        await main.InitializeDatabaseAsync();
        var service = new DataService(coordinator);
        var parent = await service.AddMemberAsync(new FamilyMember { Name = "虚构父" });

        var preview = new PrintPreviewWindow(service, new PrintService());
        var previewViewModel = (PreviewViewModel)preview.DataContext;
        await previewViewModel.InitializeAsync();
        if (previewViewModel.AllMembers.Count != 1) throw new InvalidOperationException("预览未加载隔离测试人物。");

        var importPath = Path.Combine(database.BackupDirectory, "fictional.json");
        await new CollaborationService().ExportSubtreeJson(parent.Id, [parent], importPath);
        var importDialog = new CollaborationImportDialog([parent], service);
        if (await importDialog.ImportAsync(importPath, parent.Id) != 1)
            throw new InvalidOperationException("协作导入未完成。");
        var backupDialog = new DatabaseBackupDialog(database.DatabasePath, database.BackupDirectory, coordinator);
        var backupPath = Path.Combine(database.BackupDirectory, "ui-backup.zip");
        await backupDialog.CreateBackupAsync(backupPath);

        // Keep an active registered context alive while the real window-owned services are drained.
        var context = coordinator.CreateContext();
        await context.Database.OpenConnectionAsync();
        var drain = coordinator.DrainAsync();
        if (drain.IsCompleted) throw new InvalidOperationException("排空未等待活动连接。");
        await context.DisposeAsync();
        await drain;
        try { await importDialog.ImportAsync(importPath, parent.Id); throw new InvalidOperationException("排空后导入未被拒绝。"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("停用")) { }
        try { await backupDialog.CreateBackupAsync(Path.Combine(database.BackupDirectory, "blocked.zip")); throw new InvalidOperationException("排空后备份未被拒绝。"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("停用")) { }
        await previewViewModel.InitializeAsync();
        if (!previewViewModel.StatusText.Contains("失败")) throw new InvalidOperationException("预览未报告排空后的读取失败。");
        if (!File.Exists(backupPath)) throw new InvalidOperationException("测试备份不存在。");
        await using (var exclusive = new FileStream(database.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        preview.Close();
        importDialog.Close();
        backupDialog.Close();
        main.Close();
    }
}
