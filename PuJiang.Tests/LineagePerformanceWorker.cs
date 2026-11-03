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
    public static async Task<int> RunAsync(string directory, bool full, bool graph = false)
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
                    var samples = await MeasureAsync(database, full, graph);
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

    private static async Task<LineagePerformanceResult> MeasureAsync(TestDatabase database, bool full, bool graph)
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
        if (graph)
        {
            var result = await MeasureGraphAsync(vm, main, full);
            await using var verify = database.CreateContext();
            var afterGraph = await new GenealogyDataService(verify).GetGraphSnapshotAsync(LineagePerformanceData.GenealogyId);
            if (before.UnlinkGeneration != afterGraph.UnlinkGeneration || before.People.Count != afterGraph.People.Count ||
                before.Relationships.Count != afterGraph.Relationships.Count) throw new InvalidOperationException("图形测量改变了原始数据。");
            main.Close();
            return result;
        }

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

    private static async Task<LineagePerformanceResult> MeasureGraphAsync(GenealogySelectionViewModel vm, MainWindow main, bool full)
    {
        async Task<(LineageTreeDialog Dialog, double Elapsed)> OpenAsync()
        {
            var watch = Stopwatch.StartNew();
            var preview = await vm.GetLineagePreviewAsync();
            var dialog = new LineageTreeDialog(vm, preview) { Owner = main, ShowActivated = false, ShowInTaskbar = false };
            dialog.Show();
            ((TabControl)dialog.FindName("LineageViews")).SelectedIndex = 1;
            dialog.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            watch.Stop();
            var layout = (LineageGraphLayout)typeof(LineageTreeDialog).GetField("_graphLayout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(dialog)!;
            var count = ((Canvas)dialog.FindName("GraphCanvas")).Children.OfType<Grid>().Count();
            if (layout.Nodes.Count != 41 || count == 0 || count >= 41)
                throw new InvalidOperationException("图形根节点布局或视口呈现错误。");
            return (dialog, watch.Elapsed.TotalMilliseconds);
        }
        var (first, firstMs) = await OpenAsync();
        first.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var warm = new List<double>(); var names = new List<double>(); var ids = new List<double>(); var expand = new List<double>();
        if (full)
        {
            LineageTreeDialog? dialog = null;
            for (var i = 0; i < 20; i++)
            {
                var opened = await OpenAsync(); warm.Add(opened.Elapsed); dialog = opened.Dialog;
                if (i < 19) { dialog.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            }
            var canvas = (Canvas)dialog!.FindName("GraphCanvas");
            var search = (TextBox)dialog.FindName("SearchBox");
            var results = (ComboBox)dialog.FindName("SearchResults");
            var locate = (Button)dialog.FindName("LocateButton");
            var target = LineagePerformanceData.PersonId(500);
            var expanded = (HashSet<Guid>)typeof(LineageTreeDialog).GetField("_graphExpanded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(dialog)!;
            var render = typeof(LineageTreeDialog).GetMethod("RenderGraph", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            async Task ResetAsync()
            {
                expanded.Clear(); render.Invoke(dialog, null); search.Text = string.Empty;
                var scroll = (ScrollViewer)dialog.FindName("GraphScroll");
                scroll.ScrollToHorizontalOffset(0); scroll.ScrollToVerticalOffset(0);
                dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            async Task<double> SearchAsync(string query)
            {
                await ResetAsync();
                var watch = Stopwatch.StartNew();
                search.Text = query;
                results.SelectedItem = results.Items.OfType<LineageTreeNode>().Single(n => n.PersonId == target);
                locate.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); watch.Stop();
                if (!canvas.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Button>()).Any(b => b.Tag is Guid id && id == target))
                    throw new InvalidOperationException("图形搜索定位失败。");
                var targetPanel = canvas.Children.OfType<Grid>().Single(g => g.Children.OfType<Button>().Any(b => b.Tag is Guid id && id == target));
                var viewport = (ScrollViewer)dialog.FindName("GraphScroll");
                var point = targetPanel.TranslatePoint(new Point(0, 0), viewport);
                if (point.X < -1 || point.Y < -1 || point.X >= viewport.ViewportWidth || point.Y >= viewport.ViewportHeight)
                    throw new InvalidOperationException($"图形定位未进入视口：{point}。");
                return watch.Elapsed.TotalMilliseconds;
            }
            for (var i = 0; i < 20; i++) names.Add(await SearchAsync("虚构成员0500"));
            for (var i = 0; i < 20; i++) ids.Add(await SearchAsync(target.ToString()));
            for (var i = 0; i < 20; i++)
            {
                await ResetAsync();
                var toggle = canvas.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Button>())
                    .Single(b => b.Tag is Guid id && id == LineagePerformanceData.PersonId(0) && ((string)b.Content).StartsWith("展开"));
                var watch = Stopwatch.StartNew(); toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); watch.Stop();
                if (canvas.Children.OfType<System.Windows.Shapes.Line>().Count() != 3) throw new InvalidOperationException("图形根节点展开失败。");
                expand.Add(watch.Elapsed.TotalMilliseconds);
            }
            dialog.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        return new LineagePerformanceResult(firstMs, warm, names, ids, expand);
    }
}
