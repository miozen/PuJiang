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
        var nestedBranch = new ZupuPaiBan.Models.FamilyBranch { GenealogyId = genealogy.SelectedGenealogy!.Id, ParentBranchId = firstBranch, Name = "次支" };
        await using (var context = coordinator.CreateContext())
        {
            context.FamilyBranches.Add(nestedBranch);
            await context.SaveChangesAsync();
        }
        await genealogy.SelectAsync(genealogy.SelectedGenealogy);
        await EditBranchAsync(null, firstBranch);
        await EditBranchAsync(firstBranch, nestedBranch.Id);
        await EditBranchAsync(nestedBranch.Id, secondBranch);
        await EditBranchAsync(secondBranch, null);

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
