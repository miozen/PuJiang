using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

internal static class LineagePerformanceWorker
{
    public static async Task<int> RunAsync(string directory, bool full)
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
                try
                {
                    var samples = await MeasureAsync(database, full);
                    Console.WriteLine("RESULT:" + JsonSerializer.Serialize(samples));
                    result.TrySetResult(0);
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); result.TrySetResult(1); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await result.Task;
    }

    private static async Task<LineagePerformanceResult> MeasureAsync(TestDatabase database, bool full)
    {
        Directory.CreateDirectory(database.BackupDirectory);
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
        var coordinator = new TargetContextCoordinator(options);
        var main = new MainWindow(coordinator, database.BackupDirectory) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        main.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var initialization = Stopwatch.StartNew();
        while (!main.IsEnabled && initialization.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
        if (!main.IsEnabled) throw new TimeoutException("隔离主窗口未完成初始化。");
        var panel = (GroupBox)main.FindName("genealogyPanel");
        var vm = (GenealogySelectionViewModel)panel.DataContext;
        await vm.LoadAsync();
        await vm.SelectAsync(vm.Genealogies.Single(g => g.Id == LineagePerformanceData.GenealogyId));
        await using var beforeContext = database.CreateContext();
        var before = await new GenealogyDataService(beforeContext).GetGraphSnapshotAsync(LineagePerformanceData.GenealogyId);
        if (before.People.Count != 1000 || before.Relationships.Count != 1023) throw new InvalidOperationException("样本结构不正确。");

        async Task<(LineageTreeDialog Dialog, double Elapsed)> OpenAsync()
        {
            var watch = Stopwatch.StartNew();
            var preview = await vm.GetLineagePreviewAsync();
            var dialog = new LineageTreeDialog(vm, preview) { Owner = main, ShowActivated = false, ShowInTaskbar = false };
            dialog.Show();
            dialog.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            watch.Stop();
            var tree = (TreeView)dialog.FindName("LineageTree");
            if (tree.Items.Count != 41 || !dialog.IsVisible) throw new InvalidOperationException($"树形窗口或样本根节点错误：{tree.Items.Count}。");
            return (dialog, watch.Elapsed.TotalMilliseconds);
        }

        var (first, firstMs) = await OpenAsync();
        first.Close();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var warm = new List<double>();
        var names = new List<double>();
        var ids = new List<double>();
        var expand = new List<double>();
        if (full)
        {
            LineageTreeDialog? current = null;
            for (var i = 0; i < 20; i++)
            {
                var opened = await OpenAsync();
                warm.Add(opened.Elapsed);
                current = opened.Dialog;
                if (i != 19) { current.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            }
            var dialog = current!;
            var tree = (TreeView)dialog.FindName("LineageTree");
            var search = (TextBox)dialog.FindName("SearchBox");
            var results = (ComboBox)dialog.FindName("SearchResults");
            var locate = (Button)dialog.FindName("LocateButton");
            var target = LineagePerformanceData.PersonId(500);
            var root = tree.Items.OfType<LineageTreeNode>().Single(n => n.PersonId == LineagePerformanceData.PersonId(0));

            async Task<double> SearchAsync(string query)
            {
                root.IsExpanded = false;
                dialog.UpdateLayout();
                var rootContainer = tree.ItemContainerGenerator.ContainerFromItem(root) as TreeViewItem
                    ?? throw new InvalidOperationException("根人物容器不存在。");
                rootContainer.IsSelected = true;
                search.Text = string.Empty;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                if (tree.SelectedItem is not LineageTreeNode initial || initial.PersonId != root.PersonId || root.IsExpanded)
                    throw new InvalidOperationException("搜索前未复位至折叠的根人物。");
                var watch = Stopwatch.StartNew();
                search.Text = query;
                results.SelectedItem = results.Items.OfType<LineageTreeNode>().Single(n => n.PersonId == target);
                locate.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                dialog.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                watch.Stop();
                if (tree.SelectedItem is not LineageTreeNode selected || selected.PersonId != target)
                    throw new InvalidOperationException("搜索定位未选中目标人物。");
                return watch.Elapsed.TotalMilliseconds;
            }
            for (var i = 0; i < 20; i++) names.Add(await SearchAsync("虚构成员0500"));
            for (var i = 0; i < 20; i++) ids.Add(await SearchAsync(target.ToString()));

            for (var i = 0; i < 20; i++)
            {
                root.IsExpanded = false;
                dialog.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var container = tree.ItemContainerGenerator.ContainerFromItem(root) as TreeViewItem
                    ?? throw new InvalidOperationException("根人物容器不存在。");
                var watch = Stopwatch.StartNew();
                container.IsExpanded = true;
                dialog.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                watch.Stop();
                if (!container.IsExpanded || container.Items.Count == 0) throw new InvalidOperationException("根人物展开失败。");
                expand.Add(watch.Elapsed.TotalMilliseconds);
            }
            dialog.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        await using var afterContext = database.CreateContext();
        var after = await new GenealogyDataService(afterContext).GetGraphSnapshotAsync(LineagePerformanceData.GenealogyId);
        if (before.UnlinkGeneration != after.UnlinkGeneration || before.People.Count != after.People.Count || before.Relationships.Count != after.Relationships.Count)
            throw new InvalidOperationException("只读测量改变了图数据或代数。");
        main.Close();
        return new LineagePerformanceResult(firstMs, warm, names, ids, expand);
    }
}
