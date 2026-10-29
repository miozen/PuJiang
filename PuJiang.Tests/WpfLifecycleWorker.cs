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

        var removalCandidates = await viewModel.GetTargetLinkRemovalCandidatesAsync();
        if (removalCandidates.Count != 2 || removalCandidates.Any(candidate => !candidate.Description.Contains("甲") && !candidate.Description.Contains("乙")))
            throw new InvalidOperationException("解除入口未仅显示当前人物参与的记录。");
        var removalDialog = new TargetLinkRemovalDialog(viewModel.GetSelectedPersonForEditing(), removalCandidates);
        var candidateList = (System.Windows.Controls.ListBox)removalDialog.FindName("CandidateList");
        var confirmation = (System.Windows.Controls.CheckBox)removalDialog.FindName("ConfirmationBox");
        var removeButton = (System.Windows.Controls.Button)removalDialog.FindName("RemoveButton");
        if (removeButton.IsEnabled) throw new InvalidOperationException("解除入口未要求明确确认。");
        candidateList.SelectedItem = removalCandidates.Single(candidate => candidate.Kind == TargetLinkRemovalKind.Relationship);
        confirmation.IsChecked = true;
        if (!removeButton.IsEnabled) throw new InvalidOperationException("解除入口未在选择并确认后启用。");
        await viewModel.RemoveTargetLinkAsync(removalDialog.SelectedCandidate!);
        if ((await viewModel.GetTargetLinkRemovalCandidatesAsync()).Single().Kind != TargetLinkRemovalKind.FamilyUnion)
            throw new InvalidOperationException("解除关系错误删除了其它婚配记录。");

        viewModel.NewName = "乙谱";
        await viewModel.CreateAsync();
        viewModel.NewPersonName = "外谱人物";
        await viewModel.AddPersonAsync();
        var foreign = viewModel.People.Single();
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
    }
}
