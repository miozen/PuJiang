using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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
        var originalPerson = genealogy.People.Single().Id;
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == originalPerson);
        genealogy.NewChildName = "快捷解除子女";
        await genealogy.AddBiologicalChildAsync();
        var quickChild = genealogy.People.Single(p => p.Name == "快捷解除子女").Id;
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == quickChild);
        var quickOriginal = (await genealogy.GetTargetLinkRemovalCandidatesAsync()).Single();
        await genealogy.UpdateTargetRelationshipDetailsAsync(quickOriginal, "快捷备注", "快捷来源");
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == originalPerson);
        await genealogy.LoadSelectedBiologicalChildrenAsync();
        genealogy.SelectedBiologicalChild = genealogy.BiologicalChildren.Single(p => p.Id == quickChild);
        await QuickUnlinkAsync(false);
        await using (var verifyCancel = coordinator.CreateContext())
            if (!await verifyCancel.PersonRelationships.AnyAsync(r => r.Id == quickOriginal.Id))
                throw new InvalidOperationException("取消快捷解除后关系消失。");
        await QuickUnlinkAsync(true);
        await using (var verifyUnlink = coordinator.CreateContext())
            if (await verifyUnlink.PersonRelationships.AnyAsync(r => r.Id == quickOriginal.Id) ||
                await verifyUnlink.People.CountAsync() != 2)
                throw new InvalidOperationException("快捷解除结果不正确。");
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == quickChild);
        await genealogy.DeleteUnrelatedPersonAsync();
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == originalPerson);
        genealogy.NewPersonName = "配偶验收人物";
        await genealogy.AddPersonAsync();
        var spouseId = genealogy.People.Single(p => p.Name == "配偶验收人物").Id;
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == originalPerson);
        await genealogy.AddTargetRelationshipAsync(ZupuPaiBan.Models.PersonRelationshipKind.Spouse, spouseId, "配偶谱记", "配偶谱源");
        await genealogy.AddTargetFamilyUnionAsync(ZupuPaiBan.Models.FamilyUnionKind.Marriage, spouseId);
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        var spouseNode = genealogy.TargetTree.Single(n => n.PersonId == spouseId);
        var spouseEntry = spouseNode.Entries.Single(e => e.Label.Contains("原记录方向") && e.OtherPersonId == originalPerson);
        if (!spouseEntry.Label.Contains("配偶谱记") || !spouseEntry.Label.Contains("配偶谱源") ||
            !spouseNode.Entries.Any(e => e.Label.StartsWith("婚配：")))
            throw new InvalidOperationException("配偶关系反向展示或独立婚配不正确。");
        var spouseTree = (System.Windows.Controls.TreeView)main.FindName("targetTree");
        spouseTree.UpdateLayout();
        var spouseItem = (System.Windows.Controls.TreeViewItem)spouseTree.ItemContainerGenerator.ContainerFromItem(spouseNode);
        spouseItem.IsExpanded = true;
        spouseTree.UpdateLayout();
        var spouseEntryItem = (System.Windows.Controls.TreeViewItem)spouseItem.ItemContainerGenerator.ContainerFromItem(spouseEntry);
        spouseEntryItem.IsSelected = true;
        ((System.Windows.Controls.Button)main.FindName("locateRelationshipTargetButton"))
            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (spouseTree.SelectedItem is not TargetMemberTreeNode spouseTarget || spouseTarget.PersonId != originalPerson ||
            genealogy.GetSelectedPersonForEditing().Id != originalPerson)
            throw new InvalidOperationException("反向配偶关系未定位到原主体。");
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == spouseId);
        var spouseLinks = await genealogy.GetTargetLinkRemovalCandidatesAsync();
        await genealogy.RemoveTargetLinkAsync(spouseLinks.Single(c => c.Kind == TargetLinkRemovalKind.Relationship));
        var remainingUnion = (await genealogy.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.Kind == TargetLinkRemovalKind.FamilyUnion);
        await genealogy.RemoveTargetLinkAsync(remainingUnion);
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == spouseId);
        await genealogy.DeleteUnrelatedPersonAsync();
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == originalPerson);

        async Task QuickUnlinkAsync(bool confirm)
        {
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<TargetLinkRemovalDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                try
                {
                    var list = (System.Windows.Controls.ListBox)dialog.FindName("CandidateList");
                    var preview = ((System.Windows.Controls.TextBox)dialog.FindName("PreviewBox")).Text;
                    if (list.Items.Count != 1 || dialog.SelectedCandidate?.Id != quickOriginal.Id ||
                        !preview.Contains(quickChild.ToString()) || !preview.Contains(originalPerson.ToString()) ||
                        !preview.Contains("快捷备注") || !preview.Contains("快捷来源"))
                        throw new InvalidOperationException("快捷解除预览未绑定正确关系及资料。");
                    if (confirm)
                    {
                        ((System.Windows.Controls.CheckBox)dialog.FindName("ConfirmationBox")).IsChecked = true;
                        ((System.Windows.Controls.Button)dialog.FindName("RemoveButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    else dialog.Close();
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            };
            timer.Start();
            typeof(MainWindow).GetMethod("RemoveBiologicalParentRelationship_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(main, [main, new RoutedEventArgs()]);
            var until = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled) && until.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
            if (timer.IsEnabled || !panel.IsEnabled) { timer.Stop(); throw new TimeoutException("快捷解除窗口未完成。"); }
            if (failure is not null) throw failure;
        }

        genealogy.NewPersonName = "临时删除人物";
        await genealogy.AddPersonAsync();
        var temporaryPerson = genealogy.People.Single(p => p.Name == "临时删除人物").Id;
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == temporaryPerson);
        await genealogy.AddTargetRelationshipAsync(ZupuPaiBan.Models.PersonRelationshipKind.Spouse, originalPerson, null, null);
        await ShowPersonDeletePreviewAsync(expectBlocked: true, confirm: false);
        var link = (await genealogy.GetTargetLinkRemovalCandidatesAsync()).Single();
        await genealogy.RemoveTargetLinkAsync(link);
        await ShowPersonDeletePreviewAsync(expectBlocked: false, confirm: false);
        if (!genealogy.People.Any(p => p.Id == temporaryPerson)) throw new InvalidOperationException("取消删除后人物消失。");
        await ShowPersonDeletePreviewAsync(expectBlocked: false, confirm: true);
        if (genealogy.People.Any(p => p.Id == temporaryPerson)) throw new InvalidOperationException("确认删除后人物仍存在。");

        async Task ShowPersonDeletePreviewAsync(bool expectBlocked, bool confirm)
        {
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<PersonDeletePreviewDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                var button = (System.Windows.Controls.Button)dialog.FindName("DeleteButton");
                var list = (System.Windows.Controls.ListBox)dialog.FindName("LinkList");
                if (button.IsEnabled == expectBlocked || (list.Items.Count > 0) != expectBlocked)
                    failure = new InvalidOperationException("删除预览的关联或确认状态错误。");
                if (confirm) button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                else dialog.Close();
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("previewPersonDeleteButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var deadline = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled) && deadline.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
            timer.Stop();
            if (failure is not null) throw failure;
            if (!panel.IsEnabled || main.OwnedWindows.OfType<PersonDeletePreviewDialog>().Any(w => w.IsVisible)) throw new TimeoutException("删除预览未完成。");
        }
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

        await MoveBranchAsync(firstBranch, secondBranch, confirm: false);
        if (genealogy.Branches.Single(b => b.Id == firstBranch).ParentBranchId is not null)
            throw new InvalidOperationException("取消移动后房支发生变化。");
        await MoveBranchAsync(firstBranch, secondBranch);
        if (!genealogy.ParentBranchChoices.Any(b => b.Id == thirdBranch && b.Label.Contains("次房 / 新长房 / 新次支 / 三支")) || genealogy.People.Single().BranchId != thirdBranch)
            throw new InvalidOperationException("移动后子树或人物归属不正确。");
        await MoveBranchAsync(firstBranch, null);
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        if (!genealogy.ParentBranchChoices.Any(b => b.Id == thirdBranch && b.Label.Contains("新长房 / 新次支 / 三支")) || genealogy.People.Single().BranchId != thirdBranch)
            throw new InvalidOperationException("移动到顶层并重载后结果不正确。");

        await CreateChildAsync(firstBranch, "临时房");
        var temporary = genealogy.Branches.Single(b => b.Name == "临时房").Id;
        await CreateChildAsync(temporary, "临时支");
        var temporaryChild = genealogy.Branches.Single(b => b.Name == "临时支").Id;
        await EditBranchAsync(thirdBranch, temporary);
        ((System.Windows.Controls.ComboBox)main.FindName("renameBranchSelector")).SelectedItem = genealogy.ParentBranchChoices.Single(b => b.Id == temporary);
        var cancelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        cancelTimer.Tick += (_, _) =>
        {
            var dialog = main.OwnedWindows.OfType<BranchReassignmentDialog>().FirstOrDefault(w => w.IsVisible);
            if (dialog is null) return;
            cancelTimer.Stop();
            dialog.Close();
        };
        cancelTimer.Start();
        ((System.Windows.Controls.Button)main.FindName("reassignDeleteBranchButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        cancelTimer.Stop();
        if (!genealogy.Branches.Any(b => b.Id == temporary) || genealogy.People.Single().BranchId != temporary)
            throw new InvalidOperationException("取消安置后数据发生改变。");
        var reassignTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        reassignTimer.Tick += (_, _) =>
        {
            var dialog = main.OwnedWindows.OfType<BranchReassignmentDialog>().FirstOrDefault(w => w.IsVisible);
            if (dialog is null) return;
            reassignTimer.Stop();
            var childBox = (System.Windows.Controls.ComboBox)dialog.FindName("ChildTarget");
            var personBox = (System.Windows.Controls.ComboBox)dialog.FindName("PersonTarget");
            childBox.SelectedItem = childBox.Items.Cast<BranchReassignmentDialog.Destination>().Single(d => d.Id == firstBranch);
            personBox.SelectedItem = personBox.Items.Cast<BranchReassignmentDialog.Destination>().Single(d => d.Id is null);
            var confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            confirmTimer.Tick += (_, _) =>
            {
                var confirmation = FindWindow(null, "确认删除房支");
                if (confirmation == IntPtr.Zero) return;
                GetWindowThreadProcessId(confirmation, out var ownerPid);
                if (ownerPid != (uint)Environment.ProcessId) return;
                confirmTimer.Stop();
                PostMessage(confirmation, 0x0111, new IntPtr(6), IntPtr.Zero);
            };
            confirmTimer.Start();
            ((System.Windows.Controls.Button)dialog.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            confirmTimer.Stop();
        };
        reassignTimer.Start();
        ((System.Windows.Controls.Button)main.FindName("reassignDeleteBranchButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        reassignTimer.Stop();
        var reassignWait = Stopwatch.StartNew();
        while (!panel.IsEnabled && reassignWait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (!panel.IsEnabled || genealogy.Branches.Any(b => b.Id == temporary) ||
            genealogy.Branches.Single(b => b.Id == temporaryChild).ParentBranchId != firstBranch || genealogy.People.Single().BranchId is not null)
            throw new InvalidOperationException("界面安置并删除房支结果不正确。");
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        await EditBranchAsync(null, thirdBranch);
        await ConfirmDeleteBranchAsync(temporaryChild, true);

        async Task MoveBranchAsync(Guid branchId, Guid? parentId, bool confirm = true)
        {
            ((System.Windows.Controls.ComboBox)main.FindName("renameBranchSelector")).SelectedItem = genealogy.ParentBranchChoices.Single(b => b.Id == branchId);
            ((System.Windows.Controls.CheckBox)main.FindName("moveBranchToRoot")).IsChecked = parentId is null;
            if (parentId is Guid id)
                ((System.Windows.Controls.ComboBox)main.FindName("moveBranchTargetSelector")).SelectedItem = genealogy.ParentBranchChoices.Single(b => b.Id == id);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = FindWindow(null, "确认移动房支");
                if (dialog == IntPtr.Zero) return;
                GetWindowThreadProcessId(dialog, out var ownerPid);
                if (ownerPid != (uint)Environment.ProcessId) return;
                timer.Stop();
                PostMessage(dialog, 0x0111, new IntPtr(confirm ? 6 : 7), IntPtr.Zero);
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("moveBranchButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            timer.Stop();
            var until = Stopwatch.StartNew();
            while (!panel.IsEnabled && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (!panel.IsEnabled || (confirm && genealogy.Branches.Single(b => b.Id == branchId).ParentBranchId != parentId))
                throw new InvalidOperationException("界面未完成房支移动。");
        }

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
                if (!box.Items.Cast<object>().Any(item =>
                    item.GetType().GetProperty("Label")!.GetValue(item)!.ToString() is string label &&
                    (label.Contains("长房 / 次支") || label.Contains("新长房 / 新次支"))))
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
        await genealogy.AddTargetRelationshipAsync(ZupuPaiBan.Models.PersonRelationshipKind.LostAncestry, null, "旧记", "旧谱");
        var lostId = (await genealogy.GetTargetLinkRemovalCandidatesAsync()).Single().Id;
        var cancelRelationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        cancelRelationTimer.Tick += (_, _) =>
        {
            var dialog = main.OwnedWindows.OfType<TargetRelationshipDialog>().SingleOrDefault();
            if (dialog is null) return;
            cancelRelationTimer.Stop();
            dialog.Close();
        };
        cancelRelationTimer.Start();
        typeof(MainWindow).GetMethod("TargetShortcut_AddRelationship_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(main, [main, new RoutedEventArgs()]);
        cancelRelationTimer.Stop();
        if ((await genealogy.GetTargetLinkRemovalCandidatesAsync()).Count != 1)
            throw new InvalidOperationException("取消新增关系后记录发生变化。");
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

        var ancestryDetails = genealogy.TargetTree.Single(n => n.PersonId == originalPerson).Details;
        if (!ancestryDetails.Any(d => d.Contains("上系失考") && d.Contains("旧记") && d.Contains("旧谱")) ||
            !ancestryDetails.Any(d => d.Contains("生物父母") && d.Contains("关系对方")))
            throw new InvalidOperationException("重载后未同时显示失考及新增上系记录。");
        var sourceNode = genealogy.TargetTree.Single(n => n.PersonId == originalPerson);
        var targetPerson = genealogy.People.Single(p => p.Name == "关系对方");
        var relationEntry = sourceNode.Entries.Single(e => e.OtherPersonId == targetPerson.Id);
        tree.UpdateLayout();
        var sourceItem = (System.Windows.Controls.TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(sourceNode);
        sourceItem.IsExpanded = true;
        tree.UpdateLayout();
        var entryItem = (System.Windows.Controls.TreeViewItem)sourceItem.ItemContainerGenerator.ContainerFromItem(relationEntry);
        entryItem.IsSelected = true;
        ((System.Windows.Controls.Button)main.FindName("locateRelationshipTargetButton"))
            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (tree.SelectedItem is not TargetMemberTreeNode located || located.PersonId != targetPerson.Id ||
            genealogy.GetSelectedPersonForEditing().Id != targetPerson.Id)
            throw new InvalidOperationException("关系对方定位未选中正确人物。");
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == originalPerson);
        Exception? previewFailure = null;
        var previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        previewTimer.Tick += (_, _) =>
        {
            var dialog = main.OwnedWindows.OfType<TargetLinkRemovalDialog>().FirstOrDefault(w => w.IsVisible);
            if (dialog is null) return;
            previewTimer.Stop();
            try
            {
                var list = (System.Windows.Controls.ListBox)dialog.FindName("CandidateList");
                var preview = (System.Windows.Controls.TextBox)dialog.FindName("PreviewBox");
                var confirmation = (System.Windows.Controls.CheckBox)dialog.FindName("ConfirmationBox");
                var button = (System.Windows.Controls.Button)dialog.FindName("RemoveButton");
                list.SelectedItem = list.Items.Cast<TargetLinkRemovalCandidate>().Single(c => c.Id == lostId);
                if (!preview.Text.Contains(lostId.ToString()) || !preview.Text.Contains(originalPerson.ToString()) ||
                    !preview.Text.Contains("旧记") || !preview.Text.Contains("旧谱") || !preview.Text.Contains("未详"))
                    throw new InvalidOperationException("失考记录预览未显示快照资料。");
                confirmation.IsChecked = true;
                list.SelectedItem = list.Items.Cast<TargetLinkRemovalCandidate>().Single(c => c.Id != lostId);
                if (confirmation.IsChecked == true || button.IsEnabled || !preview.Text.Contains(targetPerson.Id.ToString()) ||
                    !preview.Text.Contains("界面谱载备注") || !preview.Text.Contains("界面谱牒来源"))
                    throw new InvalidOperationException("切换记录后预览或确认状态不正确。");
            }
            catch (Exception ex) { previewFailure = ex; }
            finally { dialog.Close(); }
        };
        previewTimer.Start();
        typeof(MainWindow).GetMethod("TargetShortcut_RemoveLink_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(main, [main, new RoutedEventArgs()]);
        var previewDeadline = Stopwatch.StartNew();
        while ((previewTimer.IsEnabled || !panel.IsEnabled) && previewDeadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (previewTimer.IsEnabled || !panel.IsEnabled) { previewTimer.Stop(); throw new TimeoutException("解除预览未完成。"); }
        if (previewFailure is not null) throw previewFailure;
        if ((await genealogy.GetTargetLinkRemovalCandidatesAsync()).Count != 2)
            throw new InvalidOperationException("取消解除后记录发生变化。");
        var lostCandidate = (await genealogy.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.Id == lostId);
        await genealogy.RemoveTargetLinkAsync(lostCandidate);

        genealogy.SelectedPerson = genealogy.People.Single(p => p.Name == "搜索验收人物");
        var beforeCorrection = (await genealogy.GetTargetLinkRemovalCandidatesAsync()).Single();
        await CorrectParentKindAsync(false);
        if ((await genealogy.GetTargetLinkRemovalCandidatesAsync()).Single().RelationshipSnapshot!.Kind != ZupuPaiBan.Models.PersonRelationshipKind.BiologicalParent)
            throw new InvalidOperationException("取消更正后类型发生变化。");
        await CorrectParentKindAsync(true);
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Name == "搜索验收人物");
        var corrected = (await genealogy.GetTargetLinkRemovalCandidatesAsync()).Single();
        if (corrected.Id != beforeCorrection.Id || corrected.RelationshipSnapshot!.Kind != ZupuPaiBan.Models.PersonRelationshipKind.AdoptiveParent ||
            corrected.RelationshipSnapshot.Note != "界面谱载备注" || corrected.RelationshipSnapshot.SourceCitation != "界面谱牒来源")
            throw new InvalidOperationException("更正或重载后关系身份及资料不正确。");

        async Task CorrectParentKindAsync(bool confirm)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<ParentRelationshipCorrectionDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                if (!confirm) { dialog.Close(); return; }
                ((System.Windows.Controls.ListBox)dialog.FindName("CandidateList")).SelectedIndex = 0;
                ((System.Windows.Controls.ComboBox)dialog.FindName("KindBox")).SelectedValue = ZupuPaiBan.Models.PersonRelationshipKind.AdoptiveParent;
                var confirmationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                confirmationTimer.Tick += (_, _) =>
                {
                    var message = FindWindow(null, "确认更正");
                    if (message == IntPtr.Zero) return;
                    GetWindowThreadProcessId(message, out var pid);
                    if (pid != (uint)Environment.ProcessId) return;
                    confirmationTimer.Stop();
                    PostMessage(message, 0x0111, new IntPtr(6), IntPtr.Zero);
                };
                confirmationTimer.Start();
                ((System.Windows.Controls.Button)dialog.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                confirmationTimer.Stop();
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("correctParentKindButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled) && until.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
            if (timer.IsEnabled || !panel.IsEnabled) { timer.Stop(); throw new TimeoutException("更正关系窗口未完成。"); }
        }

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

        await AddUnknownUnionAsync(false);
        await AddUnknownUnionAsync(true);
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Name == "搜索验收人物");
        var unknown = (await genealogy.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.UnionSnapshot is { PartnerTwoId: null });
        if (unknown.UnionSnapshot!.StartDateText != "未详始年" || unknown.UnionSnapshot.Note != "未详备注")
            throw new InvalidOperationException("未详婚配重载后资料不正确。");
        var unknownPreviewDialog = new TargetLinkRemovalDialog([unknown]);
        ((System.Windows.Controls.ListBox)unknownPreviewDialog.FindName("CandidateList")).SelectedIndex = 0;
        var unknownPreview = ((System.Windows.Controls.TextBox)unknownPreviewDialog.FindName("PreviewBox")).Text;
        if (!unknownPreview.Contains("未详") || !unknownPreview.Contains(unknown.Id.ToString()) ||
            !unknownPreview.Contains("未详始年") || !unknownPreview.Contains("未详备注"))
            throw new InvalidOperationException("另一方未详的婚配预览不正确。");
        unknownPreviewDialog.Close();
        var unknownEditTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        unknownEditTimer.Tick += (_, _) =>
        {
            var dialog = main.OwnedWindows.OfType<TargetFamilyUnionDetailsDialog>().FirstOrDefault(w => w.IsVisible);
            if (dialog is null) return;
            unknownEditTimer.Stop();
            var list = (System.Windows.Controls.ListBox)dialog.FindName("CandidateList");
            list.SelectedItem = list.Items.Cast<TargetLinkRemovalCandidate>().Single(c => c.Id == unknown.Id);
            ((System.Windows.Controls.TextBox)dialog.FindName("NoteBox")).Text = "已编辑未详";
            ((System.Windows.Controls.Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        };
        unknownEditTimer.Start();
        ((System.Windows.Controls.Button)main.FindName("editFamilyUnionDetailsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var editUnknownWait = Stopwatch.StartNew();
        while ((unknownEditTimer.IsEnabled || !panel.IsEnabled) && editUnknownWait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (unknownEditTimer.IsEnabled || !panel.IsEnabled) { unknownEditTimer.Stop(); throw new TimeoutException("未详婚配编辑未完成。"); }
        await using (var verifyEdit = coordinator.CreateContext())
            if ((await verifyEdit.FamilyUnions.AsNoTracking().SingleAsync(u => u.Id == unknown.Id)).Note != "已编辑未详")
                throw new InvalidOperationException("未详婚配编辑未保存。");
        await CorrectUnionKindAsync(false);
        await CorrectUnionKindAsync(true);
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Name == "搜索验收人物");
        await using (var verifyKind = coordinator.CreateContext())
        {
            var saved = await verifyKind.FamilyUnions.AsNoTracking().SingleAsync(u => u.Id == unknown.Id);
            if (saved.Kind != ZupuPaiBan.Models.FamilyUnionKind.Partnership || saved.PartnerTwoId is not null || saved.Note != "已编辑未详")
                throw new InvalidOperationException("婚配类型更正后资料不正确。");
        }

        async Task CorrectUnionKindAsync(bool confirm)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<UnionKindCorrectionDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                if (!confirm) { dialog.Close(); return; }
                var list = (System.Windows.Controls.ListBox)dialog.FindName("CandidateList");
                list.SelectedItem = list.Items.Cast<TargetLinkRemovalCandidate>().Single(c => c.Id == unknown.Id);
                ((System.Windows.Controls.ComboBox)dialog.FindName("KindBox")).SelectedValue = ZupuPaiBan.Models.FamilyUnionKind.Partnership;
                var confirmationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                confirmationTimer.Tick += (_, _) =>
                {
                    var message = FindWindow(null, "确认更正婚配类型");
                    if (message == IntPtr.Zero) return;
                    GetWindowThreadProcessId(message, out var pid);
                    if (pid != (uint)Environment.ProcessId) return;
                    confirmationTimer.Stop();
                    PostMessage(message, 0x0111, new IntPtr(6), IntPtr.Zero);
                };
                confirmationTimer.Start();
                ((System.Windows.Controls.Button)dialog.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                confirmationTimer.Stop();
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("correctUnionKindButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled) && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (timer.IsEnabled || !panel.IsEnabled) { timer.Stop(); throw new TimeoutException("婚配类型更正未完成。"); }
            if (!confirm)
            {
                await using var verifyCancel = coordinator.CreateContext();
                if ((await verifyCancel.FamilyUnions.AsNoTracking().SingleAsync(u => u.Id == unknown.Id)).Kind != ZupuPaiBan.Models.FamilyUnionKind.Unknown)
                    throw new InvalidOperationException("取消后婚配类型发生改变。");
            }
        }
        await CompleteUnionAsync(false);
        await CompleteUnionAsync(true);
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Name == "搜索验收人物");
        await using (var verifyComplete = coordinator.CreateContext())
        {
            var union = await verifyComplete.FamilyUnions.AsNoTracking().SingleAsync(u => u.Id == unknown.Id);
            if (union.PartnerTwoId is null || union.Note != "已编辑未详" || union.StartDateText != "未详始年")
                throw new InvalidOperationException("婚配补全重载后身份或资料不正确。");
        }

        async Task CompleteUnionAsync(bool confirm)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<CompleteUnionPartnerDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                if (!confirm) { dialog.Close(); return; }
                ((System.Windows.Controls.ListBox)dialog.FindName("CandidateList")).SelectedIndex = 0;
                ((System.Windows.Controls.ComboBox)dialog.FindName("PartnerBox")).SelectedItem = genealogy.People.Single(p => p.Name == "关系对方");
                var confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                confirmTimer.Tick += (_, _) =>
                {
                    var message = FindWindow(null, "确认补全");
                    if (message == IntPtr.Zero) return;
                    GetWindowThreadProcessId(message, out var pid);
                    if (pid != (uint)Environment.ProcessId) return;
                    confirmTimer.Stop();
                    PostMessage(message, 0x0111, new IntPtr(6), IntPtr.Zero);
                };
                confirmTimer.Start();
                ((System.Windows.Controls.Button)dialog.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                confirmTimer.Stop();
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("completeUnionPartnerButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled) && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (timer.IsEnabled || !panel.IsEnabled) { timer.Stop(); throw new TimeoutException("婚配补全未完成。"); }
            if (!confirm)
            {
                await using var verifyCancel = coordinator.CreateContext();
                if ((await verifyCancel.FamilyUnions.AsNoTracking().SingleAsync(u => u.Id == unknown.Id)).PartnerTwoId is not null)
                    throw new InvalidOperationException("取消补全后婚配发生改变。");
            }
        }
        Exception? unionPreviewFailure = null;
        var removalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        removalTimer.Tick += (_, _) =>
        {
            var dialog = main.OwnedWindows.OfType<TargetLinkRemovalDialog>().FirstOrDefault(w => w.IsVisible);
            if (dialog is null) return;
            removalTimer.Stop();
            var list = (System.Windows.Controls.ListBox)dialog.FindName("CandidateList");
            list.SelectedItem = list.Items.Cast<TargetLinkRemovalCandidate>().Single(c => c.Id == unknown.Id);
            var unionPreview = ((System.Windows.Controls.TextBox)dialog.FindName("PreviewBox")).Text;
            if (!unionPreview.Contains(unknown.Id.ToString()) || !unionPreview.Contains("未详始年") || !unionPreview.Contains("已编辑未详"))
                unionPreviewFailure = new InvalidOperationException("婚配预览未显示记录及保存资料。");
            ((System.Windows.Controls.CheckBox)dialog.FindName("ConfirmationBox")).IsChecked = true;
            ((System.Windows.Controls.Button)dialog.FindName("RemoveButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        };
        removalTimer.Start();
        typeof(MainWindow).GetMethod("TargetShortcut_RemoveLink_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [main, new RoutedEventArgs()]);
        var removeUnknownWait = Stopwatch.StartNew();
        while ((removalTimer.IsEnabled || !panel.IsEnabled) && removeUnknownWait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (removalTimer.IsEnabled || !panel.IsEnabled) { removalTimer.Stop(); throw new TimeoutException("未详婚配解除未完成。"); }
        if (unionPreviewFailure is not null) throw unionPreviewFailure;
        await using (var verifyUnknown = coordinator.CreateContext())
            if (await verifyUnknown.FamilyUnions.AnyAsync(u => u.Id == unknown.Id)) throw new InvalidOperationException("未详婚配未解除。");

        async Task AddUnknownUnionAsync(bool save)
        {
            genealogy.SelectedPerson = genealogy.People.Single(p => p.Name == "搜索验收人物");
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<TargetFamilyUnionDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                var partner = (System.Windows.Controls.ComboBox)dialog.FindName("PartnerTwoBox");
                var check = (System.Windows.Controls.CheckBox)dialog.FindName("UnknownPartnerCheck");
                if (check.IsChecked == true || !partner.IsEnabled || partner.SelectedItem is not null)
                    failure = new InvalidOperationException("新增婚配默认状态不正确。");
                var saveButton = (System.Windows.Controls.Button)dialog.FindName("SaveButton");
                if (saveButton.IsEnabled) failure = new InvalidOperationException("未选择对方时未阻止保存。");
                partner.SelectedIndex = 0;
                check.IsChecked = true;
                if (partner.IsEnabled || partner.SelectedItem is not null) failure = new InvalidOperationException("切换未详未清除对方。");
                check.IsChecked = false;
                if (!partner.IsEnabled || partner.SelectedItem is not null) failure = new InvalidOperationException("切回选择人物时保留旧选择。");
                check.IsChecked = true;
                ((System.Windows.Controls.ComboBox)dialog.FindName("KindBox")).SelectedValue = ZupuPaiBan.Models.FamilyUnionKind.Unknown;
                ((System.Windows.Controls.TextBox)dialog.FindName("StartDateBox")).Text = "未详始年";
                ((System.Windows.Controls.TextBox)dialog.FindName("NoteBox")).Text = "未详备注";
                if (save) ((System.Windows.Controls.Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                else dialog.Close();
            };
            timer.Start();
            typeof(MainWindow).GetMethod("TargetShortcut_AddFamilyUnion_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [main, new RoutedEventArgs()]);
            var until = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled) && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (timer.IsEnabled || !panel.IsEnabled) { timer.Stop(); throw new TimeoutException("新增未详婚配未完成。"); }
            if (failure is not null) throw failure;
            if (!save && (await genealogy.GetTargetLinkRemovalCandidatesAsync()).Any(c => c.UnionSnapshot is { PartnerTwoId: null }))
                throw new InvalidOperationException("取消后仍写入未详婚配。");
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

        await EditGenealogyProfileAsync("", "", "张", "初始简介");
        await genealogy.LoadAsync();
        genealogySelector.SelectedItem = genealogy.Genealogies.Single(g => g.Id == originalGenealogyId);
        var profileReloadDeadline = Stopwatch.StartNew();
        while ((genealogy.SelectedGenealogy?.Id != originalGenealogyId || genealogy.People.Count != 2) && profileReloadDeadline.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        if (genealogy.SelectedGenealogy?.Id != originalGenealogyId || genealogy.People.Count != 2)
            throw new TimeoutException("重载族谱资料后未恢复选择。");
        await EditGenealogyProfileAsync("张", "初始简介", "赵", "更正简介");
        await EditGenealogyProfileAsync("赵", "更正简介", "", "");
        await using (var verifyProfile = coordinator.CreateContext())
        {
            var own = await verifyProfile.Genealogies.SingleAsync(g => g.Id == originalGenealogyId);
            var other = await verifyProfile.Genealogies.SingleAsync(g => g.Id == duplicateId);
            if (own.Surname is not null || own.Description is not null || other.Surname is not null || other.Description is not null ||
                await verifyProfile.PersonRelationships.CountAsync(r => r.GenealogyId == originalGenealogyId) != 1 ||
                await verifyProfile.FamilyUnions.CountAsync(u => u.GenealogyId == originalGenealogyId) != 1 ||
                await verifyProfile.FamilyBranches.CountAsync(b => b.GenealogyId == originalGenealogyId) != 4)
                throw new InvalidOperationException("族谱资料清空或隔离结果不正确。");
        }

        var importSourceId = Guid.NewGuid();
        await using (var importContext = coordinator.CreateContext())
        {
            importContext.GenealogyImportRecords.AddRange(
                new ZupuPaiBan.Models.GenealogyImportRecord { GenealogyId = originalGenealogyId, SourceKind = "PuJiang.InternalJson", ImportMode = "Default", SourceFormatVersion = 1, SourceGenealogyId = importSourceId, SourceDocumentFingerprint = "internal-fingerprint" },
                new ZupuPaiBan.Models.GenealogyImportRecord { GenealogyId = originalGenealogyId, SourceKind = "PuJiang.GEDCOM-5.5.x", ImportMode = "Copy", SourceFormatVersion = 1, SourceGenealogyId = Guid.NewGuid(), SourceDocumentFingerprint = "external-fingerprint" });
            await importContext.SaveChangesAsync();
        }
        await ViewHistoryAsync(originalGenealogyId, false);
        genealogySelector.SelectedItem = genealogy.Genealogies.Single(g => g.Id == duplicateId);
        var historySwitch = Stopwatch.StartNew();
        while (genealogy.SelectedGenealogy?.Id != duplicateId && historySwitch.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (genealogy.SelectedGenealogy?.Id != duplicateId) throw new TimeoutException("切换空来源族谱失败。");
        await ViewHistoryAsync(duplicateId, true);
        genealogySelector.SelectedItem = genealogy.Genealogies.Single(g => g.Id == originalGenealogyId);
        historySwitch.Restart();
        while ((genealogy.SelectedGenealogy?.Id != originalGenealogyId || genealogy.People.Count != 2) && historySwitch.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        if (genealogy.SelectedGenealogy?.Id != originalGenealogyId || genealogy.People.Count != 2) throw new TimeoutException("切回原族谱失败。");
        await ViewHistoryAsync(originalGenealogyId, false);
        await using (var verifyHistory = coordinator.CreateContext())
        {
            if (await verifyHistory.GenealogyImportRecords.CountAsync() != 2 || await verifyHistory.People.CountAsync(p => p.GenealogyId == originalGenealogyId) != 2)
                throw new InvalidOperationException("查看来源改变了记录或图数据。");
        }

        async Task ViewHistoryAsync(Guid expectedId, bool empty)
        {
            Exception? failure = null;
            var historyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            historyTimer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<GenealogyImportHistoryDialog>().SingleOrDefault();
                if (dialog is null) return;
                historyTimer.Stop();
                var label = (System.Windows.Controls.TextBlock)dialog.FindName("GenealogyLabel");
                var body = (System.Windows.Controls.TextBox)dialog.FindName("HistoryText");
                if (!label.Text.Contains(expectedId.ToString()) || !body.IsReadOnly ||
                    (empty ? body.Text != "当前族谱没有导入来源记录。" :
                        !body.Text.Contains("internal-fingerprint") || !body.Text.Contains("external-fingerprint") ||
                        !body.Text.Contains($"来源族谱 ID：{importSourceId}") || !body.Text.Contains("来源标识（由指纹派生）")))
                    failure = new InvalidOperationException("导入来源窗口内容、隔离或只读状态不正确。");
                ((System.Windows.Controls.Button)dialog.FindName("CloseButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            historyTimer.Start();
            ((System.Windows.Controls.Button)main.FindName("viewImportHistoryButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while (historyTimer.IsEnabled && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (historyTimer.IsEnabled) { historyTimer.Stop(); throw new TimeoutException("未显示导入来源窗口。"); }
            if (failure is not null) throw failure;
        }

        async Task EditGenealogyProfileAsync(string expectedSurname, string expectedDescription, string surname, string description)
        {
            Exception? failure = null;
            var profileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            profileTimer.Tick += (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<GenealogyProfileDialog>().SingleOrDefault();
                if (dialog is null) return;
                profileTimer.Stop();
                var surnameBox = (System.Windows.Controls.TextBox)dialog.FindName("SurnameBox");
                var descriptionBox = (System.Windows.Controls.TextBox)dialog.FindName("DescriptionBox");
                if (surnameBox.Text != expectedSurname || descriptionBox.Text != expectedDescription)
                    failure = new InvalidOperationException("重开族谱资料未显示已保存内容。");
                surnameBox.Text = surname; descriptionBox.Text = description;
                ((System.Windows.Controls.Button)dialog.FindName("SaveButton"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            profileTimer.Start();
            ((System.Windows.Controls.Button)main.FindName("editGenealogyProfileButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while ((profileTimer.IsEnabled || !panel.IsEnabled) && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            if (profileTimer.IsEnabled || !panel.IsEnabled) { profileTimer.Stop(); throw new TimeoutException("编辑族谱资料后界面未恢复。"); }
            if (failure is not null) throw failure;
        }

        var priorGenealogy = genealogy.SelectedGenealogy!;
        genealogy.NewName = "路径验收谱";
        await genealogy.CreateAsync();
        var pathGenealogy = genealogy.SelectedGenealogy!;
        genealogy.NewPersonName = "路径主体"; await genealogy.AddPersonAsync();
        genealogy.NewPersonName = "路径父母"; await genealogy.AddPersonAsync();
        genealogy.NewPersonName = "路径养亲"; await genealogy.AddPersonAsync();
        var pathSubject = genealogy.People.Single(p => p.Name == "路径主体").Id;
        var pathParent = genealogy.People.Single(p => p.Name == "路径父母").Id;
        var pathAdoptive = genealogy.People.Single(p => p.Name == "路径养亲").Id;
        genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == pathSubject);
        await genealogy.AddTargetRelationshipAsync(ZupuPaiBan.Models.PersonRelationshipKind.BiologicalParent, pathParent);
        await genealogy.AddTargetRelationshipAsync(ZupuPaiBan.Models.PersonRelationshipKind.AdoptiveParent, pathAdoptive);
        await genealogy.AddTargetRelationshipAsync(ZupuPaiBan.Models.PersonRelationshipKind.LostAncestry, null);
        await PreviewPathAsync(false);
        await PreviewPathAsync(true);
        await PreviewFullLineageAsync(false);
        await PreviewFullLineageAsync(true);
        await PreviewLineageTreeAsync(0);
        await PreviewLineageTreeAsync(1);
        await PreviewLineageTreeAsync(2);
        await using (var pathVerify = coordinator.CreateContext())
            if (await pathVerify.PersonRelationships.CountAsync(r => r.GenealogyId == pathGenealogy.Id) != 3)
                throw new InvalidOperationException("只读路径预览修改了原始关系。");
        await genealogy.SelectAsync(priorGenealogy);

        async Task PreviewLineageTreeAsync(int invalidate)
        {
            if (genealogy.SelectedGenealogy?.Id != pathGenealogy.Id) await genealogy.SelectAsync(pathGenealogy);
            genealogy.SelectedPerson = null;
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += async (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<LineageTreeDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                try
                {
                    var tree = (System.Windows.Controls.TreeView)dialog.FindName("LineageTree");
                    var searchBox = (System.Windows.Controls.TextBox)dialog.FindName("SearchBox");
                    var searchResults = (System.Windows.Controls.ComboBox)dialog.FindName("SearchResults");
                    var choices = (System.Windows.Controls.ListBox)dialog.FindName("CandidateList");
                    var status = (System.Windows.Controls.TextBlock)dialog.FindName("StatusText");
                    if (tree.Items.Count != 3 || !tree.Items.Cast<LineageTreeNode>().Single(n => n.PersonId == pathSubject).Projection.NeedsChoice)
                        throw new InvalidOperationException("树形浏览未显示待选及断开人物。");
                    searchBox.Text = "路径";
                    if (searchResults.Items.Count != 3) throw new InvalidOperationException("树形浏览姓名搜索结果错误。");
                    searchBox.Text = pathSubject.ToString();
                    if (searchResults.Items.Count != 1) throw new InvalidOperationException("树形浏览 ID 搜索结果错误。");
                    searchResults.SelectedIndex = 0;
                    ((System.Windows.Controls.Button)dialog.FindName("LocateButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (tree.SelectedItem is not LineageTreeNode selected || selected.PersonId != pathSubject || choices.Items.Count != 2 ||
                        !((System.Windows.Controls.TextBox)dialog.FindName("DetailsText")).Text.Contains("失考"))
                        throw new InvalidOperationException("树形浏览定位或候选显示错误。");
                    choices.SelectedItem = choices.Items.Cast<LineageParentOption>().Single(c => c.Parent.Id == pathParent);
                    ((System.Windows.Controls.Button)dialog.FindName("ChooseButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (invalidate == 0)
                    {
                        ((System.Windows.Controls.TextBox)dialog.FindName("ChartName")).Text = "测试图表";
                        ((System.Windows.Controls.Button)dialog.FindName("SaveChartButton"))
                            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        var charts = (System.Windows.Controls.ComboBox)dialog.FindName("Charts");
                        var wait = Stopwatch.StartNew();
                        while (!status.Text.Contains("已保存") && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
                        if (!status.Text.Contains("已保存")) throw new InvalidOperationException("WPF 保存图表失败：" + status.Text);
                        ((System.Windows.Controls.Button)dialog.FindName("NewChartButton"))
                            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        charts.SelectedIndex = 0;
                        ((System.Windows.Controls.Button)dialog.FindName("OpenChartButton"))
                            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        wait.Restart();
                        while (tree.Items.Count != 2 && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
                        if (tree.Items.Count != 2 || tree.Items.Cast<LineageTreeNode>().Single(n => n.PersonId == pathParent).Children.Single().PersonId != pathSubject)
                            throw new InvalidOperationException("WPF 重开图表未恢复选线：" + status.Text);
                        await using (var invalidDb = coordinator.CreateContext())
                        {
                            var graph = await new GenealogyDataService(invalidDb).GetGraphSnapshotAsync(pathGenealogy.Id);
                            var stale = new LineageChart { GenealogyId = pathGenealogy.Id, Name = "失效图表", Revision = 1, Generation = graph.UnlinkGeneration };
                            stale.Choices.Add(new LineageChartChoice { PersonId = pathSubject, PersonName = "路径人物", ParentId = pathParent,
                                ParentName = "路径父母", RelationshipId = Guid.NewGuid(), Kind = PersonRelationshipKind.BiologicalParent });
                            invalidDb.LineageCharts.Add(stale);
                            await invalidDb.SaveChangesAsync();
                        }
                        ((System.Windows.Controls.Button)dialog.FindName("SaveChartButton"))
                            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        wait.Restart();
                        while (charts.Items.Count < 2 && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
                        charts.SelectedItem = charts.Items.Cast<LineageChart>().Single(c => c.Name == "失效图表");
                        ((System.Windows.Controls.Button)dialog.FindName("OpenChartButton"))
                            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        var invalidList = (System.Windows.Controls.ListBox)dialog.FindName("InvalidChoices");
                        wait.Restart();
                        while (invalidList.Items.Count != 1 && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
                        if (invalidList.Items.Count != 1 || !tree.Items.Cast<LineageTreeNode>().Single(n => n.PersonId == pathSubject).Projection.NeedsChoice)
                            throw new InvalidOperationException("WPF 未保留并阻止失效选线。");
                        ((System.Windows.Controls.Button)dialog.FindName("SaveChartButton"))
                            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        if (!status.Text.Contains("失效选线")) throw new InvalidOperationException("WPF 未阻止保存未处理失效项。");
                        invalidList.SelectedIndex = 0;
                        ((System.Windows.Controls.Button)dialog.FindName("ClearInvalidButton"))
                            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        if (invalidList.Items.Count != 0) throw new InvalidOperationException("WPF 未明确清除失效项。");
                        var reviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                        reviewTimer.Tick += (_, _) =>
                        {
                            var reviewWindow = dialog.OwnedWindows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w.Title == "复核图表");
                            if (reviewWindow is null) return;
                            reviewTimer.Stop();
                            var reviewPanel = (System.Windows.Controls.DockPanel)reviewWindow.Content;
                            var reviewList = reviewPanel.Children.OfType<System.Windows.Controls.ListBox>().Single();
                            if (reviewList.Items.Count != 1) { reviewWindow.DialogResult = false; return; }
                            var reviewButtons = reviewPanel.Children.OfType<System.Windows.Controls.StackPanel>().Single();
                            reviewButtons.Children.OfType<System.Windows.Controls.Button>().Single(b => (string)b.Content == "确认复核")
                                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        };
                        reviewTimer.Start();
                        var reviewMethod = typeof(LineageTreeDialog).GetMethod("ConfirmReview", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        var review = new ChartReview(new LineageChart(), true, new[] {
                            new ChartChoiceReview(new LineageChartChoice { PersonName = "测试", ParentName = "父母" }, null) });
                        if (reviewMethod.Invoke(dialog, new object[] { review }) is not true)
                            throw new InvalidOperationException("WPF 复核窗口未展示和确认选线。");
                        charts.SelectedItem = charts.Items.Cast<LineageChart>().Single(c => c.Name == "测试图表");
                        ((System.Windows.Controls.Button)dialog.FindName("OpenChartButton"))
                            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        wait.Restart();
                        while (tree.Items.Count != 2 && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
                        searchBox.Text = ""; searchBox.Text = pathSubject.ToString(); searchResults.SelectedIndex = 0;
                        ((System.Windows.Controls.Button)dialog.FindName("LocateButton"))
                            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    var parentNode = tree.Items.Cast<LineageTreeNode>().Single(n => n.PersonId == pathParent);
                    if (tree.Items.Count != 2 || parentNode.Children.Single().PersonId != pathSubject || !parentNode.IsExpanded)
                        throw new InvalidOperationException("选择后树形父子边不正确。");
                    parentNode.IsExpanded = false;
                    if (parentNode.IsExpanded) throw new InvalidOperationException("树形折叠失败。");
                    searchBox.Text = ""; searchBox.Text = pathSubject.ToString(); searchResults.SelectedIndex = 0;
                    ((System.Windows.Controls.Button)dialog.FindName("LocateButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (!parentNode.IsExpanded || tree.SelectedItem is not LineageTreeNode located || located.PersonId != pathSubject)
                        throw new InvalidOperationException("搜索定位未展开路径并选中人物。");
                    if (!parentNode.IsPathAncestor || located.IsPathAncestor || !status.Text.Contains("未记录"))
                        throw new InvalidOperationException("搜索后的祖先高亮或路径边界不正确。");
                    tree.UpdateLayout();
                    var parentContainer = tree.ItemContainerGenerator.ContainerFromItem(parentNode) as System.Windows.Controls.TreeViewItem
                        ?? throw new InvalidOperationException("路径祖先容器不存在。");
                    var pathLabel = FindLineageLabel(parentContainer, parentNode)
                        ?? throw new InvalidOperationException("路径祖先标签不存在。");
                    if (pathLabel.Background is not System.Windows.Media.SolidColorBrush pathBrush ||
                        pathBrush.Color != System.Windows.Media.Color.FromRgb(255, 217, 138))
                        throw new InvalidOperationException("路径祖先没有应用高亮样式。");
                    choices.SelectedItem = choices.Items.Cast<LineageParentOption>().Single(c => c.Parent.Id == pathAdoptive);
                    ((System.Windows.Controls.Button)dialog.FindName("ChooseButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    var adoptiveNode = tree.Items.Cast<LineageTreeNode>().Single(n => n.PersonId == pathAdoptive);
                    var originalParentNode = tree.Items.Cast<LineageTreeNode>().Single(n => n.PersonId == pathParent);
                    if (!adoptiveNode.IsPathAncestor || originalParentNode.IsPathAncestor)
                        throw new InvalidOperationException("更换选线后路径高亮未更新。");
                    choices.SelectedItem = choices.Items.Cast<LineageParentOption>().Single(c => c.Parent.Id == pathParent);
                    ((System.Windows.Controls.Button)dialog.FindName("ChooseButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    parentNode = tree.Items.Cast<LineageTreeNode>().Single(n => n.PersonId == pathParent);
                    if (!parentNode.IsPathAncestor) throw new InvalidOperationException("恢复选线后路径高亮不正确。");
                    parentNode.IsExpanded = false;
                    if (parentNode.IsExpanded || parentNode.Children.Single().Projection.SelectedParent?.Parent.Id != pathParent)
                        throw new InvalidOperationException("折叠改变了选线或自动重新展开。");
                    ((System.Windows.Controls.Button)dialog.FindName("ClearSelectionButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (parentNode.IsPathAncestor || tree.SelectedItem is not null)
                        throw new InvalidOperationException("清除选择未清除路径高亮。");
                    searchBox.Text = ""; searchBox.Text = pathSubject.ToString(); searchResults.SelectedIndex = 0;
                    ((System.Windows.Controls.Button)dialog.FindName("LocateButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (!parentNode.IsPathAncestor) throw new InvalidOperationException("重新定位后路径高亮未恢复。");
                    if (invalidate == 0)
                    {
                        var views = (System.Windows.Controls.TabControl)dialog.FindName("LineageViews");
                        views.SelectedIndex = 1;
                        var canvas = (System.Windows.Controls.Canvas)dialog.FindName("GraphCanvas");
                        var rootPanels = canvas.Children.OfType<System.Windows.Controls.Grid>().ToList();
                        if (rootPanels.Count != 2) throw new InvalidOperationException("图形根节点错误。");
                        var toggle = rootPanels.SelectMany(p => p.Children.OfType<System.Windows.Controls.Button>())
                            .Single(b => b.Tag is Guid id && id == pathParent && ((string)b.Content).StartsWith("展开"));
                        toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        if (canvas.Children.OfType<System.Windows.Shapes.Line>().Count() != 1) throw new InvalidOperationException("图形展开未绘制连线。");
                        searchBox.Text = ""; searchBox.Text = pathSubject.ToString(); searchResults.SelectedIndex = 0;
                        ((System.Windows.Controls.Button)dialog.FindName("LocateButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        if (!canvas.Children.OfType<System.Windows.Shapes.Line>().Any(l => l.Stroke == System.Windows.Media.Brushes.DarkOrange))
                            throw new InvalidOperationException("图形路径连线未高亮。");
                        var scale = (System.Windows.Media.ScaleTransform)dialog.FindName("GraphScale");
                        ((System.Windows.Controls.Button)dialog.FindName("GraphZoomIn")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        if (scale.ScaleX <= 1) throw new InvalidOperationException("图形缩放未生效。");
                        toggle = canvas.Children.OfType<System.Windows.Controls.Grid>().SelectMany(p => p.Children.OfType<System.Windows.Controls.Button>())
                            .Single(b => b.Tag is Guid id && id == pathParent && ((string)b.Content).StartsWith("折叠"));
                        toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        if (canvas.Children.OfType<System.Windows.Shapes.Line>().Any()) throw new InvalidOperationException("图形折叠未隐藏下系。");
                    }
                    if (invalidate != 0)
                    {
                        await genealogy.SelectAsync(invalidate == 1 ? priorGenealogy : pathGenealogy);
                        if (tree.Items.Count != 0 || choices.Items.Count != 0 || parentNode.IsPathAncestor || !status.Text.Contains("失效"))
                            throw new InvalidOperationException("切谱或刷新后树形浏览未失效。");
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { dialog.Close(); }
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("lineageTreeButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled || main.OwnedWindows.OfType<LineageTreeDialog>().Any(w => w.IsVisible)) && until.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
            if (timer.IsEnabled || !panel.IsEnabled) { timer.Stop(); throw new TimeoutException("树形世系浏览未完成。"); }
            if (failure is not null) throw failure;
        }

        async Task PreviewFullLineageAsync(bool invalidate)
        {
            if (genealogy.SelectedGenealogy?.Id != pathGenealogy.Id) await genealogy.SelectAsync(pathGenealogy);
            genealogy.SelectedPerson = null;
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += async (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<LineagePreviewDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                try
                {
                    var people = (System.Windows.Controls.ListBox)dialog.FindName("PersonList");
                    var choices = (System.Windows.Controls.ListBox)dialog.FindName("CandidateList");
                    var status = (System.Windows.Controls.TextBlock)dialog.FindName("StatusText");
                    var details = (System.Windows.Controls.TextBox)dialog.FindName("DetailsText");
                    if (people.Items.Count != 3) throw new InvalidOperationException("全谱预览未列出所有人物。");
                    people.SelectedItem = people.Items.Cast<LineagePersonProjection>().Single(r => r.Person.Id == pathSubject);
                    var row = (LineagePersonProjection)people.SelectedItem;
                    if (!row.NeedsChoice || choices.Items.Count != 2 || !details.Text.Contains("失考"))
                        throw new InvalidOperationException("全谱预览未显示待选关系及失考。");
                    choices.SelectedItem = choices.Items.Cast<LineageParentOption>().Single(c => c.Parent.Id == pathParent);
                    ((System.Windows.Controls.Button)dialog.FindName("ChooseButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    row = (LineagePersonProjection)people.SelectedItem;
                    if (row.SelectedParent?.Parent.Id != pathParent || row.NeedsChoice)
                        throw new InvalidOperationException("全谱预览选择关系未生效。");
                    ((System.Windows.Controls.Button)dialog.FindName("ClearButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (!((LineagePersonProjection)people.SelectedItem).NeedsChoice)
                        throw new InvalidOperationException("清除选择后未恢复待选择状态。");
                    if (invalidate)
                    {
                        await genealogy.SelectAsync(priorGenealogy);
                        if (people.Items.Count != 0 || choices.Items.Count != 0 || !status.Text.Contains("失效"))
                            throw new InvalidOperationException("切谱后全谱预览未清空。");
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { dialog.Close(); }
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("lineagePreviewButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled || main.OwnedWindows.OfType<LineagePreviewDialog>().Any(w => w.IsVisible)) && until.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
            if (timer.IsEnabled || !panel.IsEnabled) { timer.Stop(); throw new TimeoutException("全谱预览未完成。"); }
            if (failure is not null) throw failure;
        }

        async Task PreviewPathAsync(bool invalidate)
        {
            if (genealogy.SelectedGenealogy?.Id != pathGenealogy.Id) await genealogy.SelectAsync(pathGenealogy);
            genealogy.SelectedPerson = genealogy.People.Single(p => p.Id == pathSubject);
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += async (_, _) =>
            {
                var dialog = main.OwnedWindows.OfType<AncestryPreviewDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                try
                {
                    var paths = (System.Windows.Controls.ListBox)dialog.FindName("PathList");
                    var choices = (System.Windows.Controls.ListBox)dialog.FindName("ChoiceList");
                    var status = (System.Windows.Controls.TextBlock)dialog.FindName("StatusText");
                    if (paths.Items.Count != 1 || !paths.Items[0].ToString()!.Contains("失考") || choices.Items.Count != 2)
                        throw new InvalidOperationException("路径预览未展示失考及多条候选。");
                    choices.SelectedItem = choices.Items.Cast<AncestryChoice>().Single(c => c.ParentId == pathParent);
                    ((System.Windows.Controls.Button)dialog.FindName("ContinueButton"))
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (paths.Items.Count != 2 || !paths.Items[1].ToString()!.Contains(pathParent.ToString()) || !status.Text.Contains("未记录"))
                        throw new InvalidOperationException("路径预览选择后结果不正确。");
                    if (invalidate)
                    {
                        await genealogy.SelectAsync(priorGenealogy);
                        if (paths.Items.Count != 0 || choices.Items.Count != 0 || !status.Text.Contains("失效"))
                            throw new InvalidOperationException("切谱后上系预览未失效。");
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { dialog.Close(); }
            };
            timer.Start();
            ((System.Windows.Controls.Button)main.FindName("ancestryPreviewButton"))
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var until = Stopwatch.StartNew();
            while ((timer.IsEnabled || !panel.IsEnabled || main.OwnedWindows.OfType<AncestryPreviewDialog>().Any(w => w.IsVisible)) && until.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
            if (timer.IsEnabled || !panel.IsEnabled) { timer.Stop(); throw new TimeoutException("上系预览未完成。"); }
            if (failure is not null) throw failure;
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

    private static System.Windows.Controls.TextBlock? FindLineageLabel(DependencyObject root, LineageTreeNode node)
    {
        if (root is System.Windows.Controls.TextBlock text && ReferenceEquals(text.DataContext, node) && text.Text == node.Label) return text;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindLineageLabel(System.Windows.Media.VisualTreeHelper.GetChild(root, i), node);
            if (result is not null) return result;
        }
        return null;
    }
}
