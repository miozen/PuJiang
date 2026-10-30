using System.Data.Common;
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

// Dedicated process: process-wide data root and WPF dispatcher never touch the user's application.
internal static class WpfLifecycleWorker
{
    public static async Task<int> RunTargetRelationshipEditingAsync(string directory)
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
                try { await VerifyTargetRelationshipEditingAsync(database); result.TrySetResult(0); }
                catch (Exception exception) { Console.Error.WriteLine(exception); result.TrySetResult(1); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return await result.Task;
    }

    public static async Task<int> RunTargetTreeEditingAsync(string directory)
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
                try { await VerifyTargetTreeEditingAsync(database); result.TrySetResult(0); }
                catch (Exception exception) { Console.Error.WriteLine(exception); result.TrySetResult(1); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return await result.Task;
    }

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

    private static async Task VerifyTargetRelationshipEditingAsync(TestDatabase database)
    {
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
        var coordinator = new TargetContextCoordinator(options);
        await using (var migration = coordinator.CreateContext()) await migration.Database.MigrateAsync();
        var viewModel = new GenealogySelectionViewModel(coordinator.CreateContext, coordinator) { NewName = "甲谱" };
        await viewModel.CreateAsync();
        var firstGenealogy = viewModel.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙" })
        {
            viewModel.NewPersonName = name;
            await viewModel.AddPersonAsync();
        }
        var first = viewModel.People.Single(person => person.Name == "甲");
        var second = viewModel.People.Single(person => person.Name == "乙");
        viewModel.SelectedPerson = first;

        var relationshipDialog = new TargetRelationshipDialog(viewModel.GetSelectedPersonForEditing(), viewModel.GetCurrentPeopleForEditing());
        var relatedBox = (System.Windows.Controls.ComboBox)relationshipDialog.FindName("RelatedPersonBox");
        if (relatedBox.Items.Count != 1 || relatedBox.Items[0] is not Person { Id: var relatedId } || relatedId != second.Id)
            throw new InvalidOperationException("关系编辑框暴露了当前族谱以外或主体自身的人物。");
        relatedBox.SelectedItem = relatedBox.Items[0];
        await viewModel.AddTargetRelationshipAsync(relationshipDialog.Kind, relationshipDialog.RelatedPersonId);

        var unionDialog = new TargetFamilyUnionDialog(viewModel.GetSelectedPersonForEditing(), viewModel.GetCurrentPeopleForEditing());
        var partnerBox = (System.Windows.Controls.ComboBox)unionDialog.FindName("PartnerTwoBox");
        if (partnerBox.Items.Count != 1 || partnerBox.Items[0] is not Person { Id: var partnerId } || partnerId != second.Id)
            throw new InvalidOperationException("婚配编辑框暴露了当前族谱以外或主体自身的人物。");
        partnerBox.SelectedItem = partnerBox.Items[0];
        await viewModel.AddTargetFamilyUnionAsync(unionDialog.Kind, unionDialog.PartnerTwoId ?? Guid.Empty);

        // Simulate a rename after the people list was loaded but before opening the confirmation.
        await using (var external = database.CreateContext())
        {
            (await external.People.SingleAsync(person => person.Id == first.Id)).Name = "甲新";
            await external.SaveChangesAsync();
        }
        var removalCandidates = await viewModel.GetTargetLinkRemovalCandidatesAsync();
        if (removalCandidates.Count != 2 || removalCandidates.Any(candidate => !candidate.Description.Contains("甲") && !candidate.Description.Contains("乙")))
            throw new InvalidOperationException("解除入口未仅显示当前人物参与的记录。");
        var removalDialog = new TargetLinkRemovalDialog(removalCandidates);
        var subjectLabel = (System.Windows.Controls.TextBlock)removalDialog.FindName("SubjectLabel");
        if (!subjectLabel.Text.Contains("甲新", StringComparison.Ordinal))
            throw new InvalidOperationException("确认窗口主体姓名未使用同一次候选快照。");
        var candidateList = (System.Windows.Controls.ListBox)removalDialog.FindName("CandidateList");
        var confirmation = (System.Windows.Controls.CheckBox)removalDialog.FindName("ConfirmationBox");
        var removeButton = (System.Windows.Controls.Button)removalDialog.FindName("RemoveButton");
        if (removeButton.IsEnabled) throw new InvalidOperationException("解除入口未要求明确确认。");
        candidateList.SelectedItem = removalCandidates.Single(candidate => candidate.Kind == TargetLinkRemovalKind.Relationship);
        confirmation.IsChecked = true;
        if (!removeButton.IsEnabled) throw new InvalidOperationException("解除入口未在选择并确认后启用。");

        await using (var external = database.CreateContext())
        {
            (await external.People.SingleAsync(person => person.Id == second.Id)).Name = "乙新";
            await external.SaveChangesAsync();
        }
        try
        {
            await viewModel.RemoveTargetLinkAsync(removalDialog.SelectedCandidate!);
            throw new InvalidOperationException("人物改名后的旧确认仍被接受。");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("解除候选已过期", StringComparison.Ordinal)) { }
        if (viewModel.People.Single(person => person.Id == second.Id).Name != "乙新")
            throw new InvalidOperationException("人物改名后未恢复当前人物列表。");
        removalDialog.Close();
        removalCandidates = await viewModel.GetTargetLinkRemovalCandidatesAsync();
        removalDialog = new TargetLinkRemovalDialog(removalCandidates);
        candidateList = (System.Windows.Controls.ListBox)removalDialog.FindName("CandidateList");
        confirmation = (System.Windows.Controls.CheckBox)removalDialog.FindName("ConfirmationBox");
        candidateList.SelectedItem = removalCandidates.Single(candidate => candidate.Kind == TargetLinkRemovalKind.Relationship);
        confirmation.IsChecked = true;

        // The modal confirmation keeps an immutable candidate, but another supported process can still edit the
        // SQLite file. The stale confirmation must delete nothing and reload the current graph before reporting it.
        await using (var external = database.CreateContext())
            await external.PersonRelationships.Where(item => item.Id == removalDialog.SelectedCandidate!.Id).ExecuteDeleteAsync();
        try
        {
            await viewModel.RemoveTargetLinkAsync(removalDialog.SelectedCandidate!);
            throw new InvalidOperationException("外部已解除的候选仍被接受。");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("解除候选已过期", StringComparison.Ordinal)) { }
        if (viewModel.Status != "解除候选已过期，当前图数据已重新加载。")
            throw new InvalidOperationException("外部解除后的确认窗口未恢复当前图状态。");
        var remainingCandidates = await viewModel.GetTargetLinkRemovalCandidatesAsync();
        if (remainingCandidates.Single().Kind != TargetLinkRemovalKind.FamilyUnion)
            throw new InvalidOperationException("外部解除后的候选列表仍保留过期关系或丢失婚配。");

        var staleSelectionDialog = new TargetLinkRemovalDialog(remainingCandidates);
        var staleCandidateList = (System.Windows.Controls.ListBox)staleSelectionDialog.FindName("CandidateList");
        var staleConfirmation = (System.Windows.Controls.CheckBox)staleSelectionDialog.FindName("ConfirmationBox");
        staleCandidateList.SelectedItem = remainingCandidates.Single();
        staleConfirmation.IsChecked = true;

        await using (var external = database.CreateContext())
        {
            var record = await external.FamilyUnions.SingleAsync(item => item.Id == staleSelectionDialog.SelectedCandidate!.Id);
            record.Note = "外部修改";
            await external.SaveChangesAsync();
        }
        try
        {
            await viewModel.RemoveTargetLinkAsync(staleSelectionDialog.SelectedCandidate!);
            throw new InvalidOperationException("外部修改后的确认候选仍被接受。");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("解除候选已过期", StringComparison.Ordinal)) { }
        if (viewModel.Status != "解除候选已过期，当前图数据已重新加载。")
            throw new InvalidOperationException("外部修改后未恢复当前图状态。");
        remainingCandidates = await viewModel.GetTargetLinkRemovalCandidatesAsync();
        var switchedDialog = new TargetLinkRemovalDialog(remainingCandidates);
        ((System.Windows.Controls.ListBox)switchedDialog.FindName("CandidateList")).SelectedItem = remainingCandidates.Single();
        ((System.Windows.Controls.CheckBox)switchedDialog.FindName("ConfirmationBox")).IsChecked = true;

        viewModel.NewName = "乙谱";
        await viewModel.CreateAsync();
        viewModel.NewPersonName = "外谱人物";
        await viewModel.AddPersonAsync();
        var foreign = viewModel.People.Single();
        viewModel.SelectedPerson = foreign;
        try
        {
            await viewModel.RemoveTargetLinkAsync(switchedDialog.SelectedCandidate!);
            throw new InvalidOperationException("切换族谱后的确认候选仍被接受。");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("当前选择", StringComparison.Ordinal)) { }
        await viewModel.SelectAsync(firstGenealogy);
        viewModel.SelectedPerson = viewModel.People.Single(person => person.Id == first.Id);
        var scopedDialog = new TargetFamilyUnionDialog(viewModel.GetSelectedPersonForEditing(), viewModel.GetCurrentPeopleForEditing());
        var scopedPartners = (System.Windows.Controls.ComboBox)scopedDialog.FindName("PartnerTwoBox");
        if (scopedPartners.Items.OfType<Person>().Any(person => person.Id == foreign.Id))
            throw new InvalidOperationException("切换后编辑框泄漏了另一族谱人物。");

        await using var verify = database.CreateContext();
        if (await verify.PersonRelationships.CountAsync(item => item.GenealogyId == firstGenealogy.Id) != 0 ||
            await verify.FamilyUnions.CountAsync(item => item.GenealogyId == firstGenealogy.Id) != 1 ||
            await verify.PersonRelationships.CountAsync(item => item.GenealogyId != firstGenealogy.Id) != 0 ||
            await verify.FamilyUnions.CountAsync(item => item.GenealogyId != firstGenealogy.Id) != 0)
            throw new InvalidOperationException("隔离 WPF 编辑写入了错误的族谱范围。");

        relationshipDialog.Close();
        unionDialog.Close();
        scopedDialog.Close();
        removalDialog.Close();
        staleSelectionDialog.Close();
        switchedDialog.Close();
    }

    private static async Task VerifyTargetTreeEditingAsync(TestDatabase database)
    {
        var pause = new PauseTreeEditCommandInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(pause).Options;
        var coordinator = new TargetContextCoordinator(options);
        await using (var migration = coordinator.CreateContext()) await migration.Database.MigrateAsync();
        var viewModel = new GenealogySelectionViewModel(coordinator.CreateContext, coordinator) { NewName = "甲谱" };
        await viewModel.CreateAsync();
        var firstGenealogy = viewModel.SelectedGenealogy!;
        foreach (var name in new[] { "甲父母", "待关联子女" })
        {
            viewModel.NewPersonName = name;
            await viewModel.AddPersonAsync();
        }
        var parent = viewModel.People.Single(item => item.Name == "甲父母");
        var existingChild = viewModel.People.Single(item => item.Name == "待关联子女");
        viewModel.NewName = "乙谱";
        await viewModel.CreateAsync();
        var secondGenealogy = viewModel.SelectedGenealogy!;
        viewModel.NewPersonName = "乙谱人物";
        await viewModel.AddPersonAsync();

        await viewModel.SelectAsync(firstGenealogy);
        var parentNode = viewModel.TargetTree.Single(item => item.PersonId == parent.Id);
        pause.Arm("UPDATE \"People\"");
        var profile = viewModel.SaveTreePersonProfileAsync(parentNode, "甲父母已编辑", PersonSex.Male, null, null, "树节点档案");
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await SwitchAndReleaseAsync();
        await profile;

        await viewModel.SelectAsync(firstGenealogy);
        parentNode = viewModel.TargetTree.Single(item => item.PersonId == parent.Id);
        pause.Arm("INSERT INTO \"PersonRelationships\"");
        var createChild = viewModel.AddBiologicalChildForTreeNodeAsync(parentNode, "树节点新增子女");
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await SwitchAndReleaseAsync();
        await createChild;

        await viewModel.SelectAsync(firstGenealogy);
        viewModel.SelectedPerson = viewModel.People.Single(item => item.Id == parent.Id);
        viewModel.SelectedExistingChild = viewModel.People.Single(item => item.Id == existingChild.Id);
        pause.Arm("INSERT INTO \"PersonRelationships\"");
        var linkExisting = viewModel.AddExistingBiologicalChildAsync();
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await SwitchAndReleaseAsync();
        await linkExisting;

        await viewModel.SelectAsync(firstGenealogy);
        viewModel.SelectedPerson = viewModel.People.Single(item => item.Id == parent.Id);
        viewModel.SelectedBiologicalChild = viewModel.People.Single(item => item.Name == "树节点新增子女");
        pause.Arm("DELETE FROM \"PersonRelationships\"");
        var unlink = viewModel.RemoveBiologicalParentRelationshipAsync();
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await SwitchAndReleaseAsync();
        await unlink;

        await using (var verify = database.CreateContext())
        {
            if ((await verify.People.SingleAsync(item => item.Id == parent.Id)).Biography != "树节点档案" ||
                await verify.People.CountAsync(item => item.GenealogyId == firstGenealogy.Id) != 3 ||
                await verify.People.CountAsync(item => item.GenealogyId == secondGenealogy.Id) != 1 ||
                await verify.PersonRelationships.CountAsync(item => item.GenealogyId == secondGenealogy.Id) != 0)
                throw new InvalidOperationException("跨谱切换导致成员树或父子编辑写入错误范围。");
            var remaining = await verify.PersonRelationships.SingleAsync(item => item.GenealogyId == firstGenealogy.Id);
            if (remaining.PersonId != existingChild.Id || remaining.RelatedPersonId != parent.Id)
                throw new InvalidOperationException("跨谱切换导致生物父子解除错误删除。");
        }
        if (viewModel.SelectedGenealogy?.Id != secondGenealogy.Id || viewModel.SelectedPerson is not null ||
            viewModel.People.Any(item => item.GenealogyId != secondGenealogy.Id) || viewModel.BiologicalChildren.Count != 0)
            throw new InvalidOperationException("旧谱父子编辑回填了当前谱视图。");

        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await using (var exclusive = new FileStream(database.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }

        async Task SwitchAndReleaseAsync()
        {
            var switching = viewModel.SelectAsync(secondGenealogy);
            if (viewModel.SelectedGenealogy?.Id != secondGenealogy.Id)
                throw new InvalidOperationException("在途编辑期间未先切换当前族谱。");
            pause.Release();
            await switching;
        }
    }

    private sealed class PauseTreeEditCommandInterceptor : DbCommandInterceptor
    {
        private string? _sqlFragment;
        private int _armed;
        private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _continue = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Arm(string sqlFragment)
        {
            _sqlFragment = sqlFragment;
            _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _continue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _armed, 1);
        }

        public void Release() => _continue.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PauseIfArmedAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PauseIfArmedAsync(command, cancellationToken);
            return result;
        }

        private async Task PauseIfArmedAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var fragment = _sqlFragment;
            if (fragment is null || !command.CommandText.Contains(fragment, StringComparison.Ordinal) ||
                Interlocked.Exchange(ref _armed, 0) != 1) return;
            var entered = _entered;
            var continuation = _continue;
            entered.TrySetResult();
            await continuation.Task.WaitAsync(cancellationToken);
        }
    }
}
