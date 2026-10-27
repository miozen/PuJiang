using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

// Runs a real App.OnStartup/OnExit in a dedicated temporary-directory process.
internal static class WpfAppExitWorker
{
    public static async Task<int> RunAsync(string root, string scenario)
    {
        var database = new TestDatabase(root);
        Environment.SetEnvironmentVariable(AppDataPaths.TestRootEnvironmentVariable, root);
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var options = new DbContextOptionsBuilder<ZupuDbContext>()
                    .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
                var coordinator = new TargetContextCoordinator(options);
                var main = new MainWindow(coordinator, database.BackupDirectory,
                    coordinateExit: scenario != "cancel", exitTimeout: TimeSpan.FromMilliseconds(200))
                { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
                Exception? failure = null;
                var verified = false;
                Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        var timer = Stopwatch.StartNew();
                        while (!main.IsEnabled && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
                        if (!main.IsEnabled) throw new TimeoutException("主窗口初始化超时。");
                        await VerifyScenarioAsync(app, main, coordinator, root, scenario);
                        verified = true;
                    }
                    catch (Exception ex) { failure = ex; app.Shutdown(-1); }
                });
                app.Run(main);
                if (failure != null) throw failure;
                if (!verified) throw new InvalidOperationException("应用退出前测试未完成。");
                using var reacquired = DataDirectoryLease.Acquire(root);
                result.TrySetResult(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); result.TrySetResult(1); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await result.Task;
    }

    private static async Task VerifyScenarioAsync(App app, MainWindow main, TargetContextCoordinator coordinator,
        string root, string scenario)
    {
        if (scenario == "success")
        {
            var child = new Window { Owner = main, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            var childClosed = false;
            child.Closed += (_, _) => childClosed = true;
            main.Closed += (_, _) =>
            {
                if (!childClosed) throw new InvalidOperationException("主窗口关闭时子窗口仍未关闭。");
            };
            child.Show();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = Task.Run(async () =>
            {
                using var operation = coordinator.EnterOperation();
                await using var context = coordinator.CreateContext();
                await context.Database.OpenConnectionAsync();
                entered.TrySetResult();
                await release.Task;
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var finalHold = coordinator.CreateContext();
            main.Close();
            if (!main.IsVisible || main.IsEnabled) throw new InvalidOperationException("在途操作期间主窗口未保持等待。");
            AssertLeaseHeld(root);
            AssertNewOperationRejected(coordinator);
            release.SetResult();
            await work;
            // Let the caller record the assertions before allowing App.OnExit to stop the dispatcher.
            _ = main.Dispatcher.BeginInvoke(() => finalHold.Dispose(), DispatcherPriority.Background);
            return;
        }

        if (scenario is "child-refusal" or "child-error")
        {
            var child = new Window { Owner = main, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            var refuse = true;
            System.ComponentModel.CancelEventHandler onClosing = (_, e) =>
            {
                if (refuse && scenario == "child-error") throw new InvalidOperationException("模拟子窗口关闭异常。");
                e.Cancel = refuse;
            };
            child.Closing += onClosing;
            child.Show();
            main.Close();
            await WaitForFailureAsync(main);
            if (scenario == "child-refusal" && !child.IsVisible)
                throw new InvalidOperationException("拒绝关闭的子窗口意外关闭。");
            AssertLeaseHeld(root);
            AssertNewOperationRejected(coordinator);
            refuse = false;
            child.Closing -= onClosing;
            if (child.IsVisible) child.Close();
            _ = app.Dispatcher.BeginInvoke(() => app.Shutdown(), DispatcherPriority.Background); // Test cleanup only.
            return;
        }

        await using var held = coordinator.CreateContext();
        if (scenario == "cancel")
        {
            using var cancellation = new CancellationTokenSource();
            var stopping = main.PrepareForExitAsync(cancellation.Token);
            cancellation.Cancel();
            if (await stopping) throw new InvalidOperationException("取消后不应报告成功。");
        }
        else if (scenario == "timeout")
        {
            main.Close();
            await WaitForFailureAsync(main);
        }
        else throw new InvalidOperationException("未知测试场景。");
        if (!main.IsVisible || main.IsEnabled) throw new InvalidOperationException("取消或超时后窗口未保持失败关闭。");
        AssertLeaseHeld(root);
        AssertNewOperationRejected(coordinator);
        await held.DisposeAsync();
        _ = app.Dispatcher.BeginInvoke(() => app.Shutdown(), DispatcherPriority.Background); // Test cleanup only.
    }

    private static async Task WaitForFailureAsync(MainWindow main)
    {
        var timer = Stopwatch.StartNew();
        while (!((MainViewModel)main.DataContext).StatusText.Contains("未完成") && timer.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        if (!((MainViewModel)main.DataContext).StatusText.Contains("未完成"))
            throw new TimeoutException("未观察到失败关闭状态。");
    }

    private static void AssertLeaseHeld(string root)
    {
        try { using var unexpected = DataDirectoryLease.Acquire(root); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("使用")) { return; }
        throw new InvalidOperationException("数据目录锁过早释放。");
    }

    private static void AssertNewOperationRejected(TargetContextCoordinator coordinator)
    {
        try { using var unexpected = coordinator.EnterOperation(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("停用")) { return; }
        throw new InvalidOperationException("排空后新操作未被拒绝。");
    }
}
