using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using ZupuPaiBan.Views;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class WorkbenchProfileWindowTests
{
    [Fact]
    [Trait("Category", "UI")]
    public async Task Window_switch_and_owner_exit_respect_draft_choices()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var a = new Genealogy { Name = "甲" }; var b = new Genealogy { Name = "乙" };
        var p = new Person { GenealogyId = a.Id, Name = "甲人" }; var q = new Person { GenealogyId = a.Id, Name = "甲二" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.AddRange(a, b); seed.People.AddRange(p, q); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var coordinator = MakeCoordinator(db); var main = new MainWindow(coordinator, Path.GetTempPath(), coordinateExit: true); main.Show();
            var window = new WorkbenchProfileWindow(coordinator) { Owner = main }; window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(a.Id); await window.SelectPersonForTest(p.Id);
            await window.PlacePersonForTest(2); Assert.Equal(p.Id, window.CanvasPersonForTest);
            window.SetNameForTest("草稿"); window.ChooseForTest = () => DraftLeaveChoice.Cancel;
            await window.SelectPersonForTest(q.Id); Assert.Equal(p.Id, window.SessionForTest.Draft!.Original.PersonId);
            Assert.Equal(p.Id, window.CanvasPersonForTest); Assert.Equal(2, window.CanvasColumnForTest);
            window.Close(); await Dispatcher.Yield(DispatcherPriority.Background); Assert.True(window.IsVisible);
            window.ChooseForTest = () => DraftLeaveChoice.Discard;
            await window.SelectPersonForTest(q.Id); Assert.Equal(q.Id, window.SessionForTest.Draft!.Original.PersonId);
            window.SetNameForTest("草稿"); window.ChooseForTest = () => DraftLeaveChoice.Cancel;
            await window.SelectCanvasForTest(); Assert.Equal(q.Id, window.SessionForTest.Draft!.Original.PersonId);
            await window.SelectGenealogyForTest(b.Id); Assert.Equal("草稿", window.SessionForTest.Draft!.Values.Name);
            window.ChooseForTest = () => DraftLeaveChoice.Discard;
            await window.SelectGenealogyForTest(b.Id); Assert.Null(window.CanvasPersonForTest); Assert.Null(window.SessionForTest.Draft);
            await window.SelectGenealogyForTest(a.Id); await window.SelectPersonForTest(q.Id);
            window.SetNameForTest("草稿"); window.ChooseForTest = () => DraftLeaveChoice.Cancel;
            main.Close(); await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.True(main.IsVisible); Assert.True(window.SessionForTest.Draft.IsDirty);
            window.ChooseForTest = () => DraftLeaveChoice.Save;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            main.Closed += (_, _) => closed.TrySetResult(); main.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        });
        await using var verify = db.CreateContext(); Assert.Equal("甲人", (await verify.People.SingleAsync(x => x.Id == p.Id)).Name);
        Assert.Equal("草稿", (await verify.People.SingleAsync(x => x.Id == q.Id)).Name);
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Window_reports_commit_when_refresh_fails_without_retry()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var p = new Person { GenealogyId = g.Id, Name = "原名" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(p); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var coordinator = MakeCoordinator(db);
            var session = new WorkbenchDraftSession(coordinator.EnterOperation,
                async (gid, pid) => { await using var context = coordinator.CreateContext(); return await new WorkbenchPersonProfileCommand(context).ReadAsync(gid, pid); },
                async draft => { await using var context = coordinator.CreateContext(); await draft.SaveAsync(new WorkbenchPersonProfileCommand(context)); });
            var window = new WorkbenchProfileWindow(coordinator, session); window.Show();
            window.AfterCommitAsync = _ => Task.FromException(new InvalidOperationException("注入刷新故障"));
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(p.Id);
            await window.PlacePersonForTest(1); Assert.Equal(p.Id, window.CanvasPersonForTest);
            window.SetNameForTest("新名"); await window.SaveForTest();
            Assert.Contains("已保存，但刷新失败", window.StatusForTest); Assert.Null(session.Draft);
            Assert.Null(window.CanvasPersonForTest);
            await window.SaveForTest(); Assert.Contains("已保存，但刷新失败", window.StatusForTest);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal("新名", (await verify.People.SingleAsync()).Name);
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Canvas_restores_after_save_and_external_change_clears_it_without_losing_draft()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var p = new Person { GenealogyId = g.Id, Name = "旧名", RecordedGeneration = "二世" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(p); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(p.Id);
            await window.PlacePersonForTest(2); window.SetNameForTest("新名"); await window.SaveForTest();
            Assert.Equal(p.Id, window.CanvasPersonForTest); Assert.Equal("新名", window.CanvasNameForTest); Assert.Equal(2, window.CanvasColumnForTest);
            window.SetNameForTest("未保存草稿");
            await using (var other = db.CreateContext()) await other.People.Where(x => x.Id == p.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Biography, "外部变更"));
            await window.PollCanvasForTest(); Assert.Null(window.CanvasPersonForTest);
            Assert.True(window.SessionForTest.Draft!.IsDirty); Assert.Equal("未保存草稿", window.SessionForTest.Draft.Values.Name);
            window.ChooseForTest = () => DraftLeaveChoice.Discard;
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Changed_generation_during_refresh_is_not_restored_to_old_column()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var p = new Person { GenealogyId = g.Id, Name = "人物", RecordedGeneration = "二世" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(p); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            window.AfterCommitAsync = async _ => { await using var other = db.CreateContext(); await other.People.Where(x => x.Id == p.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RecordedGeneration, "三世")); };
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(p.Id);
            await window.PlacePersonForTest(2); window.SetNameForTest("已保存"); await window.SaveForTest();
            Assert.Null(window.CanvasPersonForTest); Assert.Contains("已保存，但刷新失败", window.StatusForTest);
            Assert.Null(window.SessionForTest.Draft);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); var person = await verify.People.SingleAsync();
        Assert.Equal("已保存", person.Name); Assert.Equal("三世", person.RecordedGeneration);
    }
    private static TargetContextCoordinator MakeCoordinator(TestDatabase db) => new(new DbContextOptionsBuilder<ZupuDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()).Options);
    private static async Task RunSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }
}
