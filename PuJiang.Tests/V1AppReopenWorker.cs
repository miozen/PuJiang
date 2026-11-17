using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using PuJiang.V1.Application;
using PuJiang.V1.Persistence;
using ZupuPaiBan;
using ZupuPaiBan.Data;
using ZupuPaiBan.Infrastructure;
using ZupuPaiBan.Services;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

// Each invocation runs the actual WPF App startup and normal shutdown in its own process.
internal static class V1AppReopenWorker
{
    public static async Task<int> RunAsync(string root, string phase)
    {
        var fixture = new TestDatabase(root); // refuses non-test paths
        Environment.SetEnvironmentVariable(AppDataPaths.TestRootEnvironmentVariable, root);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={fixture.DatabasePath};Pooling=False").Options;
                var main = new MainWindow(new TargetContextCoordinator(options), fixture.BackupDirectory, coordinateExit: true)
                    { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
                Exception? failure = null; var verified = false;
                Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        typeof(MainWindow).GetMethod("WorkbenchProfile_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object?[] { main, new RoutedEventArgs() });
                        var workbench = main.OwnedWindows.OfType<WorkbenchProfileWindow>().Single();
                        await workbench.InitializeForTest(); await workbench.OpenV1SampleForTest();
                        var window = workbench.OwnedWindows.OfType<V1FirstRootWindow>().Single(); await window.ReadyForTest;
                        var sample = await ControlledFictionalSamples.OpenLatestOrCreateAsync();
                        var lineages = new LineageApplication(new IsolatedLineageStore(sample.Database));
                        var roots = new FirstRootApplication(new IsolatedFirstRootStore(sample.Database));
                        if (phase == "create")
                        {
                            window.BeginForTest(); window.SetGenerationForTest("1"); window.SetInputForTest("甲"); await window.ConfirmForTest();
                            if (window.NodeCountForTest != 1) throw new InvalidOperationException("第一谱首节点未显示。");
                            var second = await lineages.CreateAsync(new(Guid.NewGuid(), sample.GenealogyId, "虚构第二谱", "家谱", "虚构地区", "虚构主题", "", ""));
                            await roots.ExecuteAsync(new(Guid.NewGuid(), sample.GenealogyId, second, RootNameMode.Named, "冯乙", 2));
                        }
                        var list = await lineages.ListAsync(sample.GenealogyId);
                        if (list.Count != 2) throw new InvalidOperationException("没有读取到两条谱系。");
                        var secondId = list.Single(x => x.Name == "虚构第二谱").Id;
                        await window.SwitchLineageForTest(secondId);
                        if (!window.HomeVisibleForTest || !window.HomeTextForTest.Contains("人物总数：1")) throw new InvalidOperationException("第二谱首页异常。");
                        var secondRoots = await roots.ReadAsync(sample.GenealogyId, secondId);
                        if (secondRoots.Nodes.Single().Name != "冯乙") throw new InvalidOperationException("第二谱首节点异常。");
                        await window.EnterCanvasForTest(secondRoots.Nodes.Single());
                        if (window.FocusForTest.Person != secondRoots.Nodes.Single().PersonId) throw new InvalidOperationException("第二谱焦点异常。");
                        await window.SwitchLineageForTest(sample.LineageId);
                        if (!window.HomeTextForTest.Contains("人物总数：1")) throw new InvalidOperationException("第一谱首页异常。");
                        var firstRoots = await roots.ReadAsync(sample.GenealogyId, sample.LineageId);
                        if (firstRoots.Nodes.Single().Name != "冯甲") throw new InvalidOperationException("第一谱首节点异常。");
                        Console.WriteLine($"V1_REOPEN_OK {phase} {sample.GenealogyId} {sample.LineageId} {secondId}");
                        verified = true; main.Close();
                    }
                    catch (Exception e) { failure = e; app.Shutdown(-1); }
                });
                app.Run(main);
                if (failure is not null) throw failure;
                if (!verified) throw new InvalidOperationException("未完成应用退出前验证。");
                completion.TrySetResult(0);
            }
            catch (Exception e) { Console.Error.WriteLine(e); completion.TrySetResult(1); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return await completion.Task;
    }
}
