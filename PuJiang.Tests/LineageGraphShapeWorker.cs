using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

internal static class LineageGraphShapeWorker
{
    public static async Task<int> RunAsync(string directory, string shape)
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
                try { await MeasureAsync(database, shape); result.TrySetResult(0); }
                catch (Exception ex) { Console.Error.WriteLine(ex); result.TrySetResult(1); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return await result.Task;
    }

    private static async Task MeasureAsync(TestDatabase database, string shape)
    {
        void Record(string stage, object value) { Console.WriteLine("RESULT:" + JsonSerializer.Serialize(new { shape, stage, value })); Console.Out.Flush(); }
        void Phase(string stage) { Console.WriteLine("PHASE:" + stage); Console.Out.Flush(); }
        Directory.CreateDirectory(database.BackupDirectory);
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
        var coordinator = new TargetContextCoordinator(options);
        var main = new MainWindow(coordinator, database.BackupDirectory) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        main.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var initialization = Stopwatch.StartNew();
        while (!main.IsEnabled && initialization.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
        if (!main.IsEnabled) throw new TimeoutException("隔离主窗口初始化超时。");
        var vm = (GenealogySelectionViewModel)((GroupBox)main.FindName("genealogyPanel")).DataContext;
        await vm.LoadAsync(); await vm.SelectAsync(vm.Genealogies.Single(g => g.Id == LineagePerformanceData.GenealogyId));
        await using var beforeContext = database.CreateContext();
        var before = await new GenealogyDataService(beforeContext).GetGraphSnapshotAsync(LineagePerformanceData.GenealogyId);
        var process = Process.GetCurrentProcess(); process.Refresh();
        long initialMemory = process.PrivateMemorySize64;
        Phase("open");
        var watch = Stopwatch.StartNew();
        var preview = await vm.GetLineagePreviewAsync();
        var dialog = new LineageTreeDialog(vm, preview) { Owner = main, ShowActivated = false, ShowInTaskbar = false };
        dialog.Show(); ((TabControl)dialog.FindName("LineageViews")).SelectedIndex = 1; dialog.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); watch.Stop();
        var canvas = (Canvas)dialog.FindName("GraphCanvas");
        var scroll = (ScrollViewer)dialog.FindName("GraphScroll");
        var expanded = (HashSet<Guid>)typeof(LineageTreeDialog).GetField("_graphExpanded", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        var render = typeof(LineageTreeDialog).GetMethod("RenderGraph", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tree = (LineageTreeState)typeof(LineageTreeDialog).GetField("_tree", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        object Counts()
        {
            process.Refresh();
            var grids = canvas.Children.OfType<Grid>().ToArray();
            return new { nodes = grids.Length, lines = canvas.Children.OfType<Line>().Count(),
                buttons = grids.Sum(g => g.Children.OfType<Button>().Count()), canvasWidth = canvas.Width, canvasHeight = canvas.Height,
                viewportWidth = scroll.ViewportWidth, viewportHeight = scroll.ViewportHeight, privateBytes = process.PrivateMemorySize64,
                privateDeltaBytes = process.PrivateMemorySize64 - initialMemory };
        }
        Record("open", new { ms = watch.Elapsed.TotalMilliseconds, counts = Counts() });
        if (shape == "deep")
        {
            Phase("click-expand-10");
            for (var i = 0; i < 10; i++)
            {
                var personId = LineagePerformanceData.PersonId(i);
                var button = canvas.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Button>())
                    .Single(b => b.Tag is Guid id && id == personId && ((string)b.Content).StartsWith("展开"));
                watch.Restart(); button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); watch.Stop();
                Record("click-expand", new { step = i + 1, ms = watch.Elapsed.TotalMilliseconds, counts = Counts() });
            }
        }
        else
        {
            Phase("click-expand-root");
            var rootId = LineagePerformanceData.PersonId(0);
            var button = canvas.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Button>())
                .Single(b => b.Tag is Guid id && id == rootId && ((string)b.Content).StartsWith("展开"));
            watch.Restart(); button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); watch.Stop();
            Record("click-expand-root", new { ms = watch.Elapsed.TotalMilliseconds, counts = Counts() });
        }
        Phase("full-render");
        expanded.UnionWith(tree.People.Select(n => n.PersonId));
        for (var run = 1; run <= 3; run++)
        {
            watch.Restart(); render.Invoke(dialog, null); dialog.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); watch.Stop();
            var nodes = canvas.Children.OfType<Grid>().Count(); var lines = canvas.Children.OfType<Line>().Count();
            if (nodes != 1000 || lines != (shape == "balanced" ? 959 : 999))
                throw new InvalidOperationException($"全展开节点或边数量错误：{nodes}/{lines}。");
            Record("full-render", new { run, ms = watch.Elapsed.TotalMilliseconds, counts = Counts() });
        }
        Phase("locate-last");
        var search = (TextBox)dialog.FindName("SearchBox");
        var results = (ComboBox)dialog.FindName("SearchResults");
        var targetId = LineagePerformanceData.PersonId(shape == "balanced" ? 500 : 999);
        for (var run = 1; run <= 3; run++)
        {
            search.Text = string.Empty; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            watch.Restart(); search.Text = targetId.ToString();
            results.SelectedItem = results.Items.OfType<LineageTreeNode>().Single(n => n.PersonId == targetId);
            ((Button)dialog.FindName("LocateButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); watch.Stop();
            var target = canvas.Children.OfType<Grid>().Single(g => g.Children.OfType<Button>().Any(b => b.Tag is Guid id && id == targetId));
            var point = target.TranslatePoint(new Point(), scroll);
            if (point.X < -1 || point.Y < -1 || point.X >= scroll.ViewportWidth || point.Y >= scroll.ViewportHeight)
                throw new InvalidOperationException($"定位目标未进入视口：{point}。");
            Record("locate-last", new { run, ms = watch.Elapsed.TotalMilliseconds, x = point.X, y = point.Y, counts = Counts() });
        }
        Phase("scroll");
        for (var run = 1; run <= 3; run++)
        {
            scroll.ScrollToHorizontalOffset(0); scroll.ScrollToVerticalOffset(0);
            dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            watch.Restart(); scroll.ScrollToHorizontalOffset(scroll.ScrollableWidth); scroll.ScrollToVerticalOffset(scroll.ScrollableHeight);
            dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); watch.Stop();
            Record("scroll-end", new { run, ms = watch.Elapsed.TotalMilliseconds, horizontal = scroll.HorizontalOffset,
                vertical = scroll.VerticalOffset, scrollableWidth = scroll.ScrollableWidth, scrollableHeight = scroll.ScrollableHeight, counts = Counts() });
        }
        await using var afterContext = database.CreateContext();
        var after = await new GenealogyDataService(afterContext).GetGraphSnapshotAsync(LineagePerformanceData.GenealogyId);
        if (before.UnlinkGeneration != after.UnlinkGeneration || before.People.Count != after.People.Count || before.Relationships.Count != after.Relationships.Count)
            throw new InvalidOperationException("测量修改了原始数据或全库代数。");
        Record("data-unchanged", new { before.UnlinkGeneration, people = before.People.Count, relationships = before.Relationships.Count });
        dialog.Close(); main.Close();
    }
}
