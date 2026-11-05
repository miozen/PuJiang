using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        var p = new Person { GenealogyId = a.Id, Name = "甲人", RecordedGeneration = "二世" }; var q = new Person { GenealogyId = a.Id, Name = "甲二", RecordedGeneration = "三世" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.AddRange(a, b); seed.People.AddRange(p, q); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var coordinator = MakeCoordinator(db); var main = new MainWindow(coordinator, Path.GetTempPath(), coordinateExit: true); main.Show();
            var window = new WorkbenchProfileWindow(coordinator) { Owner = main }; window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(a.Id); await window.SelectPersonForTest(p.Id);
            Assert.True(window.CanvasContainsForTest(p.Id));
            window.SetNameForTest("草稿"); window.ChooseForTest = () => DraftLeaveChoice.Cancel;
            await window.SelectPersonForTest(q.Id); Assert.Equal(p.Id, window.SessionForTest.Draft!.Original.PersonId);
            Assert.True(window.CanvasContainsForTest(p.Id)); Assert.Equal(2, window.CanvasColumnForTest(p.Id));
            window.Close(); await Dispatcher.Yield(DispatcherPriority.Background); Assert.True(window.IsVisible);
            window.ChooseForTest = () => DraftLeaveChoice.Discard;
            await window.SelectPersonForTest(q.Id); Assert.Equal(q.Id, window.SessionForTest.Draft!.Original.PersonId);
            window.SetNameForTest("草稿"); window.ChooseForTest = () => DraftLeaveChoice.Cancel;
            await window.SelectCanvasForTest(p.Id); Assert.Equal(q.Id, window.SessionForTest.Draft!.Original.PersonId);
            await window.SelectGenealogyForTest(b.Id); Assert.Equal("草稿", window.SessionForTest.Draft!.Values.Name);
            window.ChooseForTest = () => DraftLeaveChoice.Discard;
            await window.SelectGenealogyForTest(b.Id); Assert.False(window.CanvasContainsForTest(p.Id)); Assert.Null(window.SessionForTest.Draft);
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
        var g = new Genealogy { Name = "谱" }; var p = new Person { GenealogyId = g.Id, Name = "原名", RecordedGeneration = "1" };
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
            Assert.True(window.CanvasContainsForTest(p.Id));
            window.SetNameForTest("新名"); await window.SaveForTest();
            Assert.Contains("已保存，但刷新失败", window.StatusForTest); Assert.Null(session.Draft);
            Assert.False(window.CanvasContainsForTest(p.Id));
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
        var g = new Genealogy { Name = "虚构谱" }; var p = new Person { GenealogyId = g.Id, Name = "旧名", RecordedGeneration = "二世", BirthDateText = null, DeathDateText = "", Biography = "保留原文" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(p); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(p.Id);
            Assert.True(window.CanvasContainsForTest(p.Id)); window.SetNameForTest("新名"); await window.SaveForTest();
            Assert.True(window.CanvasContainsForTest(p.Id)); Assert.Equal("新名", window.CanvasNameForTest(p.Id)); Assert.Equal(2, window.CanvasColumnForTest(p.Id));
            await using (var verify = db.CreateContext()) { var saved = await verify.People.SingleAsync(); Assert.Null(saved.BirthDateText); Assert.Equal("", saved.DeathDateText); Assert.Equal("保留原文", saved.Biography); }
            window.SetNameForTest("未保存草稿");
            await using (var other = db.CreateContext()) await other.People.Where(x => x.Id == p.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Biography, "外部变更"));
            await window.PollCanvasForTest(); Assert.False(window.CanvasContainsForTest(p.Id));
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
            Assert.True(window.CanvasContainsForTest(p.Id)); window.SetNameForTest("已保存"); await window.SaveForTest();
            Assert.False(window.CanvasContainsForTest(p.Id)); Assert.Contains("已保存，但刷新失败", window.StatusForTest);
            Assert.Null(window.SessionForTest.Draft);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); var person = await verify.People.SingleAsync();
        Assert.Equal("已保存", person.Name); Assert.Equal("三世", person.RecordedGeneration);
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Direct_new_person_and_child_use_drafts_prefill_and_draw_confirmed_relation()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "虚构谱" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); await seed.SaveChangesAsync(); }
        Guid rootId = default, childId = default, spouseId = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            var canvas = window.CanvasForTest;
            canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            var create = Assert.IsType<MenuItem>(Assert.Single(canvas.ContextMenu!.Items));
            create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.SessionForTest.Draft?.Original.IsNew == true);
            rootId = window.SessionForTest.Draft!.Original.PersonId;
            Assert.True(window.SessionForTest.Draft.IsDirty); window.SetNameForTest("始祖");
            await window.SaveForTest(); Assert.True(window.CanvasContainsForTest(rootId)); Assert.Equal(1, window.CanvasColumnForTest(rootId));
            var card = window.CanvasCardForTest(rootId);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(rootId).ContextMenu is not null);
            var childMenu = window.CanvasCardForTest(rootId).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增子女（生物关系）"));
            childMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.SessionForTest.Draft?.Original.IsNew == true);
            childId = window.SessionForTest.Draft!.Original.PersonId;
            Assert.Equal("2", window.SessionForTest.Draft.Values.RecordedGeneration);
            window.SetNameForTest("子女"); await window.SaveForTest();
            Assert.True(window.CanvasContainsForTest(childId)); Assert.Equal(2, window.CanvasColumnForTest(childId));
            Assert.Equal(1, window.CanvasEdgesForTest);
            card = window.CanvasCardForTest(rootId);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(rootId).ContextMenu is not null);
            var spouseMenu = window.CanvasCardForTest(rootId).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增配偶（婚配）"));
            spouseMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.SessionForTest.Draft?.Original.IsNew == true);
            spouseId = window.SessionForTest.Draft!.Original.PersonId; Assert.Null(window.SessionForTest.Draft.Values.RecordedGeneration);
            window.SetNameForTest("配偶"); await window.SaveForTest();
            Assert.Contains("婚配：配偶", window.CanvasPartnersForTest(rootId));
            Assert.False(window.CanvasContainsForTest(spouseId));
            await window.SelectCanvasForTest(spouseId); Assert.Equal(spouseId, window.SessionForTest.Draft!.Original.PersonId);
            window.SetNameForTest("配偶改名"); await window.SaveForTest();
            Assert.Contains("婚配：配偶改名", window.CanvasPartnersForTest(rootId));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext();
        var edge = await verify.PersonRelationships.SingleAsync(); Assert.Equal(rootId, edge.RelatedPersonId); Assert.Equal(childId, edge.PersonId);
        var union = await verify.FamilyUnions.SingleAsync(); Assert.Contains(rootId, new Guid?[] { union.PartnerOneId, union.PartnerTwoId });
        Assert.Contains(spouseId, new Guid?[] { union.PartnerOneId, union.PartnerTwoId });
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Right_click_adds_sibling_without_inventing_a_parent()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var person = new Person { GenealogyId = g.Id, Name = "人物", RecordedGeneration = "二世" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(person); await seed.SaveChangesAsync(); }
        Guid siblingId = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            var card = window.CanvasCardForTest(person.Id);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(person.Id).ContextMenu is not null);
            var sibling = window.CanvasCardForTest(person.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增兄妹"));
            sibling.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.SessionForTest.Draft?.Original.IsNew == true);
            siblingId = window.SessionForTest.Draft!.Original.PersonId; Assert.Equal("2", window.SessionForTest.Draft.Values.RecordedGeneration);
            window.SetNameForTest("兄妹"); await window.SaveForTest();
            Assert.True(window.CanvasContainsForTest(siblingId)); Assert.Contains("人物", window.CanvasSiblingsForTest(siblingId));
            Assert.Contains("兄妹", window.CanvasSiblingsForTest(person.Id));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); var relation = await verify.PersonRelationships.SingleAsync();
        Assert.Equal(PersonRelationshipKind.Sibling, relation.Kind); Assert.Equal(siblingId, relation.PersonId); Assert.Equal(person.Id, relation.RelatedPersonId);
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Sibling_with_multiple_parents_requires_explicit_choice()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var person = new Person { GenealogyId = g.Id, Name = "人物", RecordedGeneration = "二世" };
        var a = new Person { GenealogyId = g.Id, Name = "生父", RecordedGeneration = "一世" };
        var b = new Person { GenealogyId = g.Id, Name = "嗣父", RecordedGeneration = "一世" };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(person, a, b);
            seed.PersonRelationships.AddRange(new PersonRelationship { GenealogyId = g.Id, PersonId = person.Id, RelatedPersonId = a.Id, Kind = PersonRelationshipKind.BiologicalParent },
                new PersonRelationship { GenealogyId = g.Id, PersonId = person.Id, RelatedPersonId = b.Id, Kind = PersonRelationshipKind.HeirParent });
            await seed.SaveChangesAsync();
        }
        Guid siblingId = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            var card = window.CanvasCardForTest(person.Id);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(person.Id).ContextMenu is not null);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                var dialog = window.OwnedWindows.OfType<Window>().FirstOrDefault(w => w.Title == "选择共同父母");
                if (dialog?.Content is not StackPanel panel) return;
                var boxes = panel.Children.OfType<ComboBox>().ToArray(); var list = boxes[0]; var confirm = panel.Children.OfType<Button>().Single();
                Assert.False(confirm.IsEnabled);
                list.SelectedIndex = list.Items.Cast<object>().ToList().FindIndex(x => x.ToString()!.Contains("嗣父"));
                boxes[1].SelectedIndex = 2;
                Assert.True(confirm.IsEnabled); timer.Stop(); confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            var sibling = window.CanvasCardForTest(person.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增兄妹"));
            sibling.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.SessionForTest.Draft?.Original.IsNew == true);
            siblingId = window.SessionForTest.Draft!.Original.PersonId; window.SetNameForTest("兄妹"); await window.SaveForTest();
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); var relations = await verify.PersonRelationships.Where(r => r.PersonId == siblingId).ToListAsync();
        Assert.Contains(relations, r => r.Kind == PersonRelationshipKind.HeirParent && r.RelatedPersonId == b.Id);
        Assert.DoesNotContain(relations, r => r.RelatedPersonId == a.Id);
        Assert.Contains(relations, r => r.Kind == PersonRelationshipKind.Sibling && r.RelatedPersonId == person.Id);
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Right_click_edit_changes_generation_without_a_separate_mapping_step()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var p = new Person { GenealogyId = g.Id, Name = "人物", RecordedGeneration = "一世" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(p); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest();
            await window.SelectGenealogyForTest(g.Id); Assert.Equal(1, window.CanvasColumnForTest(p.Id));
            var card = window.CanvasCardForTest(p.Id);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(p.Id).ContextMenu is not null);
            var edit = window.CanvasCardForTest(p.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "编辑人物"));
            edit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); window.SetGenerationForTest("3");
            await window.SaveForTest(); Assert.Equal(3, window.CanvasColumnForTest(p.Id));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal("3", (await verify.People.SingleAsync()).RecordedGeneration);
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Stale_canvas_cannot_start_a_child_for_a_changed_parent()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var p = new Person { GenealogyId = g.Id, Name = "父", RecordedGeneration = "1" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(p); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectCanvasForTest(p.Id);
            await using (var other = db.CreateContext()) await other.People.Where(x => x.Id == p.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "外部变更"));
            await window.BeginNewForTest(p.Id, PersonRelationshipKind.BiologicalParent);
            Assert.False(window.CanvasContainsForTest(p.Id)); Assert.Equal(p.Id, window.SessionForTest.Draft!.Original.PersonId);
            Assert.False(window.SessionForTest.Draft.Original.IsNew); Assert.Contains("未新建人物", window.StatusForTest);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal(1, await verify.People.CountAsync()); Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Unresolved_generation_and_sibling_order_remain_visible_as_diagnostics()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var root = new Person { GenealogyId = g.Id, Name = "父", RecordedGeneration = "一世" };
        var a = new Person { GenealogyId = g.Id, Name = "甲", RecordedGeneration = "二世" }; var b = new Person { GenealogyId = g.Id, Name = "乙", RecordedGeneration = "第二世" };
        var unknown = new Person { GenealogyId = g.Id, Name = "待考人物", RecordedGeneration = "待考" };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(root, a, b, unknown);
            seed.PersonRelationships.AddRange(new PersonRelationship { GenealogyId = g.Id, PersonId = a.Id, RelatedPersonId = root.Id, Kind = PersonRelationshipKind.BiologicalParent },
                new PersonRelationship { GenealogyId = g.Id, PersonId = b.Id, RelatedPersonId = root.Id, Kind = PersonRelationshipKind.BiologicalParent });
            await seed.SaveChangesAsync();
        }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            Assert.True(window.CanvasContainsForTest(root.Id)); Assert.True(window.CanvasContainsForTest(a.Id)); Assert.True(window.CanvasContainsForTest(b.Id));
            Assert.False(window.CanvasContainsForTest(unknown.Id)); Assert.Equal(0, window.CanvasEdgesForTest);
            Assert.Contains(window.DiagnosticsForTest, text => text.Contains("待考人物"));
            Assert.Contains(window.DiagnosticsForTest, text => text.Contains("多名子女的顺序"));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
    }
    private static TargetContextCoordinator MakeCoordinator(TestDatabase db) => new(new DbContextOptionsBuilder<ZupuDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()).Options);
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "WPF action did not complete");
    }
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
