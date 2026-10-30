using System.Data.Common;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ZupuPaiBan;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

// This worker is invoked only with an existing GUID-named test root.
internal static class WpfInflightWorker
{
    public static async Task<int> RunTargetLinkEditingAsync(string directory)
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
                try { await VerifyTargetLinkEditingAsync(database, directory); result.TrySetResult(0); }
                catch (Exception ex) { Console.Error.WriteLine(ex); result.TrySetResult(1); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        return await result.Task;
    }

    public static async Task<int> RunExternalAsync(string directory)
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
                try { await VerifyExternalAsync(database, directory); result.TrySetResult(0); }
                catch (Exception ex) { Console.Error.WriteLine(ex); result.TrySetResult(1); }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        return await result.Task;
    }
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

    private static async Task VerifyExternalAsync(TestDatabase database, string root)
    {
        static void Stage(string name) => Console.WriteLine($"External inflight stage: {name}");
        Directory.CreateDirectory(database.BackupDirectory);
        using var lease = DataDirectoryLease.Acquire(root);
        var pause = new PauseGenealogyInsertInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={database.DatabasePath};Pooling=False").AddInterceptors(pause).Options;
        var coordinator = new TargetContextCoordinator(options);
        var main = new MainWindow(coordinator, database.BackupDirectory, coordinateExit: true) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        main.Show();
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!main.IsEnabled && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (!main.IsEnabled) throw new TimeoutException("主窗口未完成初始化。");
        Stage("main-ready");
        long generationBefore;
        await using (var before = database.CreateContext())
            generationBefore = await before.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
        var source = Path.Combine(root, "selected.ged");
        await File.WriteAllTextAsync(source, "0 HEAD\n1 GEDC\n2 VERS 5.5.1\n0 @I1@ INDI\n1 NAME 虚构 /人物/\n1 SEX M\n0 TRLR\n");
        var dialog = new ExternalGenealogyExchangeImportDialog(coordinator) { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        dialog.Show();
        typeof(ExternalGenealogyExchangeImportDialog).GetField("_selectedPath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, source);
        var refresh = (Task)typeof(ExternalGenealogyExchangeImportDialog).GetMethod("RefreshPreflightAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [])!;
        await refresh;
        Stage("preflight-complete");
        ((System.Windows.Controls.TextBox)dialog.FindName("GenealogyNameText")).Text = "在途外部谱";
        pause.Enabled = true;
        typeof(ExternalGenealogyExchangeImportDialog).GetMethod("ConfirmImport_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Stage("insert-paused");
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        main.Close();
        Stage("close-requested");
        if (main.IsEnabled) throw new InvalidOperationException("正常退出未开始等待在途外部导入。");
        try { using var operation = coordinator.EnterOperation(); throw new InvalidOperationException("排空期间未拒绝新操作。"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("停用")) { }
        try { using var second = DataDirectoryLease.Acquire(root); throw new InvalidOperationException("外部导入在途时未保持测试租约。"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("占用") || ex.Message.Contains("使用")) { }
        pause.Continue.TrySetResult();
        Stage("insert-released");
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Stage("main-closed");
        await using var verify = database.CreateContext();
        if (await verify.Genealogies.CountAsync() != 1 || await verify.People.CountAsync() != 1 || await verify.GenealogyImportRecords.CountAsync() != 1)
            throw new InvalidOperationException("外部导入未以完整图和来源记录原子完成。");
        var generationAfter = await verify.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
        if (generationAfter != generationBefore + 1)
            throw new InvalidOperationException("外部导入退出竞争中的代数与已提交人物不一致。");
        Stage("verified");
    }

    private static async Task VerifyTargetLinkEditingAsync(TestDatabase database, string root)
    {
        Directory.CreateDirectory(database.BackupDirectory);
        using var lease = DataDirectoryLease.Acquire(root);
        await VerifyTargetLinkExitBoundaryAsync(database, root, TargetLinkExitAction.AddUnion);
        await VerifyTargetLinkExitBoundaryAsync(database, root, TargetLinkExitAction.RemoveRelationship);
    }

    private static async Task VerifyTargetLinkExitBoundaryAsync(TestDatabase database, string root, TargetLinkExitAction action)
    {
        var sqlFragment = action == TargetLinkExitAction.AddUnion
            ? "INSERT INTO \"FamilyUnions\""
            : "DELETE FROM \"PersonRelationships\"";
        var pause = new PauseTargetLinkCommandInterceptor(sqlFragment);
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").AddInterceptors(pause).Options;
        var coordinator = new TargetContextCoordinator(options);
        var main = new MainWindow(coordinator, database.BackupDirectory, coordinateExit: true, exitTimeout: TimeSpan.FromSeconds(10))
        { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        main.Show();
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!main.IsEnabled && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (!main.IsEnabled) throw new TimeoutException("目标关系测试主窗口未完成初始化。");

        var viewModel = new GenealogySelectionViewModel(coordinator.CreateContext, coordinator)
        { NewName = action == TargetLinkExitAction.AddUnion ? "在途婚配谱" : "在途关系谱" };
        await viewModel.CreateAsync();
        var genealogy = viewModel.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙" })
        {
            viewModel.NewPersonName = name;
            await viewModel.AddPersonAsync();
        }
        var first = viewModel.People.Single(person => person.Name == "甲");
        var second = viewModel.People.Single(person => person.Name == "乙");
        viewModel.SelectedPerson = first;
        if (action == TargetLinkExitAction.RemoveRelationship)
        {
            await viewModel.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        }
        long generationBefore, scopedGenerationBefore;
        await using (var before = database.CreateContext())
        {
            generationBefore = await before.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
            scopedGenerationBefore = await ScopedValueAsync(before, genealogy.Id);
        }

        pause.Enabled = true;
        Task edit = action == TargetLinkExitAction.AddUnion
            ? viewModel.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, second.Id)
            : viewModel.RemoveTargetLinkAsync((await viewModel.GetTargetLinkRemovalCandidatesAsync())
                .Single(candidate => candidate.Kind == TargetLinkRemovalKind.Relationship));
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        main.Close();
        if (main.IsEnabled) throw new InvalidOperationException("正常退出未开始等待在途目标关系编辑。");
        try { using var operation = coordinator.EnterOperation(); throw new InvalidOperationException("排空期间未拒绝新目标操作。"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("停用")) { }
        try { using var secondLease = DataDirectoryLease.Acquire(root); throw new InvalidOperationException("目标关系编辑在途时未保持测试租约。"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("占用") || ex.Message.Contains("使用")) { }

        pause.Continue.TrySetResult();
        await edit;
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var exclusive = new FileStream(database.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        await using var verify = database.CreateContext();
        var generationAfter = await verify.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
        if (generationAfter != generationBefore + 1)
            throw new InvalidOperationException("目标关系退出竞争中的代数与已提交写入不一致。");
        if (await ScopedValueAsync(verify, genealogy.Id) != scopedGenerationBefore + 1)
            throw new InvalidOperationException("目标关系退出竞争中的按谱代数与已提交写入不一致。");
        if (action == TargetLinkExitAction.AddUnion)
        {
            if (await verify.FamilyUnions.CountAsync(union => union.GenealogyId == genealogy.Id) != 1 ||
                await verify.PersonRelationships.CountAsync(relationship => relationship.GenealogyId == genealogy.Id) != 0)
                throw new InvalidOperationException("关闭后的在途婚配新增未以单一完整结果提交。");
        }
        else if (await verify.PersonRelationships.CountAsync(relationship => relationship.GenealogyId == genealogy.Id) != 0 ||
                 await verify.FamilyUnions.CountAsync(union => union.GenealogyId == genealogy.Id) != 0)
        {
            throw new InvalidOperationException("关闭后的在途人物关系解除未以单一完整结果提交。");
        }
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

    private sealed class PauseGenealogyInsertInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PauseIfGenealogyInsertAsync(command, cancellationToken);
            return result;
        }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PauseIfGenealogyInsertAsync(command, cancellationToken);
            return result;
        }
        private async Task PauseIfGenealogyInsertAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO \"Genealogies\"", StringComparison.Ordinal))
            { Enabled = false; Entered.TrySetResult(); await Continue.Task.WaitAsync(cancellationToken); }
        }
    }

    private sealed class PauseTargetLinkCommandInterceptor(string sqlFragment) : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PauseIfTargetLinkCommandAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PauseIfTargetLinkCommandAsync(command, cancellationToken);
            return result;
        }

        private async Task PauseIfTargetLinkCommandAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (!Enabled || !command.CommandText.Contains(sqlFragment, StringComparison.Ordinal)) return;
            Enabled = false;
            Entered.TrySetResult();
            await Continue.Task.WaitAsync(cancellationToken);
        }
    }

    private enum TargetLinkExitAction
    {
        AddUnion,
        RemoveRelationship
    }

    private static Task<long> ScopedValueAsync(ZupuDbContext context, Guid genealogyId) =>
        context.Database.SqlQueryRaw<long>(ScopedUnlinkGenerationV1.GuardSql(),
            new Microsoft.Data.Sqlite.SqliteParameter("@genealogy", genealogyId.ToString().ToUpperInvariant())).SingleAsync();
}
