using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                scroll.ScrollToVerticalOffset(i * (LineageGraphLayoutService.NodeHeight + LineageGraphLayoutService.VerticalGap));
                dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
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
        for (var run = 1; run <= 20; run++)
        {
            watch.Restart();
            var calculated = LineageGraphLayoutService.Calculate(tree, expanded);
            watch.Stop();
            if (calculated.Nodes.Count != 1000) throw new InvalidOperationException("布局节点数量错误。");
            Record("layout-only", new { run, ms = watch.Elapsed.TotalMilliseconds });
        }
        for (var run = 1; run <= 20; run++)
        {
            watch.Restart(); render.Invoke(dialog, null); dialog.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); watch.Stop();
            var nodes = canvas.Children.OfType<Grid>().Count(); var lines = canvas.Children.OfType<Line>().Count();
            var layout = (LineageGraphLayout)typeof(LineageTreeDialog).GetField("_graphLayout", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
            if (layout.Nodes.Count != 1000 || layout.Edges.Count != (shape == "balanced" ? 959 : 999) || nodes >= 1000)
                throw new InvalidOperationException($"布局或视口控件数量错误：{layout.Nodes.Count}/{layout.Edges.Count}，视口 {nodes}/{lines}。");
            Record("full-render", new { run, ms = watch.Elapsed.TotalMilliseconds, counts = Counts() });
        }
        Phase("locate-last");
        var search = (TextBox)dialog.FindName("SearchBox");
        var results = (ComboBox)dialog.FindName("SearchResults");
        var targetId = LineagePerformanceData.PersonId(shape == "balanced" ? 500 : 999);
        for (var run = 1; run <= 20; run++)
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
        for (var run = 1; run <= 20; run++)
        {
            scroll.ScrollToHorizontalOffset(0); scroll.ScrollToVerticalOffset(0);
            dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            watch.Restart(); scroll.ScrollToHorizontalOffset(scroll.ScrollableWidth); scroll.ScrollToVerticalOffset(scroll.ScrollableHeight);
            dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); watch.Stop();
            Record("scroll-end", new { run, ms = watch.Elapsed.TotalMilliseconds, horizontal = scroll.HorizontalOffset,
                vertical = scroll.VerticalOffset, scrollableWidth = scroll.ScrollableWidth, scrollableHeight = scroll.ScrollableHeight, counts = Counts() });
        }
        Phase("interaction-matrix");
        // Locate a person with a selected parent, so both the selected node and a path edge are testable.
        search.Text = targetId.ToString();
        results.SelectedItem = results.Items.OfType<LineageTreeNode>().Single(n => n.PersonId == targetId);
        ((Button)dialog.FindName("LocateButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var targetNode = tree.GetPerson(targetId)!;
        var relationship = targetNode.Projection.SelectedParent!.Relationship.Id;
        if (!canvas.Children.OfType<Line>().Any(l => l.Tag is Guid id && id == relationship && l.Stroke == Brushes.DarkOrange))
            throw new InvalidOperationException("定位后的路径边未高亮。");
        var zoom = (ScaleTransform)dialog.FindName("GraphScale");
        ((Button)dialog.FindName("GraphZoomIn")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (Math.Abs(zoom.ScaleX - 1.1) > 0.001) throw new InvalidOperationException("缩放未生效。");
        // Independently compare every node in the buffered rectangle to actual controls, including boundary intersections.
        var layoutNow = (LineageGraphLayout)typeof(LineageTreeDialog).GetField("_graphLayout", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        var w = scroll.ViewportWidth / zoom.ScaleX; var h = scroll.ViewportHeight / zoom.ScaleY;
        var bounds = new Rect(scroll.HorizontalOffset / zoom.ScaleX - w, scroll.VerticalOffset / zoom.ScaleY - h, 3 * w, 3 * h);
        var expectedIds = layoutNow.Nodes.Where(n => bounds.IntersectsWith(new Rect(n.X, n.Y, LineageGraphLayoutService.NodeWidth, LineageGraphLayoutService.NodeHeight)))
            .Select(n => n.Person.PersonId).ToHashSet();
        var actualIds = canvas.Children.OfType<Grid>().Select(g => (Guid)g.Children.OfType<Button>().First().Tag).ToHashSet();
        if (!expectedIds.SetEquals(actualIds)) throw new InvalidOperationException("缩放后的缓冲视口节点不完整或包含视口外节点。");
        // Scroll to an interior position and check a line intersecting the visible boundary.
        var positions = layoutNow.Nodes.ToDictionary(n => n.Person.PersonId);
        var useVertical = scroll.ScrollableHeight > 100;
        var crossing = layoutNow.Edges.Select(edge => {
            var parent = positions[edge.ParentId]; var child = positions[edge.ChildId];
            return new { Edge = edge, X = (parent.X + child.X + LineageGraphLayoutService.NodeWidth) / 2,
                Y = (parent.Y + LineageGraphLayoutService.NodeHeight + child.Y) / 2 };
        }).Where(e => useVertical ? e.Y * zoom.ScaleY > 1 && e.Y * zoom.ScaleY < scroll.ScrollableHeight - 1
            : e.X * zoom.ScaleX > 1 && e.X * zoom.ScaleX < scroll.ScrollableWidth - 1)
            .OrderBy(e => Math.Abs((useVertical ? e.Y * zoom.ScaleY / scroll.ScrollableHeight : e.X * zoom.ScaleX / scroll.ScrollableWidth) - 0.5)).First();
        scroll.ScrollToHorizontalOffset(useVertical ? Math.Max(0, crossing.X * zoom.ScaleX - scroll.ViewportWidth / 2) : crossing.X * zoom.ScaleX);
        scroll.ScrollToVerticalOffset(useVertical ? crossing.Y * zoom.ScaleY : Math.Max(0, crossing.Y * zoom.ScaleY - scroll.ViewportHeight / 2));
        dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var visible = new Rect(scroll.HorizontalOffset / zoom.ScaleX, scroll.VerticalOffset / zoom.ScaleY, w, h);
        if (!canvas.Children.OfType<Line>().Any(l => l.Tag is Guid edgeId && edgeId == crossing.Edge.RelationshipId &&
            LineageGraphViewport.IntersectsLine(visible, new Point(l.X1, l.Y1), new Point(l.X2, l.Y2)) &&
            (!visible.Contains(new Point(l.X1, l.Y1)) || !visible.Contains(new Point(l.X2, l.Y2)))))
            throw new InvalidOperationException("未保留跨越可见视口的路径连线。");
        // Preserve a reproducible rendering for separate visual inspection, without reading user data.
        var visualDirectory = System.IO.Path.Combine(Environment.CurrentDirectory, "TestResults", "d024-visual");
        Directory.CreateDirectory(visualDirectory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(dialog);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(System.IO.Path.Combine(visualDirectory, shape + ".png"))) encoder.Save(stream);
        ((Button)dialog.FindName("GraphZoomReset")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        search.Text = LineagePerformanceData.PersonId(0).ToString();
        results.SelectedItem = results.Items.OfType<LineageTreeNode>().Single(n => n.PersonId == LineagePerformanceData.PersonId(0));
        ((Button)dialog.FindName("LocateButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var rootToggle = canvas.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Button>())
            .Single(b => b.Tag is Guid id && id == LineagePerformanceData.PersonId(0) && ((string)b.Content).StartsWith("折叠"));
        rootToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (expanded.Contains(LineagePerformanceData.PersonId(0))) throw new InvalidOperationException("折叠未更新状态。");
        var collapsed = (LineageGraphLayout)typeof(LineageTreeDialog).GetField("_graphLayout", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        if (collapsed.Nodes.Any(n => n.Person.Parent?.PersonId == LineagePerformanceData.PersonId(0)))
            throw new InvalidOperationException("折叠后直属下系仍在布局中。");
        Record("interaction-matrix", new { zoom = true, viewportNodes = actualIds.Count, selectedEdge = true, crossingEdge = true, collapse = true });
        await using var afterContext = database.CreateContext();
        var after = await new GenealogyDataService(afterContext).GetGraphSnapshotAsync(LineagePerformanceData.GenealogyId);
        if (before.UnlinkGeneration != after.UnlinkGeneration || before.People.Count != after.People.Count || before.Relationships.Count != after.Relationships.Count)
            throw new InvalidOperationException("测量修改了原始数据或全库代数。");
        Record("data-unchanged", new { before.UnlinkGeneration, people = before.People.Count, relationships = before.Relationships.Count });
        // After the read-only check, deliberately change a saved choice in the isolated database
        // and verify that its configuration requires review instead of silently applying it.
        await using (var chartContext = database.CreateContext())
        {
            var charts = new LineageChartService(chartContext);
            var chart = await charts.SaveAsync(LineagePerformanceData.GenealogyId, null, "隔离复核", null, after,
                new Dictionary<Guid, Guid> { [targetId] = relationship });
            var changed = await chartContext.PersonRelationships.SingleAsync(r => r.Id == relationship);
            changed.Kind = ZupuPaiBan.Models.PersonRelationshipKind.HeirParent;
            await chartContext.SaveChangesAsync();
            var changedSnapshot = await new GenealogyDataService(chartContext).GetGraphSnapshotAsync(LineagePerformanceData.GenealogyId);
            var review = await charts.ReviewAsync(LineagePerformanceData.GenealogyId, chart.Id, changedSnapshot);
            if (!review.GenerationChanged || review.Choices.Single().Problem is null)
                throw new InvalidOperationException("谱形配置变更后未标记失效选线。");
            Record("configuration-stale", new { generationChanged = review.GenerationChanged, problem = review.Choices.Single().Problem });
        }
        await vm.SelectAsync(vm.Genealogies.Single(g => g.Id == Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));
        if (canvas.Children.Count != 0 || !((TextBlock)dialog.FindName("StatusText")).Text.Contains("失效"))
            throw new InvalidOperationException("切换族谱后图形未失效。");
        Record("switch-genealogy", new { invalidated = true });
        dialog.Close(); main.Close();
    }
}
