using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

// Runs only in a separate, GUID-temporary test directory and closes only its own native dialog.
internal static class WpfDisplayedWorker
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private const uint WmClose = 0x0010;

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
                try { await VerifyAsync(database); result.TrySetResult(0); }
                catch (Exception ex) { Console.Error.WriteLine(ex); result.TrySetResult(1); }
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
        var main = new MainWindow(coordinator, database.BackupDirectory) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        main.Show();
        var deadline = Stopwatch.StartNew();
        while (!main.IsEnabled && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (!main.IsEnabled) throw new TimeoutException("主窗口未完成初始化。");

        var panel = (System.Windows.Controls.GroupBox)main.FindName("genealogyPanel");
        var genealogy = (GenealogySelectionViewModel)panel.DataContext;
        genealogy.NewName = "搜索验收谱";
        await genealogy.CreateAsync();
        genealogy.NewPersonName = "搜索验收人物";
        await genealogy.AddPersonAsync();
        var search = (System.Windows.Controls.TextBox)main.FindName("personSearchText");
        var results = (System.Windows.Controls.ComboBox)main.FindName("personSearchResults");
        var locate = (System.Windows.Controls.Button)main.FindName("locatePersonButton");
        var tree = (System.Windows.Controls.TreeView)main.FindName("targetTree");
        search.Text = "验收人物";
        if (results.Items.Count != 1) throw new InvalidOperationException("姓名搜索未显示结果。");
        results.SelectedIndex = 0;
        main.UpdateLayout();
        locate.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (tree.SelectedItem is not TargetMemberTreeNode selected || selected.PersonId != genealogy.People.Single().Id ||
            genealogy.SelectedPerson?.Id != selected.PersonId)
            throw new InvalidOperationException("搜索定位未选中目标人物。");

        await EditRecordedValuesAsync("", "", "七世", "长");
        await EditRecordedValuesAsync("七世", "长", "八世", "次");
        await EditRecordedValuesAsync("八世", "次", "", "");
        if (genealogy.People.Single().RecordedGeneration is not null || genealogy.People.Single().RecordedBirthOrder is not null)
            throw new InvalidOperationException("清空谱载字段未保存。");

        foreach (var name in new[] { "长房", "次房" })
        {
            ((System.Windows.Controls.TextBox)main.FindName("newBranchName")).Text = name;
            ((System.Windows.Controls.Button)main.FindName("createBranchButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while (!panel.IsEnabled && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (!panel.IsEnabled || !genealogy.Branches.Any(b => b.Name == name))
                throw new InvalidOperationException("界面未创建顶层房支。");
        }
        var firstBranch = genealogy.Branches.Single(b => b.Name == "长房").Id;
        var secondBranch = genealogy.Branches.Single(b => b.Name == "次房").Id;
        await CreateChildAsync(firstBranch, "次支");
        var nestedBranch = genealogy.Branches.Single(b => b.Name == "次支").Id;
        await CreateChildAsync(nestedBranch, "三支");
        var thirdBranch = genealogy.Branches.Single(b => b.Name == "三支").Id;
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        if (!genealogy.ParentBranchChoices.Any(b => b.Id == thirdBranch && b.Label.Contains("长房 / 次支 / 三支")))
            throw new InvalidOperationException("重新加载后子房支层级不正确。");
        await EditBranchAsync(null, firstBranch);
        await EditBranchAsync(firstBranch, thirdBranch);
        await EditBranchAsync(thirdBranch, secondBranch);
        await EditBranchAsync(secondBranch, null);
        await EditBranchAsync(null, thirdBranch);
        await RenameBranchAsync(firstBranch, "新长房");
        await RenameBranchAsync(nestedBranch, "新次支");
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        if (!genealogy.ParentBranchChoices.Any(b => b.Id == thirdBranch && b.Label.Contains("新长房 / 新次支 / 三支")) ||
            genealogy.People.Single().BranchId != thirdBranch)
            throw new InvalidOperationException("重命名后层级路径或人物归属不正确。");
        var reopened = new TargetPersonEditDialog(genealogy.People.Single(), genealogy.Branches.ToList());
        if (reopened.BranchId != thirdBranch) throw new InvalidOperationException("重开人物档案后房支归属不正确。");
        await CreateChildAsync(firstBranch, "待删除空支");
        var emptyBranch = genealogy.Branches.Single(b => b.Name == "待删除空支").Id;
        await ConfirmDeleteBranchAsync(emptyBranch, false);
        if (!genealogy.Branches.Any(b => b.Id == emptyBranch)) throw new InvalidOperationException("取消删除后房支消失。");
        await ConfirmDeleteBranchAsync(emptyBranch, true);
        if (genealogy.Branches.Any(b => b.Id == emptyBranch) || genealogy.People.Single().BranchId != thirdBranch)
            throw new InvalidOperationException("确认删除空房支后结果不正确。");

        async Task ConfirmDeleteBranchAsync(Guid branchId, bool confirm)
        {
            ((System.Windows.Controls.ComboBox)main.FindName("renameBranchSelector")).SelectedItem = genealogy.ParentBranchChoices.Single(b => b.Id == branchId);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = FindWindow(null, "确认删除房支");
                if (dialog == IntPtr.Zero) return;
                GetWindowThreadProcessId(dialog, out var ownerPid);
                if (ownerPid != (uint)Environment.ProcessId) return;
                timer.Stop();
                PostMessage(dialog, 0x0111, new IntPtr(confirm ? 6 : 7), IntPtr.Zero);
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("deleteBranchButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            timer.Stop();
            var until = Stopwatch.StartNew();
            while (!panel.IsEnabled && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (!panel.IsEnabled) throw new TimeoutException("删除房支后界面未恢复。");
        }

        async Task RenameBranchAsync(Guid branchId, string name)
        {
            ((System.Windows.Controls.ComboBox)main.FindName("renameBranchSelector")).SelectedItem = genealogy.ParentBranchChoices.Single(b => b.Id == branchId);
            ((System.Windows.Controls.TextBox)main.FindName("renameBranchName")).Text = name;
            ((System.Windows.Controls.Button)main.FindName("renameBranchButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while (!panel.IsEnabled && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (!panel.IsEnabled || !genealogy.Branches.Any(b => b.Id == branchId && b.Name == name))
                throw new InvalidOperationException("界面未完成房支重命名。");
        }

        async Task CreateChildAsync(Guid parentId, string name)
        {
            var selector = (System.Windows.Controls.ComboBox)main.FindName("parentBranchSelector");
            selector.SelectedItem = genealogy.ParentBranchChoices.Single(b => b.Id == parentId);
            ((System.Windows.Controls.TextBox)main.FindName("childBranchName")).Text = name;
            ((System.Windows.Controls.Button)main.FindName("createChildBranchButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while (!panel.IsEnabled && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (!panel.IsEnabled || !genealogy.Branches.Any(b => b.Name == name && b.ParentBranchId == parentId))
                throw new InvalidOperationException("界面未创建子房支。");
        }

        async Task EditBranchAsync(Guid? expected, Guid? target)
        {
            Exception? failure = null;
            var editTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            editTimer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<TargetPersonEditDialog>().SingleOrDefault();
                if (dialog is null) return;
                editTimer.Stop();
                var box = (System.Windows.Controls.ComboBox)dialog.FindName("BranchBox");
                if (dialog.BranchId != expected) failure = new InvalidOperationException("重开档案时房支不正确。");
                if (!box.Items.Cast<object>().Any(item => item.GetType().GetProperty("Label")!.GetValue(item)!.ToString()!.Contains("长房 / 次支")))
                    failure = new InvalidOperationException("嵌套房支未显示层级路径。");
                box.SelectedItem = box.Items.Cast<object>().Single(item => (Guid?)item.GetType().GetProperty("Id")!.GetValue(item) == target);
                ((System.Windows.Controls.Button)dialog.FindName("SaveButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            editTimer.Start();
            var edit = typeof(MainWindow).GetMethod("EditTreePersonAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)edit.Invoke(main, [genealogy.TargetTree.Single()])!;
            editTimer.Stop();
            if (failure is not null) throw failure;
            if (genealogy.People.Single().BranchId != target) throw new InvalidOperationException("房支归属未保存。");
        }

        async Task EditRecordedValuesAsync(string expectedGeneration, string expectedOrder, string generation, string order)
        {
            Exception? failure = null;
            var editTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            editTimer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<TargetPersonEditDialog>().SingleOrDefault();
                if (dialog is null) return;
                editTimer.Stop();
                var generationBox = (System.Windows.Controls.TextBox)dialog.FindName("RecordedGenerationBox");
                var orderBox = (System.Windows.Controls.TextBox)dialog.FindName("RecordedBirthOrderBox");
                if (generationBox.Text != expectedGeneration || orderBox.Text != expectedOrder)
                    failure = new InvalidOperationException("重开档案未显示已保存的谱载值。");
                generationBox.Text = generation;
                orderBox.Text = order;
                ((System.Windows.Controls.Button)dialog.FindName("SaveButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            editTimer.Start();
            var edit = typeof(MainWindow).GetMethod("EditTreePersonAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)edit.Invoke(main, [genealogy.TargetTree.Single()])!;
            editTimer.Stop();
            if (failure is not null) throw failure;
            if (genealogy.People.Single().RecordedGeneration != (generation.Length == 0 ? null : generation) ||
                genealogy.People.Single().RecordedBirthOrder != (order.Length == 0 ? null : order))
                throw new InvalidOperationException("谱载字段未通过人物编辑窗口保存。");
        }

        genealogy.NewPersonName = "关系对方";
        await genealogy.AddPersonAsync();
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Name == "搜索验收人物");
        var relationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        relationTimer.Tick += (_, _) =>
        {
            var dialog = main.OwnedWindows.OfType<TargetRelationshipDialog>().SingleOrDefault();
            if (dialog is null) return;
            relationTimer.Stop();
            var related = (System.Windows.Controls.ComboBox)dialog.FindName("RelatedPersonBox");
            related.SelectedItem = related.Items.Cast<ZupuPaiBan.Models.Person>().Single(p => p.Name == "关系对方");
            ((System.Windows.Controls.TextBox)dialog.FindName("NoteBox")).Text = "界面谱载备注";
            ((System.Windows.Controls.TextBox)dialog.FindName("SourceCitationBox")).Text = "界面谱牒来源";
            ((System.Windows.Controls.Button)dialog.FindName("SaveButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        };
        relationTimer.Start();
        typeof(MainWindow).GetMethod("TargetShortcut_AddRelationship_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(main, [main, new RoutedEventArgs()]);
        relationTimer.Stop();
        var relationDeadline = Stopwatch.StartNew();
        while (!panel.IsEnabled && relationDeadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (!panel.IsEnabled) throw new TimeoutException("新增关系后界面未恢复。");
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        if (!genealogy.TargetTree.SelectMany(node => node.Details)
            .Any(detail => detail.Contains("界面谱载备注") && detail.Contains("界面谱牒来源")))
            throw new InvalidOperationException("重载后关系清单未显示备注与来源。");

        await EditRelationshipDetailsAsync("界面谱载备注", "界面谱牒来源", "更正备注", "更正来源");
        await EditRelationshipDetailsAsync("更正备注", "更正来源", "", "");
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        await using (var verifyRelation = coordinator.CreateContext())
        {
            var relation = await verifyRelation.PersonRelationships.SingleAsync();
            if (relation.Note is not null || relation.SourceCitation is not null)
                throw new InvalidOperationException("关系资料清空未保存。");
        }

        async Task EditRelationshipDetailsAsync(string expectedNote, string expectedSource, string note, string source)
        {
            genealogy.SelectedPerson = genealogy.People.Single(p => p.Name == "搜索验收人物");
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<TargetRelationshipDetailsDialog>().SingleOrDefault();
                if (dialog is null) return;
                timer.Stop();
                ((System.Windows.Controls.ListBox)dialog.FindName("CandidateList")).SelectedIndex = 0;
                var noteBox = (System.Windows.Controls.TextBox)dialog.FindName("NoteBox");
                var sourceBox = (System.Windows.Controls.TextBox)dialog.FindName("SourceCitationBox");
                if (noteBox.Text != expectedNote || sourceBox.Text != expectedSource)
                    failure = new InvalidOperationException("关系资料编辑窗口未显示原值。");
                noteBox.Text = note;
                sourceBox.Text = source;
                ((System.Windows.Controls.Button)dialog.FindName("SaveButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("editRelationshipDetailsButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled) && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (timer.IsEnabled || !panel.IsEnabled) { timer.Stop(); throw new TimeoutException("编辑关系资料后界面未恢复。"); }
            if (failure is not null) throw failure;
        }

        genealogy.SelectedPerson = genealogy.People.Single(p => p.Name == "搜索验收人物");
        var unionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        unionTimer.Tick += (_, _) =>
        {
            var dialog = main.OwnedWindows.OfType<TargetFamilyUnionDialog>().SingleOrDefault();
            if (dialog is null) return;
            unionTimer.Stop();
            var partner = (System.Windows.Controls.ComboBox)dialog.FindName("PartnerTwoBox");
            partner.SelectedItem = partner.Items.Cast<ZupuPaiBan.Models.Person>().Single(p => p.Name == "关系对方");
            ((System.Windows.Controls.TextBox)dialog.FindName("StartDateBox")).Text = "谱载始年";
            ((System.Windows.Controls.TextBox)dialog.FindName("EndDateBox")).Text = "谱载止年";
            ((System.Windows.Controls.TextBox)dialog.FindName("NoteBox")).Text = "婚配备注";
            ((System.Windows.Controls.Button)dialog.FindName("SaveButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        };
        unionTimer.Start();
        typeof(MainWindow).GetMethod("TargetShortcut_AddFamilyUnion_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(main, [main, new RoutedEventArgs()]);
        unionTimer.Stop();
        var unionDeadline = Stopwatch.StartNew();
        while (!panel.IsEnabled && unionDeadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (!panel.IsEnabled) throw new TimeoutException("新增婚配后界面未恢复。");
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        if (!genealogy.TargetTree.SelectMany(node => node.Details)
            .Any(detail => detail.Contains("谱载始年") && detail.Contains("谱载止年") && detail.Contains("婚配备注")))
            throw new InvalidOperationException("重载后婚配资料未显示。");

        await EditUnionDetailsAsync("谱载始年", "谱载止年", "婚配备注", "更正始年", "更正止年", "更正婚配备注");
        await EditUnionDetailsAsync("更正始年", "更正止年", "更正婚配备注", "", "", "");
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        await using (var verifyUnion = coordinator.CreateContext())
        {
            var union = await verifyUnion.FamilyUnions.SingleAsync();
            if (union.StartDateText is not null || union.EndDateText is not null || union.Note is not null)
                throw new InvalidOperationException("婚配资料清空未保存。");
        }

        async Task EditUnionDetailsAsync(string expectedStart, string expectedEnd, string expectedNote, string start, string end, string note)
        {
            genealogy.SelectedPerson = genealogy.People.Single(p => p.Name == "搜索验收人物");
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<TargetFamilyUnionDetailsDialog>().SingleOrDefault();
                if (dialog is null) return;
                timer.Stop();
                ((System.Windows.Controls.ListBox)dialog.FindName("CandidateList")).SelectedIndex = 0;
                var startBox = (System.Windows.Controls.TextBox)dialog.FindName("StartDateBox");
                var endBox = (System.Windows.Controls.TextBox)dialog.FindName("EndDateBox");
                var noteBox = (System.Windows.Controls.TextBox)dialog.FindName("NoteBox");
                if (startBox.Text != expectedStart || endBox.Text != expectedEnd || noteBox.Text != expectedNote)
                    failure = new InvalidOperationException("婚配编辑窗口未显示原值。");
                startBox.Text = start; endBox.Text = end; noteBox.Text = note;
                ((System.Windows.Controls.Button)dialog.FindName("SaveButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("editFamilyUnionDetailsButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled) && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (timer.IsEnabled || !panel.IsEnabled) { timer.Stop(); throw new TimeoutException("编辑婚配资料后界面未恢复。"); }
            if (failure is not null) throw failure;
        }

        var originalGenealogyId = genealogy.SelectedGenealogy!.Id;
        Guid duplicateId;
        await using (var duplicateContext = coordinator.CreateContext())
        {
            var duplicate = await new GenealogyDataService(duplicateContext).CreateGenealogyAsync(
                new ZupuPaiBan.Models.Genealogy { Name = "搜索验收谱" });
            duplicateId = duplicate.Id;
        }
        await genealogy.LoadAsync();
        var genealogySelector = (System.Windows.Controls.ComboBox)main.FindName("genealogySelector");
        if (genealogySelector.Items.Cast<ZupuPaiBan.Models.Genealogy>().Count(g => g.Name == "搜索验收谱") != 2 ||
            genealogySelector.ItemTemplate is null)
            throw new InvalidOperationException("同名族谱未作为独立项显示。");
        var itemTemplate = (System.Windows.Controls.StackPanel)genealogySelector.ItemTemplate.LoadContent();
        if (System.Windows.Data.BindingOperations.GetBinding((System.Windows.Controls.TextBlock)itemTemplate.Children[2],
                System.Windows.Controls.TextBlock.TextProperty)?.Path.Path != "Id")
            throw new InvalidOperationException("族谱选择项未显示稳定 ID。");
        genealogySelector.SelectedItem = genealogy.Genealogies.Single(g => g.Id == originalGenealogyId);
        var selectDeadline = Stopwatch.StartNew();
        while ((genealogy.SelectedGenealogy?.Id != originalGenealogyId || genealogy.People.Count != 2) && selectDeadline.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        if (genealogy.People.Count != 2) throw new TimeoutException("选择族谱后未载入人物。");
        ((System.Windows.Controls.TextBox)main.FindName("renameGenealogyName")).Text = "搜索验收谱已更名";
        ((System.Windows.Controls.Button)main.FindName("renameGenealogyButton"))
            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var renameDeadline = Stopwatch.StartNew();
        while (!panel.IsEnabled && renameDeadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (!panel.IsEnabled || genealogy.SelectedGenealogy?.Id != originalGenealogyId ||
            genealogy.SelectedGenealogy.Name != "搜索验收谱已更名" || genealogy.People.Count != 2 ||
            genealogy.Genealogies.Single(g => g.Id == duplicateId).Name != "搜索验收谱")
            throw new InvalidOperationException("族谱重命名未保持目标选择或影响了同名族谱。");
        await genealogy.LoadAsync();
        genealogySelector.SelectedItem = genealogy.Genealogies.Single(g => g.Id == originalGenealogyId);
        var reloadDeadline = Stopwatch.StartNew();
        while ((genealogy.SelectedGenealogy?.Id != originalGenealogyId || genealogy.People.Count != 2) && reloadDeadline.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        if (genealogy.SelectedGenealogy?.Id != originalGenealogyId || genealogy.SelectedGenealogy.Name != "搜索验收谱已更名" || genealogy.People.Count != 2)
            throw new InvalidOperationException("重载后族谱选择或人物不正确。");
        await using (var verifyRename = coordinator.CreateContext())
        {
            if (await verifyRename.PersonRelationships.CountAsync(r => r.GenealogyId == originalGenealogyId) != 1 ||
                await verifyRename.FamilyUnions.CountAsync(u => u.GenealogyId == originalGenealogyId) != 1 ||
                await verifyRename.FamilyBranches.CountAsync(b => b.GenealogyId == originalGenealogyId) != 4)
                throw new InvalidOperationException("族谱重命名改变了原有图数据。");
        }

        var service = new DataService(coordinator);
        var preview = new PrintPreviewWindow(service, new PrintService()) { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        preview.Show();
        var backup = new DatabaseBackupDialog(database.DatabasePath, database.BackupDirectory, coordinator)
        { Owner = main, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        backup.Show();
        var active = coordinator.CreateContext();
        await active.Database.OpenConnectionAsync();

        Task<bool>? drain = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var dialog = FindWindow(null, "保存数据库专用备份");
            if (dialog == IntPtr.Zero) return;
            GetWindowThreadProcessId(dialog, out var ownerPid);
            if (ownerPid != (uint)Environment.ProcessId) return;
            timer.Stop();
            drain = main.QuiesceForMaintenanceAsync();
            PostMessage(dialog, WmClose, IntPtr.Zero, IntPtr.Zero);
        };
        timer.Start();
        var handler = typeof(DatabaseBackupDialog).GetMethod("CreateBackup_Click", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("无法找到备份按钮事件。");
        handler.Invoke(backup, [backup, new RoutedEventArgs()]); // Nested native-dialog message loop.
        timer.Stop();
        if (drain is null || drain.IsCompleted || main.IsEnabled)
            throw new InvalidOperationException("文件对话框重入时未进入等待排空状态。");
        await active.DisposeAsync();
        if (!await drain || main.IsEnabled || !((MainViewModel)main.DataContext).StatusText.Contains("恢复功能尚未开放"))
            throw new InvalidOperationException("排空成功状态不正确。");
        if (Directory.GetFiles(database.BackupDirectory, "*.zip").Length != 0)
            throw new InvalidOperationException("取消文件对话框后不应创建备份。");
        await using (var exclusive = new FileStream(database.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        preview.Close(); backup.Close(); main.Close();

        var failedCoordinator = new TargetContextCoordinator(options);
        var failedWindow = new MainWindow(failedCoordinator, database.BackupDirectory) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        var held = failedCoordinator.CreateContext();
        using var cancellation = new CancellationTokenSource();
        var failedDrain = failedWindow.QuiesceForMaintenanceAsync(cancellation.Token);
        cancellation.Cancel();
        if (await failedDrain || failedWindow.IsEnabled || !((MainViewModel)failedWindow.DataContext).StatusText.Contains("重新启动"))
            throw new InvalidOperationException("取消排空后未保持关闭并提示重启。");
        await held.DisposeAsync();
        try { failedCoordinator.CreateContext(); throw new InvalidOperationException("取消后仍可创建上下文。"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("停用")) { }
        failedWindow.Close();
    }
}
