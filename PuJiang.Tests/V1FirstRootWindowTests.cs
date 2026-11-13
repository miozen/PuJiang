using System.Windows.Threading;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using PuJiang.Domain;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public sealed class V1FirstRootWindowTests
{
    private sealed class FaultStore(IFirstRootStore inner) : IFirstRootStore
    {
        public bool FailBeforeSave, FailAfterSave, FailRead;
        public async Task<RootCreated> AddFirstAsync(AddFirstRoot command, CancellationToken cancellationToken)
        {
            if (FailBeforeSave) { FailBeforeSave = false; throw new InvalidOperationException("模拟写入失败"); }
            var result = await inner.AddFirstAsync(command, cancellationToken);
            if (FailAfterSave) { FailAfterSave = false; throw new InvalidOperationException("模拟提交结果丢失"); }
            return result;
        }
        public Task<RootProjection> ReadRootsAsync(Guid g, Guid l, CancellationToken cancellationToken)
        {
            if (FailRead) { FailRead = false; throw new InvalidOperationException("模拟读取失败"); }
            return inner.ReadRootsAsync(g, l, cancellationToken);
        }
    }
    private static async Task<(IsolatedTestDatabase Db, Guid Genealogy, Guid Lineage)> CreateAsync()
    {
        var db = IsolatedTestDatabase.CreateNew(); var g = Guid.NewGuid(); var l = Guid.NewGuid();
        await using var context = db.OpenContext(); await context.Database.EnsureCreatedAsync();
        context.Genealogies.Add(new Genealogy(g, "虚构项目", "冯")); context.Lineages.Add(new Lineage(l, g, "虚构谱系")); await context.SaveChangesAsync();
        return (db, g, l);
    }
    [Fact, Trait("Category", "UI")]
    public async Task Named_root_replays_uncertain_commit_preserves_focus_and_reloads()
    {
        var (db, g, l) = await CreateAsync(); var store = new FaultStore(new IsolatedFirstRootStore(db));
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(new FirstRootApplication(store), g, l, "冯"); window.Show();
            try
            {
                await window.ReadyForTest; window.BeginForTest(); window.SetInputForTest("初稿"); store.FailBeforeSave = true;
                await window.ConfirmForTest(); Assert.Equal("初稿", window.InputForTest); Assert.False(window.InputReadOnlyForTest); Assert.Contains("未保存", window.StatusForTest);
                window.SetInputForTest("朝阳"); store.FailAfterSave = true;
                await window.ConfirmForTest(); Assert.Equal("朝阳", window.InputForTest); Assert.Contains("未确认", window.StatusForTest); Assert.False(window.CanCloseForOwner());
                await window.RetryForTest(); Assert.Null(window.InputForTest); Assert.Equal(1, window.NodeCountForTest);
                var focus = window.FocusForTest; Assert.NotNull(focus.Person); Assert.NotNull(focus.Placement); Assert.NotEqual(focus.Person, focus.Placement);
                await window.ReloadForTest(); Assert.Equal(focus, window.FocusForTest); Assert.Equal(1, window.NodeCountForTest);
            }
            finally { window.Close(); }
        });
        await using var verify = db.OpenContext(); Assert.Equal("冯朝阳", (await verify.People.SingleAsync()).Name); Assert.Equal(1, await verify.Placements.CountAsync());
        await RunSta(async () => { var window = new V1FirstRootWindow(new FirstRootApplication(new IsolatedFirstRootStore(db)), g, l, "冯"); window.Show(); try { await window.ReadyForTest; Assert.Equal(1, window.NodeCountForTest); } finally { window.Close(); } });
    }
    [Fact, Trait("Category", "UI")]
    public async Task Empty_node_and_cancel_input_are_distinct_and_refresh_failure_reports_saved()
    {
        var (db, g, l) = await CreateAsync(); var store = new FaultStore(new IsolatedFirstRootStore(db));
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(new FirstRootApplication(store), g, l, "冯"); window.Show();
            try
            {
                await window.ReadyForTest; window.BeginForTest(); window.SetInputForTest("暂不提交"); window.CancelForTest();
                Assert.Empty((await new FirstRootApplication(store).ReadAsync(g,l)).Nodes);
                store.FailRead = true; await window.EmptyForTest();
                Assert.Contains("已保存，但画布读取失败", window.StatusForTest); var focus = window.FocusForTest;
                Assert.NotNull(focus.Person); Assert.NotNull(focus.Placement);
                await window.ReloadForTest(); Assert.Equal(focus, window.FocusForTest); Assert.Equal(1, window.NodeCountForTest);
            }
            finally { window.Close(); }
        });
        await using var verify = db.OpenContext(); Assert.Equal("", (await verify.People.SingleAsync()).Name);
    }
    [Fact, Trait("Category", "UI")]
    public async Task Routed_enter_commits_and_escape_cancels_input()
    {
        var (db, g, l) = await CreateAsync();
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(new FirstRootApplication(new IsolatedFirstRootStore(db)), g, l, "冯"); window.Show();
            try
            {
                await window.ReadyForTest; window.BeginForTest();
                var host = Assert.IsType<System.Windows.Controls.StackPanel>(window.InputHostForTest);
                Assert.Equal("冯", Assert.IsType<System.Windows.Controls.TextBlock>(host.Children[0]).Text);
                Assert.Same(window.InputControlForTest, host.Children[1]);
                window.SetInputForTest("取消");
                SendKey(window, Key.Escape); Assert.Null(window.InputForTest); Assert.Null(window.InputHostForTest);
                window.BeginForTest(); window.SetInputForTest("朝阳"); SendKey(window, Key.Enter);
                for (var i = 0; i < 200 && window.NodeCountForTest == 0; i++) await Task.Delay(10);
                Assert.Equal(1, window.NodeCountForTest); Assert.NotNull(window.FocusForTest.Placement); Assert.Null(window.InputHostForTest);
            }
            finally { if (window.InputForTest is not null && !window.InputReadOnlyForTest) window.CancelForTest(); window.Close(); }
        });
        await using var context = db.OpenContext(); Assert.Equal("冯朝阳", (await context.People.SingleAsync()).Name);
    }
    private static void SendKey(V1FirstRootWindow window, Key key)
    {
        var input = Assert.IsType<System.Windows.Controls.TextBox>(window.InputControlForTest);
        var source = PresentationSource.FromVisual(input) ?? throw new InvalidOperationException("输入控件未显示。");
        input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }
    [Fact, Trait("Category", "UI")]
    public async Task Canvas_fills_viewport_and_blank_click_commits()
    {
        var (db, g, l) = await CreateAsync();
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(new FirstRootApplication(new IsolatedFirstRootStore(db)), g, l, "冯"); window.Show();
            try
            {
                await window.ReadyForTest; window.Width = 1500; window.Height = 900; window.UpdateLayout();
                Assert.True(window.CanvasForTest.ActualWidth >= window.ScrollForTest.ViewportWidth - 2);
                Assert.True(window.CanvasForTest.ActualHeight >= window.ScrollForTest.ViewportHeight - 2);
                window.BeginForTest(); window.SetInputForTest("朝阳");
                var canvas = window.CanvasForTest;
                canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                for (var i = 0; i < 200 && window.NodeCountForTest == 0; i++) await Task.Delay(10);
                Assert.Equal(1, window.NodeCountForTest);
            }
            finally { if (window.InputForTest is not null && !window.InputReadOnlyForTest) window.CancelForTest(); window.Close(); }
        });
    }
    [Fact, Trait("Category", "UI")]
    public async Task Existing_workbench_opens_isolated_sample_and_refuses_exit_with_pending_input()
    {
        await using var legacyDb = new TestDatabase(); await legacyDb.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={legacyDb.DatabasePath};Pooling=False").Options;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(new TargetContextCoordinator(options)); window.Show();
            try
            {
                await window.InitializeForTest(); await window.OpenV1SampleForTest();
                var isolated = Assert.Single(window.OwnedWindows.OfType<V1FirstRootWindow>()); await isolated.ReadyForTest;
                isolated.BeginForTest(); isolated.SetInputForTest("草稿");
                Assert.False(await window.PrepareForOwnerExitAsync());
                isolated.CancelForTest(); await isolated.EmptyForTest(); var focus = isolated.FocusForTest;
                isolated.Close(); await window.OpenV1SampleForTest();
                var reopened = Assert.Single(window.OwnedWindows.OfType<V1FirstRootWindow>()); await reopened.ReadyForTest;
                Assert.Equal(1, reopened.NodeCountForTest); Assert.NotNull(focus.Placement); reopened.Close();
            }
            finally { if (await window.PrepareForOwnerExitAsync()) window.Close(); }
        });
        await using var legacy = legacyDb.CreateContext(); Assert.Empty(await legacy.People.ToListAsync());
    }
    private static async Task RunSta(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { var dispatcher = Dispatcher.CurrentDispatcher; dispatcher.BeginInvoke(async () => { try { await action(); done.SetResult(); } catch (Exception e) { done.SetException(e); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } }); Dispatcher.Run(); });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await done.Task.WaitAsync(TimeSpan.FromSeconds(30)); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }
}
