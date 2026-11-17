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
    [Fact, Trait("Category", "UI")]
    public async Task Child_order_updates_right_panel_and_focus_after_reopen()
    {
        var (db,g,l)=await CreateAsync(); var roots=new FirstRootApplication(new IsolatedFirstRootStore(db)); var children=new ChildApplication(new IsolatedChildStore(db)); var spouses=new SpouseApplication(new IsolatedSpouseStore(db)); var profiles=new PersonProfileApplication(new IsolatedPersonProfileStore(db)); var orders=new ChildOrderApplication(new IsolatedChildOrderStore(db));
        var root=await roots.ExecuteAsync(new(Guid.NewGuid(),g,l,RootNameMode.Named,"冯父",1));
        var a=await children.ExecuteAsync(new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"冯甲",0,2,1));
        var b=await children.ExecuteAsync(new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"冯乙",0,2,1));
        await RunSta(async()=> { var w=new V1FirstRootWindow(roots,g,l,"冯",children,spouses,profiles,childOrders:orders); w.Show(); try { await w.ReadyForTest; await w.SelectForTest(b.PersonId,b.PlacementId); Assert.Contains("次子",w.FamilyTextForTest); await w.MoveChildOrderForTest(root.PlacementId,b.PlacementId,a.PlacementId); Assert.Contains("长子",w.FamilyTextForTest); Assert.Equal((b.PersonId,b.PlacementId),(w.FocusForTest.Person!.Value,w.FocusForTest.Placement!.Value)); } finally {w.Close();} });
        await RunSta(async()=> { var w=new V1FirstRootWindow(roots,g,l,"冯",children,spouses,profiles,childOrders:orders); w.Show(); try { await w.ReadyForTest; await w.SelectForTest(b.PersonId,b.PlacementId); Assert.Contains("长子",w.FamilyTextForTest); } finally {w.Close();} });
    }
    [Fact, Trait("Category", "UI")]
    public async Task Child_order_failure_keeps_intent_and_retry_saves()
    {
        var (db,g,l)=await CreateAsync(); var roots=new FirstRootApplication(new IsolatedFirstRootStore(db)); var children=new ChildApplication(new IsolatedChildStore(db)); var spouses=new SpouseApplication(new IsolatedSpouseStore(db)); var profiles=new PersonProfileApplication(new IsolatedPersonProfileStore(db)); var orders=new ChildOrderApplication(new IsolatedChildOrderStore(db));
        var root=await roots.ExecuteAsync(new(Guid.NewGuid(),g,l,RootNameMode.Named,"冯父",1));
        var a=await children.ExecuteAsync(new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"冯甲",0,2,1)); var b=await children.ExecuteAsync(new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,RootNameMode.Named,"冯乙",0,2,1));
        await using(var c=db.OpenContext()) await c.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailChildOrderUI BEFORE INSERT ON ChildOrderReceipts BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await RunSta(async()=> { var w=new V1FirstRootWindow(roots,g,l,"冯",children,spouses,profiles,childOrders:orders); w.Show(); try { await w.ReadyForTest; await w.SelectForTest(b.PersonId,b.PlacementId); await w.MoveChildOrderForTest(root.PlacementId,b.PlacementId,a.PlacementId); Assert.True(w.ChildOrderPendingForTest); Assert.Contains("未保存",w.StatusForTest); Assert.False(w.CanCloseForOwner()); await using(var c=db.OpenContext()) await c.Database.ExecuteSqlRawAsync("DROP TRIGGER FailChildOrderUI"); await w.RetryChildOrderForTest(); Assert.False(w.ChildOrderPendingForTest); Assert.Contains("长子",w.FamilyTextForTest); } finally {w.Close();} });
    }
    [Fact, Trait("Category", "UI")]
    public async Task Sibling_draft_cancels_and_stale_mother_preserves_input()
    {
        var (db, g, l) = await CreateAsync();
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db)); var children = new ChildApplication(new IsolatedChildStore(db)); var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", Generation: 2, ExpectedParentGeneration: 1));
        await using (var context = db.OpenContext()) { (await context.People.SingleAsync(x => x.Id == child.PersonId)).SetGeneration(6); await context.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(roots, g, l, "冯", children, spouses); window.Show();
            try
            {
                await window.ReadyForTest; await window.SelectForTest(child.PersonId, child.PlacementId);
                window.BeginSiblingForTest(); Assert.Null(window.GenerationInputForTest); window.SetInputForTest("取消"); window.CancelForTest(); Assert.Single(await children.ReadAsync(g, l));
                window.BeginSiblingForTest(); window.SetInputForTest("草稿");
                await spouses.SetMotherAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, child.PersonId, null, null, false));
                await window.ConfirmForTest(); Assert.Equal("草稿", window.InputForTest); Assert.Contains("未保存", window.StatusForTest); Assert.Single(await children.ReadAsync(g, l));
                Assert.True(window.CanRebindChildForTest); await window.RebindChildForTest(); Assert.Equal("草稿", window.InputForTest);
                window.SetInputForTest("兄弟"); await window.ConfirmForTest();
                Assert.Null(window.InputForTest); Assert.Equal(2, window.ChildCountForTest); Assert.NotEqual(child.PersonId, window.FocusForTest.Person);
            }
            finally { if (window.InputForTest is not null) window.CancelForTest(); window.Close(); }
        });
    }
    [Fact, Trait("Category", "UI")]
    public async Task Explicit_cross_generation_facts_are_read_and_drawn_directly_after_reopen()
    {
        var (db, g, l) = await CreateAsync();
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var children = new ChildApplication(new IsolatedChildStore(db));
        var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 8));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", Generation: 9, ExpectedParentGeneration: 8));
        await using (var context = db.OpenContext())
        {
            var person = await context.People.SingleAsync(x => x.Id == child.PersonId);
            person.SetRootInformation(15, "", "");
            await context.SaveChangesAsync();
        }
        Assert.Equal(15, Assert.Single(await children.ReadAsync(g, l)).Generation);
        for (var i = 0; i < 2; i++) await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(roots, g, l, "冯", children, new SpouseApplication(new IsolatedSpouseStore(db)));
            window.Show();
            try
            {
                await window.ReadyForTest; window.UpdateLayout();
                var cards = window.CanvasForTest.Children.OfType<System.Windows.Controls.Border>().ToArray();
                var father = Assert.Single(cards, x => x.Tag is RootNode);
                var son = Assert.Single(cards, x => x.Tag is ChildNode);
                var fatherX = System.Windows.Controls.Canvas.GetLeft(father);
                var sonX = System.Windows.Controls.Canvas.GetLeft(son);
                Assert.Equal(20, fatherX);
                Assert.True(sonX - fatherX > 700);
                var heading = Assert.Single(window.GenerationHeaderForTest.Children.OfType<System.Windows.Controls.TextBlock>(), x => x.Text == "第 8 世");
                Assert.Equal(20, System.Windows.Controls.Canvas.GetLeft(heading), 2);
                window.SetViewportForTest(1.5, 0);
                heading = Assert.Single(window.GenerationHeaderForTest.Children.OfType<System.Windows.Controls.TextBlock>(), x => x.Text == "第 8 世");
                Assert.Equal(30, System.Windows.Controls.Canvas.GetLeft(heading), 2);
                window.SetViewportForTest(1, 174);
                Assert.Contains(window.GenerationHeaderForTest.Children.OfType<System.Windows.Controls.TextBlock>(), x => x.Text == "第 9 世");
                var line = Assert.Single(window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>());
                Assert.Contains(line.Points, p => Math.Abs(p.X - sonX) < 0.1 && Math.Abs(p.Y - (System.Windows.Controls.Canvas.GetTop(son) + 17)) < 0.1);
            }
            finally { window.Close(); }
        });
        await using var verify = db.OpenContext();
        Assert.Equal(2, await verify.People.CountAsync()); Assert.Equal(2, await verify.Placements.CountAsync());
        Assert.Equal(1, await verify.BiologicalParents.CountAsync());
        Assert.Equal(15, (await verify.People.SingleAsync(x => x.Id == child.PersonId)).ExplicitGeneration);
    }
    [Fact, Trait("Category", "UI")]
    public async Task Switching_lineages_shows_isolated_home_and_opens_root()
    {
        var (db,g,l)=await CreateAsync();var lines=new LineageApplication(new IsolatedLineageStore(db));var roots=new FirstRootApplication(new IsolatedFirstRootStore(db));
        var second=await lines.CreateAsync(new(Guid.NewGuid(),g,"另一谱","分类","地区","主题","",""));
        var root=await roots.ExecuteAsync(new(Guid.NewGuid(),g,second,RootNameMode.Named,"冯乙", 1));
        await RunSta(async()=>
        {
            var window=new V1FirstRootWindow(roots,g,l,"冯",new ChildApplication(new IsolatedChildStore(db)),new SpouseApplication(new IsolatedSpouseStore(db)),null,lines);window.Show();
            try {await window.ReadyForTest;await window.SwitchLineageForTest(second);Assert.Equal(second,window.LineageForTest);Assert.True(window.HomeVisibleForTest);Assert.Contains("人物总数：1",window.HomeTextForTest);Assert.Contains("创建首节点",window.HomeTextForTest);await window.EnterCanvasForTest(new(root.PersonId,root.PlacementId,"冯乙"));Assert.False(window.HomeVisibleForTest);Assert.Equal(root.PersonId,window.FocusForTest.Person);Assert.Equal(root.PlacementId,window.FocusForTest.Placement);await window.SwitchLineageForTest(l);Assert.Contains("人物总数：0",window.HomeTextForTest);Assert.Null(window.FocusForTest.Person);}
            finally {window.Close();}
        });
    }
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
                await window.ReadyForTest; window.BeginForTest(); window.SetGenerationForTest("1"); window.SetInputForTest("初稿"); store.FailBeforeSave = true;
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
    public async Task Blank_and_cancel_do_not_create_person_and_refresh_failure_reports_saved()
    {
        var (db, g, l) = await CreateAsync(); var store = new FaultStore(new IsolatedFirstRootStore(db));
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(new FirstRootApplication(store), g, l, "冯"); window.Show();
            try
            {
                await window.ReadyForTest; window.BeginForTest(); window.SetGenerationForTest("1"); window.SetInputForTest("暂不提交"); window.CancelForTest();
                Assert.Empty((await new FirstRootApplication(store).ReadAsync(g,l)).Nodes);
                window.BeginForTest(); window.SetGenerationForTest("1"); window.SetInputForTest("  "); await window.ConfirmForTest();
                Assert.Empty((await new FirstRootApplication(store).ReadAsync(g,l)).Nodes);
                window.SetInputForTest("甲"); store.FailRead = true; await window.ConfirmForTest();
                Assert.Contains("已保存，但画布读取失败", window.StatusForTest); var focus = window.FocusForTest;
                Assert.NotNull(focus.Person); Assert.NotNull(focus.Placement);
                await window.ReloadForTest(); Assert.Equal(focus, window.FocusForTest); Assert.Equal(1, window.NodeCountForTest);
            }
            finally { window.Close(); }
        });
        await using var verify = db.OpenContext(); Assert.Equal("冯甲", (await verify.People.SingleAsync()).Name);
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
                await window.ReadyForTest; window.BeginForTest(); window.SetGenerationForTest("1");
                var host = Assert.IsType<System.Windows.Controls.StackPanel>(window.InputHostForTest);
                Assert.Equal("冯", Assert.IsType<System.Windows.Controls.TextBlock>(host.Children[0]).Text);
                Assert.Same(window.InputControlForTest, host.Children[1]);
                window.SetInputForTest("取消");
                SendKey(window, Key.Escape); Assert.Null(window.InputForTest); Assert.Null(window.InputHostForTest);
                window.BeginForTest(); window.SetGenerationForTest("1"); window.SetInputForTest("朝阳"); SendKey(window, Key.Enter);
                for (var i = 0; i < 200 && window.NodeCountForTest == 0; i++) await Task.Delay(10);
                Assert.Equal(1, window.NodeCountForTest); Assert.NotNull(window.FocusForTest.Placement); Assert.Null(window.InputHostForTest);
            }
            finally { if (window.InputForTest is not null && !window.InputReadOnlyForTest) window.CancelForTest(); window.Close(); }
        });
        await using var context = db.OpenContext(); Assert.Equal("冯朝阳", (await context.People.SingleAsync()).Name);
    }
    private sealed class FaultChildStore(IChildStore inner) : IChildStore
    {
        public bool FailBefore, FailAfter;
        public async Task<ChildCreated> AddAsync(AddChild command, CancellationToken cancellationToken)
        {
            if (FailBefore) { FailBefore = false; throw new InvalidOperationException("模拟提交前失败"); }
            var result = await inner.AddAsync(command, cancellationToken);
            if (FailAfter) { FailAfter = false; throw new InvalidOperationException("模拟提交结果丢失"); }
            return result;
        }
        public Task<IReadOnlyList<ChildNode>> ReadAsync(Guid g, Guid l, CancellationToken cancellationToken) => inner.ReadAsync(g, l, cancellationToken);
        public Task<bool> HasReceiptAsync(Guid id, CancellationToken cancellationToken) => inner.HasReceiptAsync(id, cancellationToken);
    }
    [Fact, Trait("Category", "UI")]
    public async Task Workbench_offers_explicit_repair_of_damaged_order()
    {
        var (db, g, l) = await CreateAsync(); var roots = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var a = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var b = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "王氏"));
        await using (var c = db.OpenContext()) { var row = await c.SpouseOrderEntries.SingleAsync(x => x.UnionId == b.UnionId); row.Position = 3; await c.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(roots, g, l, "冯", new ChildApplication(new IsolatedChildStore(db)), spouses); window.Show();
            try
            {
                await window.ReadyForTest; await window.SelectForTest(root.PersonId, root.PlacementId);
                Assert.NotNull(window.RepairButtonForTest);
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        V1SpouseOrderRepairWindow? dialog = null;
                        for (var i = 0; i < 300 && dialog is null; i++) { await Task.Delay(10); dialog = window.OwnedWindows.OfType<V1SpouseOrderRepairWindow>().SingleOrDefault(); }
                        Assert.NotNull(dialog); Assert.False(dialog!.SaveButtonForTest.IsEnabled);
                        dialog.CandidateButtonsForTest.Single(x => x.Content.ToString()!.Contains("王氏")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        Assert.False(dialog.SaveButtonForTest.IsEnabled);
                        dialog.CandidateButtonsForTest.Single(x => x.Content.ToString()!.Contains("李氏")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        Assert.True(dialog.SaveButtonForTest.IsEnabled); dialog.SaveButtonForTest.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        for (var i = 0; i < 300 && dialog.IsVisible; i++) await Task.Delay(10);
                        Assert.False(dialog.IsVisible); finished.SetResult();
                    }
                    catch (Exception error) { finished.SetException(error); foreach (var d in window.OwnedWindows.OfType<V1SpouseOrderRepairWindow>().ToArray()) d.Close(); }
                });
                window.RepairButtonForTest!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var read = await spouses.ReadSnapshotAsync(g, l, root.PersonId, root.PlacementId); Assert.Equal(new[] { b.UnionId, a.UnionId }, read.Nodes.Select(x => x.UnionId));
            }
            finally { window.Close(); }
        });
    }
    [Fact, Trait("Category", "UI")]
    public async Task Stale_child_draft_is_preserved_and_can_be_explicitly_rebound()
    {
        var (db, g, l) = await CreateAsync();
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var children = new ChildApplication(new IsolatedChildStore(db));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var first = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var second = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "王氏"));
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(roots, g, l, "冯", children, spouses); window.Show();
            try
            {
                await window.ReadyForTest; await window.SelectForTest(root.PersonId, root.PlacementId);
                window.BeginChildForTest(); window.SetInputForTest("保留");
                await spouses.MoveAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, second.UnionId, first.UnionId, 2));
                await window.ConfirmForTest(); Assert.Equal("保留", window.InputForTest); Assert.True(window.CanRebindChildForTest);
                Assert.Empty(await children.ReadAsync(g, l)); Assert.False(window.CanCloseForOwner());
                await window.RebindChildForTest(); Assert.Equal("保留", window.InputForTest); Assert.False(window.CanRebindChildForTest);
                await window.ConfirmForTest(); Assert.Null(window.InputForTest);
                Assert.Equal(second.PersonId, Assert.Single(await children.ReadAsync(g, l)).MotherId);
            }
            finally { window.Close(); }
        });
    }
    [Fact, Trait("Category", "UI")]
    public async Task Child_and_spouse_flow_preserves_focus_and_clear_across_reopen()
    {
        var (db, g, l) = await CreateAsync();
        var rootApp = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var childApp = new ChildApplication(new IsolatedChildStore(db));
        var spouseApp = new SpouseApplication(new IsolatedSpouseStore(db));
        var root = await rootApp.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        (Guid? Person, Guid? Placement) focus = default;
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(rootApp, g, l, "冯", childApp, spouseApp); window.Show();
            try
            {
                await window.ReadyForTest; await window.SelectForTest(root.PersonId, root.PlacementId);
                window.BeginChildForTest(); window.SetInputForTest("取消"); window.CancelForTest();
                Assert.Empty(await childApp.ReadAsync(g, l));
                window.BeginChildForTest(); Assert.Null(window.GenerationInputForTest); window.SetInputForTest("子"); await window.ConfirmForTest(); focus = window.FocusForTest;
                Assert.NotNull(focus.Person); Assert.NotEqual(root.PersonId, focus.Person); Assert.NotNull(focus.Placement); Assert.Equal(1, window.ChildCountForTest);
                await window.ChooseMotherForTest(null);
                await window.SelectForTest(root.PersonId, root.PlacementId);
                window.BeginSpouseForTest(); Assert.Single(Assert.IsType<System.Windows.Controls.StackPanel>(window.InputHostForTest).Children);
                window.SetInputForTest("李氏"); await window.ConfirmForTest();
                await window.SelectForTest(focus.Person!.Value, focus.Placement!.Value);
                var savedChild = Assert.Single(await childApp.ReadAsync(g, l)); Assert.True(savedChild.MotherExplicitlyCleared); Assert.Equal(2, savedChild.Generation);
                await window.ReloadForTest(); Assert.Equal(focus, window.FocusForTest);
            }
            finally { window.Close(); }
        });
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(rootApp, g, l, "冯", childApp, spouseApp); window.Show();
            try { await window.ReadyForTest; Assert.Equal(1, window.ChildCountForTest); }
            finally { window.Close(); }
        });
        await using var context = db.OpenContext();
        Assert.Equal("冯子", (await context.People.SingleAsync(x => x.Id == focus.Person)).Name);
        Assert.Empty(await context.BiologicalParents.Where(x => x.ChildId == focus.Person && x.Role == ParentRole.Mother).ToListAsync());
    }
    [Fact, Trait("Category", "UI")]
    public async Task Child_failure_preserves_editable_draft_and_uncertain_commit_replays()
    {
        var (db, g, l) = await CreateAsync();
        var rootApp = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var root = await rootApp.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var store = new FaultChildStore(new IsolatedChildStore(db));
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(rootApp, g, l, "冯", new ChildApplication(store), new SpouseApplication(new IsolatedSpouseStore(db))); window.Show();
            try
            {
                await window.ReadyForTest; await window.SelectForTest(root.PersonId, root.PlacementId);
                window.BeginChildForTest(); window.SetInputForTest("初稿"); store.FailBefore = true; await window.ConfirmForTest();
                Assert.Equal("初稿", window.InputForTest); Assert.False(window.InputReadOnlyForTest); Assert.Contains("未保存", window.StatusForTest);
                window.SetInputForTest("新名"); store.FailAfter = true; await window.ConfirmForTest();
                Assert.Contains("未确认", window.StatusForTest); Assert.False(window.CanCloseForOwner());
                await window.RetryFamilyForTest(); Assert.Null(window.InputForTest); Assert.Equal(1, window.ChildCountForTest);
                Assert.NotEqual(root.PersonId, window.FocusForTest.Person);
            }
            finally { window.Close(); }
        });
        await using var context = db.OpenContext(); Assert.Single(await context.People.Where(x => x.Name == "冯新名").ToListAsync());
    }
    [Fact, Trait("Category", "UI")]
    public async Task Empty_child_profile_can_be_named_and_sex_changed_without_replacing_identity()
    {
        var (db, g, l) = await CreateAsync();
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var children = new ChildApplication(new IsolatedChildStore(db));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var profiles = new PersonProfileApplication(new IsolatedPersonProfileStore(db));
        var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", Generation: 2, ExpectedParentGeneration: 1));
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(roots, g, l, "冯", children, spouses, profiles); window.Show();
            try
            {
                await window.ReadyForTest; await window.SelectForTest(child.PersonId, child.PlacementId);
                await window.EditProfileForTest("冯女", PersonSex.Female);
                Assert.Equal((child.PersonId, child.PlacementId), window.FocusForTest);
                await window.ReloadForTest();
                Assert.Equal((child.PersonId, child.PlacementId), window.FocusForTest);
            }
            finally { window.Close(); }
        });
        var profile = await profiles.ReadAsync(g, l, child.PersonId, child.PlacementId);
        Assert.Equal("冯女", profile.Name); Assert.Equal(PersonSex.Female, profile.Sex);
    }
    [Fact, Trait("Category", "UI")]
    public async Task Child_descendants_remain_within_scrollable_canvas_after_viewport_change()
    {
        var (db, g, l) = await CreateAsync(); var roots = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var children = new ChildApplication(new IsolatedChildStore(db)); var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var person = root.PersonId; var placement = root.PlacementId;
        for (var i = 0; i < 5; i++)
        {
            var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, person, placement, RootNameMode.Named, "冯子" + i, Generation: i + 2, ExpectedParentGeneration: i + 1));
            person = child.PersonId; placement = child.PlacementId;
        }
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(roots, g, l, "冯", children, spouses); window.Show();
            try
            {
                await window.ReadyForTest; window.Width = 850; window.UpdateLayout();
                window.ScrollForTest.ScrollToHorizontalOffset(500); window.UpdateLayout();
                var last = window.CanvasForTest.Children.OfType<System.Windows.Controls.Border>().Single(x => x.Tag is ChildNode n && n.PersonId == person);
                Assert.True(window.CanvasForTest.MinWidth >= System.Windows.Controls.Canvas.GetLeft(last) + last.ActualWidth);
                Assert.True(window.ScrollForTest.ExtentWidth > window.ScrollForTest.ViewportWidth);
            }
            finally { window.Close(); }
        });
    }
    [Fact, Trait("Category", "UI")]
    public async Task Focused_spouse_fixed_cards_empty_node_and_child_preview_follow_layout()
    {
        var (db, g, l) = await CreateAsync();
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var children = new ChildApplication(new IsolatedChildStore(db));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", Generation: 2, ExpectedParentGeneration: 1));
        await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(roots, g, l, "冯", children, spouses); window.Show();
            try
            {
                await window.ReadyForTest; await window.SelectForTest(root.PersonId, root.PlacementId); window.UpdateLayout();
                var canvas = window.CanvasForTest;
                Assert.Single(canvas.Children.OfType<System.Windows.Controls.Border>(), x => x.Tag is SpouseNode);
                var blank = Assert.Single(canvas.Children.OfType<System.Windows.Controls.Border>(), x => x.Tag is ChildNode);
                Assert.Equal("子", Assert.IsType<System.Windows.Controls.TextBlock>(blank.Child).Text);
                Assert.Equal(110, blank.ActualWidth); Assert.Equal(34, blank.ActualHeight);
                var edge = Assert.Single(canvas.Children.OfType<System.Windows.Shapes.Polyline>());
                Assert.Contains(edge.Points, p => Math.Abs(p.X - System.Windows.Controls.Canvas.GetLeft(blank)) < 0.1 &&
                    Math.Abs(p.Y - (System.Windows.Controls.Canvas.GetTop(blank) + blank.ActualHeight / 2)) < 0.1);
                var oldTop = System.Windows.Controls.Canvas.GetTop(blank);
                window.BeginChildForTest(); window.UpdateLayout();
                Assert.Equal(2, canvas.Children.OfType<System.Windows.Shapes.Polyline>().Count());
                Assert.True(System.Windows.Controls.Canvas.GetTop(window.InputHostForTest!) > oldTop);
                Assert.Single(await children.ReadAsync(g, l));
                window.CancelForTest();
                Assert.Single(canvas.Children.OfType<System.Windows.Shapes.Polyline>());
                window.ClearFocusForTest();
                Assert.DoesNotContain(canvas.Children.OfType<System.Windows.Controls.Border>(), x => x.Tag is SpouseNode);
                await window.SelectForTest(child.PersonId, child.PlacementId);
                Assert.DoesNotContain(canvas.Children.OfType<System.Windows.Controls.Border>(), x => x.Tag is SpouseNode);
            }
            finally { window.Close(); }
        });
    }
    [Fact, Trait("Category", "UI")]
    public async Task Child_mother_selector_only_offers_current_fathers_spouses()
    {
        var (db, g, l) = await CreateAsync();
        var roots = new FirstRootApplication(new IsolatedFirstRootStore(db));
        var children = new ChildApplication(new IsolatedChildStore(db));
        var spouses = new SpouseApplication(new IsolatedSpouseStore(db));
        var root = await roots.ExecuteAsync(new(Guid.NewGuid(), g, l, RootNameMode.Named, "冯父", 1));
        var child = await children.ExecuteAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "冯子", Generation: 2, ExpectedParentGeneration: 1));
        var first = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "李氏"));
        var second = await spouses.AddAsync(new(Guid.NewGuid(), g, l, root.PersonId, root.PlacementId, RootNameMode.Named, "王氏"));
        var outsider = Guid.NewGuid();
        await using (var context = db.OpenContext()) { context.People.Add(new Person(outsider, l, "其他女性", PersonSex.Female)); await context.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new V1FirstRootWindow(roots, g, l, "冯", children, spouses); window.Show();
            try
            {
                await window.ReadyForTest; await window.SelectForTest(child.PersonId, child.PlacementId);
                Assert.Equal(new[] { first.PersonId, second.PersonId }, window.MotherCandidateIdsForTest);
                await window.ChooseMotherForTest(second.PersonId);
                Assert.Equal(second.PersonId, Assert.Single(await children.ReadAsync(g, l)).MotherId);
                await window.ChooseMotherForTest(null);
                Assert.True(Assert.Single(await children.ReadAsync(g, l)).MotherExplicitlyCleared);
            }
            finally { window.Close(); }
        });
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
                window.BeginForTest(); window.SetGenerationForTest("1"); window.SetInputForTest("朝阳");
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
            Task<IsolatedSample>? sample = null;
            Task<IsolatedSample> Sample() => sample ??= IsolatedSample.CreateAsync();
            var window = new WorkbenchProfileWindow(new TargetContextCoordinator(options)) { IsolatedSampleProvider = Sample }; window.Show();
            try
            {
                await window.InitializeForTest(); await window.OpenV1SampleForTest();
                var isolated = Assert.Single(window.OwnedWindows.OfType<V1FirstRootWindow>()); await isolated.ReadyForTest;
                isolated.BeginForTest(); isolated.SetInputForTest("草稿");
                Assert.False(await window.PrepareForOwnerExitAsync());
                isolated.CancelForTest(); isolated.BeginForTest(); isolated.SetGenerationForTest("1"); isolated.SetInputForTest("甲"); await isolated.ConfirmForTest(); var focus = isolated.FocusForTest;
                isolated.Close(); await window.OpenV1SampleForTest();
                var reopened = Assert.Single(window.OwnedWindows.OfType<V1FirstRootWindow>()); await reopened.ReadyForTest;
                Assert.Equal(1, reopened.NodeCountForTest); Assert.NotNull(focus.Placement); reopened.Close();
            }
            finally { if (await window.PrepareForOwnerExitAsync()) window.Close(); }
            var next = new WorkbenchProfileWindow(new TargetContextCoordinator(options)) { IsolatedSampleProvider = Sample }; next.Show();
            try
            {
                await next.InitializeForTest(); await next.OpenV1SampleForTest();
                var reopened = Assert.Single(next.OwnedWindows.OfType<V1FirstRootWindow>()); await reopened.ReadyForTest;
                Assert.Equal(1, reopened.NodeCountForTest); reopened.Close();
            }
            finally { if (await next.PrepareForOwnerExitAsync()) next.Close(); }
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
