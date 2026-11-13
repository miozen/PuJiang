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
    public async Task Old_sibling_record_without_shared_parent_is_diagnostic_not_canvas_kinship()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" };
        var a = new Person { GenealogyId = g.Id, Name = "甲", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var b = new Person { GenealogyId = g.Id, Name = "乙", Sex = PersonSex.Female, RecordedGeneration = "1" };
        var old = new PersonRelationship { GenealogyId = g.Id, PersonId = a.Id, RelatedPersonId = b.Id, Kind = PersonRelationshipKind.Sibling, Note = "保留" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(a, b); seed.PersonRelationships.Add(old); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            try
            {
                await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
                Assert.True(window.CanvasContainsForTest(a.Id)); Assert.True(window.CanvasContainsForTest(b.Id));
                Assert.Empty(window.CanvasSiblingsForTest(a.Id)); Assert.Empty(window.CanvasSiblingsForTest(b.Id));
                Assert.Contains(window.DiagnosticsForTest, d => d.Contains("原始兄妹记录") && d.Contains("待核实"));
                Assert.Equal(0, window.CanvasEdgesForTest);
            }
            finally { window.ChooseForTest = () => DraftLeaveChoice.Discard; if (await window.PrepareForOwnerExitAsync()) window.Close(); }
        });
        await using var verify = db.CreateContext(); Assert.Equal("保留", (await verify.PersonRelationships.SingleAsync(r => r.Id == old.Id)).Note);
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Changing_child_sex_keeps_chart_links_and_allows_same_sex_reordering()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var father = new Person { GenealogyId = g.Id, Name = "父亲", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart;
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.Add(father); await seed.SaveChangesAsync();
            var graph = await new GenealogyDataService(seed).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(seed).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        var children = new List<Guid>();
        foreach (var name in new[] { "长子", "次子", "三子" })
        {
            await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
            var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(g.Id, father.Id, chart));
            draft.Values = draft.Values with { Name = name }; children.Add(draft.Original.PersonId); await draft.SaveAsync(command);
        }
        Guid[] before; await using (var context = db.CreateContext()) before = await context.PersonRelationships.OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync();
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            try
            {
                await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectChartForTest(chart);
                Assert.Equal(3, window.CanvasEdgesForTest); await window.SelectCanvasForTest(children[1]);
                window.SetSexForTest(PersonSex.Female); await window.SaveForTest();
                Assert.Equal(3, window.CanvasEdgesForTest);
                Assert.True(Canvas.GetTop(window.CanvasCardForTest(children[0])) < Canvas.GetTop(window.CanvasCardForTest(children[2])));
                Assert.True(Canvas.GetTop(window.CanvasCardForTest(children[2])) < Canvas.GetTop(window.CanvasCardForTest(children[1])));
                await window.SelectCanvasForTest(children[2]); await window.MoveForTest(-1); await window.SaveChartOrderForTest();
                Assert.True(Canvas.GetTop(window.CanvasCardForTest(children[2])) < Canvas.GetTop(window.CanvasCardForTest(children[0])));
            }
            finally { window.ChooseForTest = () => DraftLeaveChoice.Discard; window.DiscardOrdersOnCloseForTest = () => true; if (await window.PrepareForOwnerExitAsync()) window.Close(); }
        });
        await using var verify = db.CreateContext(); Assert.Equal(before, await verify.PersonRelationships.OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Adding_mother_after_child_and_spouse_keeps_chart_links_and_relations()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var father = new Person { GenealogyId = g.Id, Name = "父亲", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart, child, mother;
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.Add(father); await seed.SaveChangesAsync();
            var graph = await new GenealogyDataService(seed).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(seed).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        await using (var context = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(context); var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(g.Id, father.Id, chart));
            draft.Values = draft.Values with { Name = "子女" }; child = draft.Original.PersonId; await draft.SaveAsync(command);
        }
        await using (var context = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(context); var draft = new WorkbenchPersonDraft(await command.BeginNewSpouseForChartAsync(g.Id, father.Id, chart));
            draft.Values = draft.Values with { Name = "母亲" }; mother = draft.Original.PersonId; await draft.SaveAsync(command);
        }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            try
            {
                await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectChartForTest(chart); await window.SelectCanvasForTest(father.Id);
                Assert.Equal(1, window.CanvasEdgesForTest); window.SetChildOtherParentForTest(child, mother); await window.SaveForTest();
                Assert.Equal(1, window.CanvasEdgesForTest); Assert.Single(window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>());
            }
            finally { window.ChooseForTest = () => DraftLeaveChoice.Discard; if (await window.PrepareForOwnerExitAsync()) window.Close(); }
        });
        await using var verify = db.CreateContext();
        Assert.True(await verify.PersonRelationships.AnyAsync(r => r.PersonId == child && r.RelatedPersonId == father.Id && r.Kind == PersonRelationshipKind.BiologicalParent));
        Assert.True(await verify.PersonRelationships.AnyAsync(r => r.PersonId == child && r.RelatedPersonId == mother && r.Kind == PersonRelationshipKind.BiologicalParent));
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Pending_chart_order_can_be_saved_from_another_person_or_explicitly_discarded_on_close()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var parent = new Person { GenealogyId = g.Id, Name = "父亲", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart;
        await using (var setup = db.CreateContext())
        {
            setup.Genealogies.Add(g); setup.People.Add(parent); await setup.SaveChangesAsync();
            var graph = await new GenealogyDataService(setup).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(setup).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        async Task<Guid> AddChild(string name)
        {
            await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
            var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(g.Id, parent.Id, chart));
            draft.Values = draft.Values with { Name = name }; await draft.SaveAsync(command); return draft.Original.PersonId;
        }
        var first = await AddChild("长子"); var second = await AddChild("次子");
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            try
            {
                await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectChartForTest(chart);
                await window.SelectCanvasForTest(second); await window.MoveForTest(-1);
                await window.SelectCanvasForTest(parent.Id); window.SetNameForTest("父亲草稿"); await window.SaveForTest();
                Assert.Contains("先保存或放弃", window.StatusForTest); Assert.True(window.SessionForTest.Draft!.IsDirty);
                await window.SaveChartOrderForTest(); Assert.Equal("已保存当前图表的同胞顺序。", window.StatusForTest);
                Assert.True(window.SessionForTest.Draft!.IsDirty); Assert.Equal("父亲草稿", window.SessionForTest.Draft.Values.Name);
                window.ChooseForTest = () => DraftLeaveChoice.Discard; await window.SelectCanvasForTest(first); await window.MoveForTest(-1);
                window.DiscardOrdersOnCloseForTest = () => false; Assert.False(await window.PrepareForOwnerExitAsync());
                Assert.Contains("顺序草稿已保留", window.StatusForTest);
                window.DiscardOrdersOnCloseForTest = () => true; Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
            }
            finally { window.DiscardOrdersOnCloseForTest = () => true; window.ChooseForTest = () => DraftLeaveChoice.Discard; if (await window.PrepareForOwnerExitAsync()) window.Close(); }
        });
        await using var verify = db.CreateContext();
        var group = await verify.LineageSiblingOrders.Include(x => x.Items).SingleAsync(x => x.ChartId == chart && x.ParentId == parent.Id);
        Assert.Equal(new[] { second, first }, group.Items.OrderBy(x => x.Position).Select(x => x.PersonId));
        Assert.Equal("父亲", (await verify.People.SingleAsync(p => p.Id == parent.Id)).Name);
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Changing_generation_moves_descendants_and_saves_their_generations()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var parent = new Person { GenealogyId = g.Id, Name = "父亲", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart, child;
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.Add(parent); await seed.SaveChangesAsync();
            var graph = await new GenealogyDataService(seed).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(seed).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
            var command = new WorkbenchPersonProfileCommand(seed); var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(g.Id, parent.Id, chart));
            draft.Values = draft.Values with { Name = "子女" }; child = draft.Original.PersonId; await draft.SaveAsync(command);
        }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest();
            await window.SelectGenealogyForTest(g.Id); await window.SelectChartForTest(chart); await window.SelectCanvasForTest(parent.Id);
            window.SetGenerationForTest("2");
            Assert.Equal(20 + 2 * 164, Canvas.GetLeft(window.CanvasCardForTest(child)));
            Assert.Single(window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>());
            await window.SaveForTest(); Assert.Contains("档案已保存", window.StatusForTest);
            Assert.Equal(3, window.CanvasColumnForTest(child));
            Assert.Single(window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>());
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
            var reopened = new WorkbenchProfileWindow(MakeCoordinator(db)); reopened.Show(); await reopened.InitializeForTest();
            await reopened.SelectGenealogyForTest(g.Id); await reopened.SelectChartForTest(chart);
            Assert.Equal(3, reopened.CanvasColumnForTest(child)); Assert.Single(reopened.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>());
            Assert.True(await reopened.PrepareForOwnerExitAsync()); reopened.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal("3", (await verify.People.SingleAsync(p => p.Id == child)).RecordedGeneration);
        Assert.Single(await verify.PersonRelationships.Where(r => r.PersonId == child && r.RelatedPersonId == parent.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Chart_inline_independent_person_saves_order_without_kinship_and_reopens()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var root = new Person { GenealogyId = g.Id, Name = "原人物", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart;
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.Add(root); await seed.SaveChangesAsync();
            var graph = await new GenealogyDataService(seed).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(seed).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        Guid added = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest();
            await window.SelectGenealogyForTest(g.Id); await window.SelectChartForTest(chart);
            await window.BeginInlineRootForTest(1); added = window.SessionForTest.Draft!.Original.PersonId;
            var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); editor.Input.Text = "新人物";
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await WaitUntil(() => window.SessionForTest.Draft is { Original.IsNew: false } && window.StatusForTest.Contains("档案已保存"));
            window.SetGenerationForTest("2"); window.SetGenerationForTest("1");
            Assert.True(Canvas.GetTop(window.CanvasCardForTest(root.Id)) < Canvas.GetTop(window.CanvasCardForTest(added)));
            Assert.True(Canvas.GetTop(window.CanvasCardForTest(root.Id)) < Canvas.GetTop(window.CanvasCardForTest(added)));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
            var reopened = new WorkbenchProfileWindow(MakeCoordinator(db)); reopened.Show(); await reopened.InitializeForTest();
            await reopened.SelectGenealogyForTest(g.Id); await reopened.SelectChartForTest(chart);
            Assert.True(Canvas.GetTop(reopened.CanvasCardForTest(root.Id)) < Canvas.GetTop(reopened.CanvasCardForTest(added)));
            Assert.True(await reopened.PrepareForOwnerExitAsync()); reopened.Close();
        });
        await using var verify = db.CreateContext(); Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Conflicting_new_root_sibling_generation_does_not_disturb_existing_child_lines()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var root = new Person { GenealogyId = g.Id, Name = "首节点", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart;
        await using (var setup = db.CreateContext())
        {
            setup.Genealogies.Add(g); setup.People.Add(root); await setup.SaveChangesAsync();
            var graph = await new GenealogyDataService(setup).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(setup).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        async Task<Guid> AddChild(string name)
        {
            await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
            var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(g.Id, root.Id, chart));
            draft.Values = draft.Values with { Name = name }; await draft.SaveAsync(command); return draft.Original.PersonId;
        }
        var first = await AddChild("长子"); var second = await AddChild("次子");
        Guid[] originalRelationships;
        await using (var before = db.CreateContext()) originalRelationships = await before.PersonRelationships.OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync();
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            try
            {
                await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectChartForTest(chart);
                var before = new[] { first, second }.Select(id => Canvas.GetTop(window.CanvasCardForTest(id))).ToArray();
                var lines = window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>().Count(); Assert.Equal(2, lines);
                await window.SelectCanvasForTest(root.Id);
                var card = window.CanvasCardForTest(root.Id);
                card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
                await WaitUntil(() => window.CanvasCardForTest(root.Id).ContextMenu is not null);
                window.CanvasCardForTest(root.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增同世人物")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
                var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); editor.Input.Text = "新兄妹";
                editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                var id = window.SessionForTest.Draft!.Original.PersonId; window.SetGenerationForTest("2");
                Assert.Equal(before, new[] { first, second }.Select(child => Canvas.GetTop(window.CanvasCardForTest(child))).ToArray());
                Assert.Equal(lines, window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>().Count());
                Assert.Contains(window.CanvasForTest.Children.OfType<Border>(), item => Equals(item.Tag, id));
                await window.SaveForTest(); Assert.Contains("档案已保存", window.StatusForTest);
                await using (var verify = db.CreateContext())
                {
                    Assert.Equal("2", (await verify.People.SingleAsync(p => p.Id == id)).RecordedGeneration);
                    Assert.Equal(originalRelationships, await verify.PersonRelationships.OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync());
                }
                window.SetGenerationForTest("1");
                Assert.Equal(lines, window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>().Count());
                await window.SaveForTest(); Assert.Contains("档案已保存", window.StatusForTest);
                await using (var saved = db.CreateContext()) Assert.Equal("1", (await saved.People.SingleAsync(p => p.Id == id)).RecordedGeneration);
                Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
                var reopened = new WorkbenchProfileWindow(MakeCoordinator(db)); reopened.Show(); await reopened.InitializeForTest();
                await reopened.SelectGenealogyForTest(g.Id); await reopened.SelectChartForTest(chart);
                Assert.Equal(lines, reopened.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>().Count());
                Assert.True(reopened.CanvasContainsForTest(id)); Assert.True(await reopened.PrepareForOwnerExitAsync()); reopened.Close();
            }
            finally { window.ChooseForTest = () => DraftLeaveChoice.Discard; if (await window.PrepareForOwnerExitAsync()) window.Close(); }
        });
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Root_sibling_chart_order_is_rendered_saved_and_reopened()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var elder = new Person { GenealogyId = g.Id, Name = "长兄", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart;
        await using (var setup = db.CreateContext())
        {
            setup.Genealogies.Add(g); setup.People.Add(elder); await setup.SaveChangesAsync();
            var graph = await new GenealogyDataService(setup).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(setup).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        async Task<Guid> Add(string name, PersonSex sex)
        {
            await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
            var draft = new WorkbenchPersonDraft(await command.BeginOrdinarySiblingForChartAsync(g.Id, elder.Id, chart));
            draft.Values = draft.Values with { Name = name, Sex = sex }; await draft.SaveAsync(command); return draft.Original.PersonId;
        }
        var sister = await Add("妹妹", PersonSex.Female); var younger = await Add("弟弟", PersonSex.Male);
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest();
            await window.SelectGenealogyForTest(g.Id); await window.SelectChartForTest(chart);
            Assert.True(Canvas.GetTop(window.CanvasCardForTest(elder.Id)) < Canvas.GetTop(window.CanvasCardForTest(younger)));
            Assert.True(Canvas.GetTop(window.CanvasCardForTest(younger)) < Canvas.GetTop(window.CanvasCardForTest(sister)));
            await window.SelectCanvasForTest(younger); await window.MoveForTest(-1); await window.SaveChartOrderForTest();
            Assert.Equal("已保存当前图表的同胞顺序。", window.StatusForTest);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
            var reopened = new WorkbenchProfileWindow(MakeCoordinator(db)); reopened.Show(); await reopened.InitializeForTest();
            await reopened.SelectGenealogyForTest(g.Id); await reopened.SelectChartForTest(chart);
            Assert.True(Canvas.GetTop(reopened.CanvasCardForTest(younger)) < Canvas.GetTop(reopened.CanvasCardForTest(elder.Id)));
            Assert.True(await reopened.PrepareForOwnerExitAsync()); reopened.Close();
        });
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Selected_chart_restores_saved_child_order_parent_lines_and_spouse_order()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "虚构谱" };
        var parent = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var a = new Person { GenealogyId = g.Id, Name = "配偶甲", Sex = PersonSex.Female }; var b = new Person { GenealogyId = g.Id, Name = "配偶乙", Sex = PersonSex.Female };
        var ua = new FamilyUnion { GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = a.Id, Kind = FamilyUnionKind.Marriage };
        var ub = new FamilyUnion { GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = b.Id, Kind = FamilyUnionKind.Marriage }; Guid chart;
        await using (var setup = db.CreateContext())
        {
            setup.Genealogies.Add(g); setup.People.AddRange(parent, a, b); setup.FamilyUnions.AddRange(ua, ub); await setup.SaveChangesAsync();
            var graph = await new GenealogyDataService(setup).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(setup).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
            var orders = new SpouseOrderStorageService(setup); var capture = await orders.CaptureAsync(g.Id, chart, parent.Id); await orders.SaveAsync(capture, new[] { ub.Id, ua.Id });
        }
        async Task<Guid> Child(string name, PersonSex sex)
        {
            await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
            var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(g.Id, parent.Id, chart));
            draft.Values = draft.Values with { Name = name, Sex = sex }; await draft.SaveAsync(command); return draft.Original.PersonId;
        }
        var girl = await Child("女儿", PersonSex.Female); var boy = await Child("儿子", PersonSex.Male); var youngerBoy = await Child("次子", PersonSex.Male);
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest();
            await window.SelectGenealogyForTest(g.Id); await window.SelectChartForTest(chart);
            Assert.Equal(3, window.CanvasEdgesForTest);
            Assert.True(Canvas.GetTop(window.CanvasCardForTest(boy)) < Canvas.GetTop(window.CanvasCardForTest(girl)));
            Assert.True(Canvas.GetTop(window.CanvasCardForTest(boy)) < Canvas.GetTop(window.CanvasCardForTest(youngerBoy)));
            Assert.Equal(new[] { "婚配：配偶乙", "婚配：配偶甲" }, window.CanvasPartnersForTest(parent.Id));
            var childPositions = new[] { boy, youngerBoy, girl }.Select(id => Canvas.GetTop(window.CanvasCardForTest(id))).ToArray();
            await window.SelectCanvasForTest(parent.Id);
            Assert.Equal(childPositions, new[] { boy, youngerBoy, girl }.Select(id => Canvas.GetTop(window.CanvasCardForTest(id))).ToArray());
            await window.MoveSpouseToForTest(parent.Id, ua.Id, ub.Id);
            Assert.Equal("已保存当前图表的配偶顺序。", window.StatusForTest);
            Assert.Equal(new[] { "婚配：配偶甲", "婚配：配偶乙" }, window.CanvasPartnersForTest(parent.Id));
            var card = window.CanvasCardForTest(parent.Id);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(parent.Id).ContextMenu is not null);
            window.CanvasCardForTest(parent.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增子女（生物关系）")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            Assert.Equal(a.Id, window.SessionForTest.Draft!.Original.SecondParentId);
            var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single();
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await WaitUntil(() => window.SessionForTest.Draft?.Original.PersonId == parent.Id);
            await window.SelectCanvasForTest(youngerBoy); await window.MoveForTest(-1); await window.SaveChartOrderForTest();
            Assert.Contains("已保存", window.StatusForTest);
            Assert.True(Canvas.GetTop(window.CanvasCardForTest(youngerBoy)) < Canvas.GetTop(window.CanvasCardForTest(boy)));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
            var reopened = new WorkbenchProfileWindow(MakeCoordinator(db)); reopened.Show(); await reopened.InitializeForTest();
            await reopened.SelectGenealogyForTest(g.Id); await reopened.SelectChartForTest(chart);
            Assert.Equal(3, reopened.CanvasEdgesForTest);
            Assert.Equal(new[] { "婚配：配偶甲", "婚配：配偶乙" }, reopened.CanvasPartnersForTest(parent.Id));
            Assert.True(Canvas.GetTop(reopened.CanvasCardForTest(youngerBoy)) < Canvas.GetTop(reopened.CanvasCardForTest(boy)));
            Assert.True(await reopened.PrepareForOwnerExitAsync()); reopened.Close();
        });
    }

    [Theory]
    [InlineData(1.0)] [InlineData(1.5)]
    [Trait("Category", "UI")]
    public async Task Switching_selection_keeps_target_in_place_when_previous_spouses_collapse(double zoom)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "虚构谱" };
        var people = Enumerable.Range(1, 10).Select(i => new Person { Id = Guid.Parse($"{i:D8}-0000-0000-0000-000000000000"), GenealogyId = g.Id, Name = $"人物{i}", Sex = PersonSex.Male, RecordedGeneration = "1" }).ToArray();
        var spouses = Enumerable.Range(1, 3).Select(i => new Person { GenealogyId = g.Id, Name = $"配偶{i}", Sex = PersonSex.Female }).ToArray();
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(people.Concat(spouses)); seed.FamilyUnions.AddRange(spouses.Select(p => new FamilyUnion { GenealogyId = g.Id, PartnerOneId = people[0].Id, PartnerTwoId = p.Id, Kind = FamilyUnionKind.Marriage })); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            var scale = Assert.IsType<System.Windows.Media.ScaleTransform>(window.CanvasForTest.LayoutTransform); scale.ScaleX = scale.ScaleY = zoom;
            await window.SelectCanvasForTest(people[0].Id); window.CanvasScrollForTest.UpdateLayout();
            window.CanvasScrollForTest.ScrollToVerticalOffset(300); window.CanvasScrollForTest.UpdateLayout();
            var before = Canvas.GetTop(window.CanvasCardForTest(people[3].Id)) * zoom - window.CanvasScrollForTest.VerticalOffset;
            await window.SelectCanvasForTest(people[3].Id); window.CanvasScrollForTest.UpdateLayout();
            var after = Canvas.GetTop(window.CanvasCardForTest(people[3].Id)) * zoom - window.CanvasScrollForTest.VerticalOffset;
            Assert.Equal(before, after, 3);
            await window.ClearCanvasSelectionForTest(); window.CanvasScrollForTest.UpdateLayout();
            Assert.Equal(after, Canvas.GetTop(window.CanvasCardForTest(people[3].Id)) * zoom - window.CanvasScrollForTest.VerticalOffset, 3);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Expanded_spouses_reserve_only_their_row_and_restore_base_spacing_when_deselected()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "虚构谱" };
        var a = new Person { Id = Guid.Parse("10000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, Name = "甲", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var b = new Person { Id = Guid.Parse("20000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, Name = "乙", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var c = new Person { Id = Guid.Parse("30000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, Name = "丙", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var spouses = Enumerable.Range(1, 3).Select(i => new Person { GenealogyId = g.Id, Name = $"配偶{i}", Sex = PersonSex.Female }).ToArray();
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(spouses.Append(a).Append(b).Append(c)); seed.FamilyUnions.AddRange(spouses.Select(p => new FamilyUnion { GenealogyId = g.Id, PartnerOneId = a.Id, PartnerTwoId = p.Id, Kind = FamilyUnionKind.Marriage })); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            double Top(Guid id) => Canvas.GetTop(window.CanvasCardForTest(id));
            var baseline = Top(b.Id) - Top(a.Id); Assert.Equal(76, baseline); Assert.Equal(baseline, Top(c.Id) - Top(b.Id));
            var originalTop = Top(a.Id);
            window.CanvasScrollForTest.UpdateLayout();
            var screenBefore = window.CanvasCardForTest(a.Id).TranslatePoint(new Point(0, 0), window.CanvasScrollForTest).Y;
            await window.SelectCanvasForTest(a.Id);
            window.CanvasScrollForTest.UpdateLayout();
            Assert.Equal(screenBefore, window.CanvasCardForTest(a.Id).TranslatePoint(new Point(0, 0), window.CanvasScrollForTest).Y, 2);
            Assert.Equal(originalTop, Top(a.Id));
            Assert.Equal(baseline, Top(c.Id) - Top(b.Id)); Assert.True(Top(b.Id) - Top(a.Id) > baseline);
            var cards = spouses.Select(p => window.CanvasCardForTest(p.Id)).ToArray();
            Assert.All(cards, card => Assert.True(Canvas.GetTop(card) + card.Height <= Top(b.Id)));
            await window.ClearCanvasSelectionForTest();
            window.CanvasScrollForTest.UpdateLayout();
            Assert.Equal(screenBefore, window.CanvasCardForTest(a.Id).TranslatePoint(new Point(0, 0), window.CanvasScrollForTest).Y, 2);
            Assert.Equal(originalTop, Top(a.Id));
            Assert.Equal(baseline, Top(b.Id) - Top(a.Id)); Assert.Equal(baseline, Top(c.Id) - Top(b.Id));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Combined_union_and_child_draft_survives_cancel_and_reload()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" };
        var father = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var a = new Person { GenealogyId = g.Id, Name = "甲", Sex = PersonSex.Female };
        var b = new Person { GenealogyId = g.Id, Name = "乙", Sex = PersonSex.Female };
        var child = new Person { GenealogyId = g.Id, Name = "子", RecordedGeneration = "2" };
        var union = new FamilyUnion { Id = Guid.Parse("10000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = father.Id, PartnerTwoId = a.Id, Kind = FamilyUnionKind.Marriage };
        var otherUnion = new FamilyUnion { Id = Guid.Parse("20000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = father.Id, PartnerTwoId = b.Id, Kind = FamilyUnionKind.Marriage };
        var motherLink = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = a.Id, Kind = PersonRelationshipKind.BiologicalParent };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(father, a, b, child); seed.FamilyUnions.AddRange(union, otherUnion); seed.PersonRelationships.AddRange(new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = father.Id, Kind = PersonRelationshipKind.BiologicalParent }, motherLink); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest();
            await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(father.Id);
            window.SelectUnionForTest(0); window.SetUnionForTest(FamilyUnionKind.Marriage, "开始", "", "新备注");
            window.SetChildOtherParentForTest(child.Id, b.Id); window.SetNameForTest("父改名");
            window.ChooseForTest = () => DraftLeaveChoice.Cancel; await window.SelectCanvasForTest(child.Id);
            Assert.Equal(father.Id, window.SessionForTest.Draft!.Original.PersonId); Assert.True(window.SessionForTest.Draft.IsDirty);
            Assert.Equal(b.Id, window.ChildOtherParentForTest(child.Id));
            await using (var before = db.CreateContext()) { Assert.Equal("父", (await before.People.SingleAsync(p => p.Id == father.Id)).Name); Assert.Equal(a.Id, (await before.PersonRelationships.SingleAsync(r => r.Id == motherLink.Id)).RelatedPersonId); }
            await window.SaveForTest(); Assert.False(window.SessionForTest.Draft!.IsDirty);
            await window.SelectCanvasForTest(child.Id); Assert.Contains("生母：乙", window.DraftParentsForTest);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext();
        Assert.Equal("父改名", (await verify.People.SingleAsync(p => p.Id == father.Id)).Name);
        Assert.Equal("新备注", (await verify.FamilyUnions.SingleAsync(u => u.Id == union.Id)).Note);
        Assert.Equal(b.Id, (await verify.PersonRelationships.SingleAsync(r => r.Id == motherLink.Id)).RelatedPersonId);
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Child_list_parent_correction_respects_draft_and_keeps_selected_parent()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var parent = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var a = new Person { GenealogyId = g.Id, Name = "甲", Sex = PersonSex.Female }; var b = new Person { GenealogyId = g.Id, Name = "乙", Sex = PersonSex.Female };
        var child = new Person { GenealogyId = g.Id, Name = "子女", RecordedGeneration = "2" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(parent, a, b, child); seed.FamilyUnions.AddRange(new FamilyUnion { GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = a.Id, Kind = FamilyUnionKind.Marriage }, new FamilyUnion { GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = b.Id, Kind = FamilyUnionKind.Marriage }); seed.PersonRelationships.AddRange(new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent }, new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = a.Id, Kind = PersonRelationshipKind.BiologicalParent }); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(parent.Id);
            Assert.Equal(1, window.ChildCountForTest); Assert.Equal(a.Id, window.ChildOtherParentForTest(child.Id));
            window.SetChildOtherParentForTest(child.Id, b.Id); Assert.True(window.SessionForTest.Draft!.IsDirty); Assert.Equal(b.Id, window.ChildOtherParentForTest(child.Id));
            await using (var before = db.CreateContext()) Assert.True(await before.PersonRelationships.AnyAsync(r => r.PersonId == child.Id && r.RelatedPersonId == a.Id));
            window.ChooseForTest = () => DraftLeaveChoice.Cancel; await window.SelectCanvasForTest(child.Id);
            Assert.Equal(parent.Id, window.SessionForTest.Draft!.Original.PersonId); Assert.Equal(b.Id, window.ChildOtherParentForTest(child.Id));
            await window.SaveForTest(); Assert.Equal(parent.Id, window.SessionForTest.Draft!.Original.PersonId); Assert.Equal(b.Id, window.ChildOtherParentForTest(child.Id));
            window.SetChildOtherParentForTest(child.Id, null); window.ChooseForTest = () => DraftLeaveChoice.Discard; await window.SelectCanvasForTest(child.Id);
            Assert.Equal(child.Id, window.SessionForTest.Draft!.Original.PersonId); Assert.Contains("生母：乙", window.DraftParentsForTest);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.True(await verify.PersonRelationships.AnyAsync(r => r.PersonId == child.Id && r.RelatedPersonId == b.Id)); Assert.False(await verify.PersonRelationships.AnyAsync(r => r.PersonId == child.Id && r.RelatedPersonId == a.Id));
    }
    [Theory]
    [InlineData(PersonSex.Male, PersonSex.Female, 0)] [InlineData(PersonSex.Male, PersonSex.Female, 1)] [InlineData(PersonSex.Male, PersonSex.Female, 2)]
    [InlineData(PersonSex.Female, PersonSex.Male, 0)] [InlineData(PersonSex.Female, PersonSex.Male, 1)] [InlineData(PersonSex.Female, PersonSex.Male, 2)]
    [Trait("Category", "UI")]
    public async Task Ordinary_child_default_parent_is_visible_before_and_after_save(PersonSex sex, PersonSex spouseSex, int count)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var parent = new Person { GenealogyId = g.Id, Name = "所选父母", Sex = sex, RecordedGeneration = "1" };
        var first = new Person { GenealogyId = g.Id, Name = "配偶甲", Sex = spouseSex }; var second = new Person { GenealogyId = g.Id, Name = "配偶乙", Sex = spouseSex };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(parent, first, second);
            if (count > 1) seed.FamilyUnions.Add(new FamilyUnion { Id = Guid.Parse("20000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = second.Id, Kind = FamilyUnionKind.Marriage });
            if (count > 0) seed.FamilyUnions.Add(new FamilyUnion { Id = Guid.Parse("10000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = first.Id, Kind = FamilyUnionKind.Marriage });
            await seed.SaveChangesAsync();
        }
        Guid childId = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(parent.Id);
            var card = window.CanvasCardForTest(parent.Id);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(parent.Id).ContextMenu is not null);
            window.CanvasCardForTest(parent.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增子女（生物关系）")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); childId = window.SessionForTest.Draft!.Original.PersonId; editor.Input.Text = "新子女";
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            var otherRole = sex == PersonSex.Male ? "生母" : "生父";
            Assert.Contains($"{otherRole}：{(count == 0 ? "未确定" : "配偶甲")}", window.DraftParentsForTest);
            await window.SaveForTest(); Assert.Equal(childId, window.SessionForTest.Draft!.Original.PersonId);
            Assert.Contains($"{otherRole}：{(count == 0 ? "未确定" : "配偶甲")}", window.DraftParentsForTest);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal(count == 0 ? 1 : 2, await verify.PersonRelationships.CountAsync(r => r.PersonId == childId));
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Incomplete_marriage_page_blocks_child_creation_without_guessing()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var parent = new Person { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var spouses = Enumerable.Range(0, 101).Select(i => new Person { GenealogyId = g.Id, Name = $"配偶{i}", Sex = PersonSex.Female }).ToArray();
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(parent); seed.People.AddRange(spouses); seed.FamilyUnions.AddRange(spouses.Select(p => new FamilyUnion { GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = p.Id, Kind = FamilyUnionKind.Marriage })); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(parent.Id);
            var card = window.CanvasCardForTest(parent.Id); card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(parent.Id).ContextMenu is not null);
            window.CanvasCardForTest(parent.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增子女（生物关系）")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.StatusForTest.Contains("未完整加载"));
            Assert.Equal(parent.Id, window.SessionForTest.Draft!.Original.PersonId); Assert.Empty(window.CanvasForTest.Children.OfType<InlineNameEditor>());
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal(102, await verify.People.CountAsync()); Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Union_profiles_remain_in_person_draft_until_save_and_support_selection()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var p = new Person { GenealogyId = g.Id, Name = "人物", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var spouse = new Person { GenealogyId = g.Id, Name = "配偶", Sex = PersonSex.Female };
        var union = new FamilyUnion { Id = Guid.Parse("10000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = p.Id, PartnerTwoId = spouse.Id, Kind = FamilyUnionKind.Marriage, Note = "原备注" };
        var unknown = new FamilyUnion { Id = Guid.Parse("20000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = p.Id, Kind = FamilyUnionKind.Unknown };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(p, spouse); seed.FamilyUnions.AddRange(union, unknown); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(p.Id);
            Assert.Equal(2, window.UnionCountForTest); Assert.True(window.UnionLocateEnabledForTest);
            window.SetUnionForTest(FamilyUnionKind.Marriage, "1900", "", "修改备注"); Assert.True(window.SessionForTest.Draft!.IsDirty);
            window.SelectUnionForTest(1); Assert.False(window.UnionLocateEnabledForTest); window.SetUnionForTest(FamilyUnionKind.Unknown, "", "", "未详备注");
            window.SelectUnionForTest(0); Assert.Equal("修改备注", window.UnionNoteForTest);
            await using (var before = db.CreateContext()) Assert.Equal("原备注", (await before.FamilyUnions.SingleAsync(u => u.Id == union.Id)).Note);
            window.ChooseForTest = () => DraftLeaveChoice.Cancel; window.LocateUnionForTest(); await WaitUntil(() => window.StatusForTest.Contains("保留当前草稿"));
            Assert.Equal(p.Id, window.SessionForTest.Draft!.Original.PersonId); Assert.True(window.SessionForTest.Draft.IsDirty);
            Assert.Equal(union.Id, window.SelectedUnionForTest);
            window.SelectUnionForTest(1); Assert.Equal(unknown.Id, window.SelectedUnionForTest);
            await window.SaveForTest(); Assert.False(window.SessionForTest.Draft!.IsDirty); Assert.Equal(p.Id, window.SessionForTest.Draft.Original.PersonId);
            Assert.Equal(unknown.Id, window.SelectedUnionForTest); Assert.Equal("未详备注", window.UnionNoteForTest);
            window.SelectUnionForTest(0);
            window.LocateUnionForTest(); await WaitUntil(() => window.SessionForTest.Draft?.Original.PersonId == spouse.Id);
            window.SetNameForTest("配偶甲改名"); await window.SaveForTest();
            Assert.Equal(spouse.Id, window.SessionForTest.Draft!.Original.PersonId);
            Assert.Equal(System.Windows.Media.Brushes.DarkOrange, window.CanvasCardForTest(spouse.Id).Background);
            Assert.DoesNotContain(window.CanvasForTest.Children.OfType<Border>(), b => Equals(b.ToolTip, "类型未详：未详") && b.Background == System.Windows.Media.Brushes.DarkOrange);
            await window.ClearCanvasSelectionForTest(); Assert.Null(window.SessionForTest.Draft);
            Assert.DoesNotContain(window.CanvasForTest.Children.OfType<Border>(), b => Equals(b.ToolTip, "类型未详：未详") && b.Background == System.Windows.Media.Brushes.DarkOrange);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal("修改备注", (await verify.FamilyUnions.SingleAsync(u => u.Id == union.Id)).Note);
        Assert.Equal("未详备注", (await verify.FamilyUnions.SingleAsync(u => u.Id == unknown.Id)).Note);
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Inline_spouse_save_failure_keeps_the_draft_and_rolls_back_then_can_retry()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var person = new Person { GenealogyId = g.Id, Name = "人物", Sex = PersonSex.Male, RecordedGeneration = "1" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(person); await seed.SaveChangesAsync(); await seed.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectInlineUnion BEFORE INSERT ON FamilyUnions BEGIN SELECT RAISE(ABORT, 'injected failure'); END"); }
        Guid spouseId = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(person.Id);
            var card = window.CanvasCardForTest(person.Id);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(person.Id).ContextMenu is not null);
            window.CanvasCardForTest(person.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增配偶（婚配）")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); editor.Input.Text = "配偶";
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            spouseId = window.SessionForTest.Draft!.Original.PersonId;
            await window.SaveForTest(); Assert.Contains("未保存", window.StatusForTest); Assert.True(window.SessionForTest.Draft!.IsDirty);
            await using (var verify = db.CreateContext()) { Assert.Single(await verify.People.ToListAsync()); Assert.Empty(await verify.FamilyUnions.ToListAsync()); await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectInlineUnion"); }
            await window.SaveForTest(); Assert.Equal(spouseId, window.SessionForTest.Draft!.Original.PersonId); Assert.False(window.SessionForTest.Draft.IsDirty);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var final = db.CreateContext(); Assert.Equal(2, await final.People.CountAsync()); Assert.Single(await final.FamilyUnions.ToListAsync());
    }
    [Theory]
    [InlineData(PersonSex.Male, PersonSex.Female)]
    [InlineData(PersonSex.Female, PersonSex.Male)]
    [InlineData(PersonSex.Unknown, PersonSex.Unknown)]
    [Trait("Category", "UI")]
    public async Task Inline_spouse_cancel_confirm_and_save_preserve_selection_and_existing_family(PersonSex currentSex, PersonSex expectedSex)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var current = new Person { GenealogyId = g.Id, Name = "人物", Sex = currentSex, RecordedGeneration = "1" };
        var prior = new Person { GenealogyId = g.Id, Name = "已有配偶", Sex = PersonSex.Female };
        var child = new Person { GenealogyId = g.Id, Name = "子女", RecordedGeneration = "2" };
        var union = new FamilyUnion { GenealogyId = g.Id, PartnerOneId = current.Id, PartnerTwoId = prior.Id, Kind = FamilyUnionKind.Marriage };
        var parentRelation = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = current.Id, Kind = PersonRelationshipKind.BiologicalParent };
        var otherRelation = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = prior.Id, Kind = PersonRelationshipKind.BiologicalParent };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(current, prior, child); seed.FamilyUnions.Add(union); seed.PersonRelationships.AddRange(parentRelation, otherRelation); await seed.SaveChangesAsync(); }
        Guid createdId = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(current.Id);
            async Task<InlineNameEditor> Start()
            {
                var card = window.CanvasCardForTest(current.Id);
                card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
                await WaitUntil(() => window.CanvasCardForTest(current.Id).ContextMenu is not null);
                var menu = window.CanvasCardForTest(current.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增配偶（婚配）"));
                menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
                return window.CanvasForTest.Children.OfType<InlineNameEditor>().Single();
            }
            void Key(InlineNameEditor editor, Key key) => editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            var editor = await Start();
            Assert.Equal(Canvas.GetLeft(window.CanvasCardForTest(current.Id)), Canvas.GetLeft(editor));
            Assert.True(Canvas.GetTop(editor) >= Canvas.GetTop(window.CanvasCardForTest(current.Id)) + window.CanvasCardForTest(current.Id).Height);
            Assert.Equal(expectedSex, window.SessionForTest.Draft!.Values.Sex);
            editor.Input.Text = " "; Key(editor, System.Windows.Input.Key.Enter); Assert.Same(editor, window.CanvasForTest.Children.OfType<InlineNameEditor>().Single());
            editor.Input.Text = "取消"; Key(editor, System.Windows.Input.Key.Escape);
            await WaitUntil(() => window.SessionForTest.Draft?.Original.PersonId == current.Id);
            await using (var verify = db.CreateContext()) { Assert.Equal(3, await verify.People.CountAsync()); Assert.Single(await verify.FamilyUnions.ToListAsync()); }
            editor = await Start(); createdId = window.SessionForTest.Draft!.Original.PersonId;
            editor.Input.Text = "新配偶";
            var canvas = window.CanvasForTest;
            canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            await WaitUntil(() => window.SessionForTest.Draft is { Original.IsNew: false } && window.SessionForTest.Draft.Original.PersonId == createdId);
            Assert.Equal(createdId, window.SessionForTest.Draft!.Original.PersonId); Assert.Equal("新配偶", window.SessionForTest.Draft.Values.Name);
            var spouseCard = window.CanvasForTest.Children.OfType<Border>().Single(b => Equals(b.Tag, createdId));
            Assert.Contains("新配偶", Assert.IsType<TextBlock>(spouseCard.Child).Text);
            Assert.Equal(System.Windows.Media.Brushes.DarkOrange, spouseCard.Background);
            window.SetNameForTest("配偶草稿"); Assert.EndsWith("配偶草稿", Assert.IsType<TextBlock>(window.CanvasCardForTest(createdId).Child).Text);
            window.SetNameForTest("新配偶");
            await using (var verify = db.CreateContext()) { Assert.Equal(4, await verify.People.CountAsync()); Assert.Equal(2, await verify.FamilyUnions.CountAsync()); }
            await window.SaveForTest(); Assert.Equal(createdId, window.SessionForTest.Draft!.Original.PersonId);
            Assert.Contains("婚配：已有配偶", window.CanvasPartnersForTest(current.Id)); Assert.Contains("婚配：新配偶", window.CanvasPartnersForTest(current.Id));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var final = db.CreateContext(); Assert.Equal(4, await final.People.CountAsync()); Assert.Equal(2, await final.FamilyUnions.CountAsync());
        Assert.Equal(expectedSex, (await final.People.SingleAsync(p => p.Id == createdId)).Sex);
        var parents = await final.PersonRelationships.Where(r => r.PersonId == child.Id).ToListAsync();
        Assert.Equal(2, parents.Count); Assert.Contains(parents, r => r.Id == parentRelation.Id && r.RelatedPersonId == current.Id);
        Assert.Contains(parents, r => r.Id == otherRelation.Id && r.RelatedPersonId == prior.Id);
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Person_cards_keep_the_same_size_and_many_spouses_are_visible_with_links()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" };
        var person = new Person { GenealogyId = g.Id, Name = "多配偶人物", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var other = new Person { GenealogyId = g.Id, Name = "另一个人物", RecordedGeneration = "1" };
        var child = new Person { GenealogyId = g.Id, Name = "子女", RecordedGeneration = "2" };
        var spouses = Enumerable.Range(1, 8).Select(i => new Person { GenealogyId = g.Id, Name = $"配偶{i}", Sex = PersonSex.Female }).ToArray();
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(spouses.Append(person).Append(other).Append(child));
            seed.FamilyUnions.AddRange(spouses.Select(s => new FamilyUnion { GenealogyId = g.Id, PartnerOneId = person.Id, PartnerTwoId = s.Id, Kind = FamilyUnionKind.Marriage }));
            seed.PersonRelationships.Add(new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = person.Id, Kind = PersonRelationshipKind.BiologicalParent });
            await seed.SaveChangesAsync();
        }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(person.Id);
            window.UpdateLayout();
            var populated = window.CanvasCardForTest(person.Id); var empty = window.CanvasCardForTest(other.Id);
            Assert.Equal(empty.Width, populated.Width); Assert.Equal(empty.Height, populated.Height);
            var cards = spouses.Select(s => window.CanvasForTest.Children.OfType<Border>().Single(b => Equals(b.Tag, s.Id))).OrderBy(Canvas.GetTop).ToArray();
            Assert.Equal(spouses.Length, cards.Length);
            Assert.All(cards, card => { Assert.Equal(populated.Width, card.Width); Assert.Equal(populated.Height, card.Height); Assert.StartsWith("♀", Assert.IsType<TextBlock>(card.Child).Text); });
            Assert.StartsWith("♂", Assert.IsType<TextBlock>(populated.Child).Text);
            Assert.True(window.CanvasForTest.Children.OfType<System.Windows.Shapes.Line>().Count() >= spouses.Length);
            Assert.Equal(1, window.CanvasEdgesForTest);
            Assert.Single(window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>());
            Assert.True(cards.Zip(cards.Skip(1)).All(pair => Canvas.GetTop(pair.First) + pair.First.Height <= Canvas.GetTop(pair.Second)));
            Assert.All(cards, card => Assert.True(Canvas.GetTop(card) + card.Height <= Canvas.GetTop(empty) ||
                Canvas.GetTop(empty) + empty.Height <= Canvas.GetTop(card)));
            Assert.IsType<TextBlock>(empty.Child);
            Assert.True(Canvas.GetTop(cards[0]) >= Canvas.GetTop(populated) + populated.Height);
            Assert.NotEqual(empty.Background, populated.Background);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
    }
    [Theory]
    [InlineData(PersonSex.Male, PersonSex.Female)]
    [InlineData(PersonSex.Female, PersonSex.Male)]
    [Trait("Category", "UI")]
    public async Task Ordinary_child_inline_cancel_and_save_keep_the_selected_parent_context(PersonSex parentSex, PersonSex spouseSex)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var parent = new Person { GenealogyId = g.Id, Name = "所选父母", Sex = parentSex, RecordedGeneration = "1" };
        var first = new Person { GenealogyId = g.Id, Name = "配偶甲", Sex = spouseSex }; var second = new Person { GenealogyId = g.Id, Name = "配偶乙", Sex = spouseSex };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(parent, first, second);
            seed.FamilyUnions.AddRange(
                new FamilyUnion { Id = Guid.Parse("20000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = second.Id, Kind = FamilyUnionKind.Marriage },
                new FamilyUnion { Id = Guid.Parse("10000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = first.Id, Kind = FamilyUnionKind.Marriage });
            await seed.SaveChangesAsync();
        }
        Guid childId = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(parent.Id);
            Assert.Equal(new[] { "婚配：配偶甲", "婚配：配偶乙" }, window.CanvasPartnersForTest(parent.Id));
            async Task<InlineNameEditor> Start()
            {
                var card = window.CanvasCardForTest(parent.Id);
                card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
                await WaitUntil(() => window.CanvasCardForTest(parent.Id).ContextMenu is not null);
                var menu = window.CanvasCardForTest(parent.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增子女（生物关系）"));
                menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
                return window.CanvasForTest.Children.OfType<InlineNameEditor>().Single();
            }
            void Key(InlineNameEditor editor, Key key) => editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            void ClickBlank()
            {
                var canvas = window.CanvasForTest;
                canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            }
            var editor = await Start(); editor.Input.Text = " "; Key(editor, System.Windows.Input.Key.Enter);
            Assert.Same(editor, window.CanvasForTest.Children.OfType<InlineNameEditor>().Single());
            ClickBlank(); Assert.Same(editor, window.CanvasForTest.Children.OfType<InlineNameEditor>().Single());
            editor.Input.Text = "取消"; Key(editor, System.Windows.Input.Key.Escape);
            await WaitUntil(() => window.SessionForTest.Draft?.Original.PersonId == parent.Id);
            await using (var verify = db.CreateContext()) { Assert.Equal(3, await verify.People.CountAsync()); Assert.Empty(await verify.PersonRelationships.ToListAsync()); }
            editor = await Start(); childId = window.SessionForTest.Draft!.Original.PersonId;
            Assert.Equal(first.Id, window.SessionForTest.Draft.Original.SecondParentId);
            Assert.True(Canvas.GetLeft(editor) > Canvas.GetLeft(window.CanvasCardForTest(parent.Id)));
            var previewLine = Assert.Single(window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>());
            var parentCard = window.CanvasCardForTest(parent.Id);
            Assert.Equal(Canvas.GetLeft(parentCard) + parentCard.Width, previewLine.Points[0].X);
            Assert.Equal(Canvas.GetTop(parentCard) + parentCard.Height / 2, previewLine.Points[0].Y);
            editor.Input.Text = "新子女"; Key(editor, System.Windows.Input.Key.Enter);
            await WaitUntil(() => window.SessionForTest.Draft is { Original.IsNew: false } && window.SessionForTest.Draft.Original.PersonId == childId);
            Assert.Equal("新子女", window.SessionForTest.Draft!.Values.Name); Assert.NotNull(window.CanvasCardForTest(childId));
            window.SetNameForTest("子女草稿"); Assert.EndsWith("子女草稿", Assert.IsType<TextBlock>(window.CanvasCardForTest(childId).Child).Text);
            window.SetSexForTest(PersonSex.Female); Assert.StartsWith("♀", Assert.IsType<TextBlock>(window.CanvasCardForTest(childId).Child).Text);
            window.SetNameForTest("新子女"); window.SetSexForTest(PersonSex.Male);
            window.SetGenerationForTest("3"); Assert.Equal(20 + 2 * 164, Canvas.GetLeft(window.CanvasCardForTest(childId)));
            window.SetGenerationForTest("2");
            Assert.Contains(parentSex == PersonSex.Male ? "生父：所选父母" : "生母：所选父母", window.DraftParentsForTest);
            Assert.Contains(parentSex == PersonSex.Male ? "生母：配偶甲" : "生父：配偶甲", window.DraftParentsForTest);
            await using (var verify = db.CreateContext()) Assert.Equal(4, await verify.People.CountAsync());
            await window.SaveForTest(); Assert.True(window.CanvasContainsForTest(childId)); Assert.Equal(1, window.CanvasEdgesForTest);
            Assert.Contains(parentSex == PersonSex.Male ? "生父：所选父母" : "生母：所选父母", window.DraftParentsForTest);
            Assert.Contains(parentSex == PersonSex.Male ? "生母：配偶甲" : "生父：配偶甲", window.DraftParentsForTest);
            editor = await Start();
            Assert.Equal(2, window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>().Count());
            var secondChild = window.SessionForTest.Draft!.Original.PersonId;
            editor.Input.Text = "第二位子女"; ClickBlank();
            await WaitUntil(() => window.SessionForTest.Draft is { Original.IsNew: false } && window.SessionForTest.Draft.Original.PersonId == secondChild);
            Assert.Equal("第二位子女", window.SessionForTest.Draft.Values.Name); Assert.NotNull(window.CanvasCardForTest(secondChild));
            Assert.Equal(2, window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>().Count());
            Assert.Equal(window.CanvasCardForTest(parent.Id).Width, window.CanvasCardForTest(secondChild).Width);
            Assert.Equal(window.CanvasCardForTest(parent.Id).Height, window.CanvasCardForTest(secondChild).Height);
            await window.SaveForTest(); Assert.Equal(2, window.CanvasEdgesForTest);
            Assert.Contains(window.DiagnosticsForTest, message => message.Contains("临时显示顺序") && message.Contains("不代表谱载排行"));
            Assert.Contains("不代表谱载排行", window.StatusForTest);
            await using (var changed = db.CreateContext())
                await changed.People.Where(p => p.Id == first.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Biography, "外部变更"));
            await window.PollCanvasForTest(); Assert.False(window.CanvasContainsForTest(childId));
            await window.ReopenCanvasForTest();
            Assert.True(window.CanvasContainsForTest(childId)); Assert.Equal(0, window.CanvasEdgesForTest);
            Assert.Contains(window.DiagnosticsForTest, message => message.Contains("父母候选存在多项"));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var final = db.CreateContext();
        var relations = await final.PersonRelationships.Where(r => r.PersonId == childId).ToListAsync();
        Assert.Equal(2, relations.Count); Assert.Contains(relations, r => r.RelatedPersonId == parent.Id); Assert.Contains(relations, r => r.RelatedPersonId == first.Id);
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Projected_input_survives_redraw_and_blocks_switch_save_and_exit()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "甲" }; var other = new Genealogy { Name = "乙" };
        var parent = new Person { GenealogyId = g.Id, Name = "父母", RecordedGeneration = "1" };
        var otherPerson = new Person { GenealogyId = g.Id, Name = "旁人", RecordedGeneration = "2" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.AddRange(g, other); seed.People.AddRange(parent, otherPerson); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(parent.Id);
            var temporary = Guid.NewGuid(); var edge = Guid.NewGuid(); var height = 85d;
            HorizontalLayoutNode Project() => InlineNamePlacement.Project(
                [new(parent.Id, 0, 0, 170, height), new(temporary, 1, 0, 170, 85)],
                [new(edge, parent.Id, temporary)], new HashSet<Guid> { parent.Id }, temporary);
            string? result = null;
            window.BeginProjectedNameInput(Project, name => result = name);
            var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single();
            Assert.Equal(Project().X + 20, Canvas.GetLeft(editor));
            editor.Input.Text = "临时姓名";
            height = 120; window.RenderCanvasForTest();
            Assert.Same(editor, window.CanvasForTest.Children.OfType<InlineNameEditor>().Single());
            Assert.Equal(WorkbenchProfileWindow.ProjectedNameTop(Project()), Canvas.GetTop(editor)); Assert.Equal("临时姓名", editor.Input.Text);
            await window.SelectPersonForTest(otherPerson.Id); Assert.Equal(parent.Id, window.SessionForTest.Draft!.Original.PersonId);
            await window.SelectGenealogyForTest(other.Id); Assert.Equal(g.Id, window.SessionForTest.Draft!.Original.GenealogyId);
            await window.SaveForTest(); Assert.Contains("Enter", window.StatusForTest);
            Assert.False(await window.PrepareForOwnerExitAsync());
            var source = PresentationSource.FromVisual(editor.Input)!;
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Null(result); Assert.Empty(window.CanvasForTest.Children.OfType<InlineNameEditor>());
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal(2, await verify.People.CountAsync());
    }

    [Fact]
    [Trait("Category", "UI")]
    public async Task Inline_input_is_retained_but_cannot_commit_when_projection_invalidates()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var p = new Person { GenealogyId = g.Id, Name = "原名", RecordedGeneration = "1" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(p); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(p.Id);
            var card = window.CanvasCardForTest(p.Id);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(p.Id).ContextMenu is not null);
            window.CanvasCardForTest(p.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "原位编辑姓名")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); editor.Input.Text = "保留输入";
            await using (var changed = db.CreateContext()) await changed.People.Where(x => x.Id == p.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Biography, "外部修改"));
            await window.PollCanvasForTest();
            Assert.Same(editor, window.CanvasForTest.Children.OfType<InlineNameEditor>().Single()); Assert.Equal("保留输入", editor.Input.Text);
            var source = PresentationSource.FromVisual(editor.Input)!;
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Equal("原名", window.SessionForTest.Draft!.Values.Name); Assert.Contains("尚未应用", window.StatusForTest);
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Inline_name_editor_updates_only_draft_and_escape_cancels()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var p = new Person { GenealogyId = g.Id, Name = "原名", RecordedGeneration = "1" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(p); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show();
            await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(p.Id);
            async Task<InlineNameEditor> Open()
            {
                var card = window.CanvasCardForTest(p.Id);
                card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
                await WaitUntil(() => window.CanvasCardForTest(p.Id).ContextMenu is not null);
                var menu = window.CanvasCardForTest(p.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "原位编辑姓名"));
                menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                return window.CanvasForTest.Children.OfType<InlineNameEditor>().Single();
            }
            void Key(InlineNameEditor editor, Key key) => editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            var editor = await Open();
            Assert.Equal(Canvas.GetLeft(window.CanvasCardForTest(p.Id)), Canvas.GetLeft(editor));
            editor.Input.Text = "取消名"; Key(editor, System.Windows.Input.Key.Escape);
            Assert.Equal("原名", window.SessionForTest.Draft!.Values.Name);
            Assert.Empty(window.CanvasForTest.Children.OfType<InlineNameEditor>());
            editor = await Open(); editor.Input.Text = "草稿名"; Key(editor, System.Windows.Input.Key.Enter);
            Assert.Equal("草稿名", window.SessionForTest.Draft!.Values.Name);
            Assert.Empty(window.CanvasForTest.Children.OfType<InlineNameEditor>());
            string CardName() => Assert.IsType<TextBlock>(window.CanvasCardForTest(p.Id).Child).Text;
            Assert.EndsWith("草稿名", CardName());
            window.RenderCanvasForTest(); Assert.EndsWith("草稿名", CardName());
            await using (var beforeSave = db.CreateContext()) Assert.Equal("原名", (await beforeSave.People.SingleAsync()).Name);
            window.ChooseForTest = () => DraftLeaveChoice.Discard;
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal("原名", (await verify.People.SingleAsync()).Name);
    }
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
    public async Task Inline_root_cancel_then_blank_confirm_in_nonfirst_generation_and_reload()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "空谱" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); await seed.SaveChangesAsync(); }
        Guid rootId = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            Assert.Equal(3, WorkbenchProfileWindow.CanvasGenerationAt(350));
            await window.BeginInlineRootForTest(3); var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single();
            Assert.Equal(PersonSex.Male, window.SessionForTest.Draft!.Values.Sex); Assert.Equal("3", window.SessionForTest.Draft.Values.RecordedGeneration);
            editor.Input.Text = "取消"; window.RenderCanvasForTest(); Assert.Same(editor, window.CanvasForTest.Children.OfType<InlineNameEditor>().Single()); Assert.Equal("取消", editor.Input.Text);
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await WaitUntil(() => window.SessionForTest.Draft is null);
            await using (var check = db.CreateContext()) { Assert.Empty(await check.People.ToListAsync()); Assert.Empty(await check.PersonRelationships.ToListAsync()); }
            await window.BeginInlineRootForTest(3); editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single();
            rootId = window.SessionForTest.Draft!.Original.PersonId; editor.Input.Text = "独立人物";
            var canvas = window.CanvasForTest; var viewport = window.CanvasScrollForTest;
            window.UpdateLayout(); viewport.UpdateLayout();
            Assert.True(viewport.ViewportHeight > 380);
            var hit = viewport.InputHitTest(new Point(30, 380)) as UIElement;
            Assert.Same(canvas, hit);
            canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            await WaitUntil(() => window.SessionForTest.Draft is { Original.IsNew: false } && window.SessionForTest.Draft.Original.PersonId == rootId);
            Assert.Contains("独立人物", Assert.IsType<TextBlock>(window.CanvasCardForTest(rootId).Child).Text);
            window.SetNameForTest("人物草稿"); Assert.EndsWith("人物草稿", Assert.IsType<TextBlock>(window.CanvasCardForTest(rootId).Child).Text); window.SetNameForTest("独立人物");
            await using (var check = db.CreateContext()) Assert.Single(await check.People.ToListAsync());
            window.SetGenerationForTest("4"); Assert.True(Canvas.GetLeft(window.CanvasCardForTest(rootId)) >= 3 * 164);
            await window.SaveForTest(); Assert.Equal(4, window.CanvasColumnForTest(rootId)); Assert.Equal(rootId, window.SessionForTest.Draft!.Original.PersonId);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); var person = await verify.People.SingleAsync();
        Assert.Equal(rootId, person.Id); Assert.Equal("4", person.RecordedGeneration); Assert.Equal(PersonSex.Male, person.Sex);
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Selected_existing_person_updates_projection_and_save_keeps_visual_anchor()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var parent = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male, RecordedGeneration = "7" };
        var child = new Person { GenealogyId = g.Id, Name = "子", Sex = PersonSex.Male, RecordedGeneration = "8" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(parent, child); seed.PersonRelationships.Add(new() { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent }); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(child.Id);
            window.UpdateLayout(); var scroll = window.CanvasScrollForTest; scroll.ScrollToHorizontalOffset(650); scroll.UpdateLayout();
            window.SetNameForTest("更名草稿"); window.SetSexForTest(PersonSex.Female);
            Assert.Equal("♀ 更名草稿", Assert.IsType<TextBlock>(window.CanvasCardForTest(child.Id).Child).Text);
            window.SetGenerationForTest("9"); Assert.Equal(20 + 8 * 164, Canvas.GetLeft(window.CanvasCardForTest(child.Id)));
            Assert.Single(window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>());
            var before = Canvas.GetLeft(window.CanvasCardForTest(child.Id)) - scroll.HorizontalOffset;
            await window.SaveForTest(); scroll.UpdateLayout();
            Assert.Equal(child.Id, window.SessionForTest.Draft!.Original.PersonId);
            Assert.Equal(before, Canvas.GetLeft(window.CanvasCardForTest(child.Id)) - scroll.HorizontalOffset, 1);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); var saved = await verify.People.SingleAsync(p => p.Id == child.Id);
        Assert.Equal("更名草稿", saved.Name); Assert.Equal(PersonSex.Female, saved.Sex); Assert.Equal("9", saved.RecordedGeneration);
        Assert.Single(await verify.PersonRelationships.ToListAsync());
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Blank_canvas_deselection_respects_cancel_discard_and_save()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var person = new Person { GenealogyId = g.Id, Name = "原名", Sex = PersonSex.Male, RecordedGeneration = "1" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(person); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id); await window.SelectPersonForTest(person.Id);
            window.SetNameForTest("未保存"); window.ChooseForTest = () => DraftLeaveChoice.Cancel;
            var canvas = window.CanvasForTest;
            canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            await WaitUntil(() => window.StatusForTest.Contains("保留当前草稿"));
            Assert.Equal(person.Id, window.SessionForTest.Draft!.Original.PersonId); Assert.Equal("未保存", window.SessionForTest.Draft.Values.Name);
            window.ChooseForTest = () => DraftLeaveChoice.Discard; await window.ClearCanvasSelectionForTest(); Assert.Null(window.SessionForTest.Draft);
            Assert.EndsWith("原名", Assert.IsType<TextBlock>(window.CanvasCardForTest(person.Id).Child).Text);
            await window.SelectCanvasForTest(person.Id); window.SetNameForTest("已保存"); window.ChooseForTest = () => DraftLeaveChoice.Save;
            await window.ClearCanvasSelectionForTest(); Assert.Null(window.SessionForTest.Draft);
            Assert.EndsWith("已保存", Assert.IsType<TextBlock>(window.CanvasCardForTest(person.Id).Child).Text);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal("已保存", (await verify.People.SingleAsync()).Name);
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Invalidated_empty_canvas_does_not_redraw_stale_root_draft()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "空谱" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); await seed.SaveChangesAsync(); }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            await window.BeginInlineRootForTest(1); var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); editor.Input.Text = "未保存";
            var id = window.SessionForTest.Draft!.Original.PersonId;
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await WaitUntil(() => window.SessionForTest.Draft is { Original.IsNew: false } && window.SessionForTest.Draft.Original.PersonId == id);
            window.SetGenerationForTest("2");
            await using (var other = db.CreateContext()) { other.People.Add(new Person { GenealogyId = g.Id, Name = "外部", RecordedGeneration = "1" }); await other.SaveChangesAsync(); }
            await window.PollCanvasForTest(); Assert.Contains("草稿保留", window.StatusForTest);
            window.SetGenerationForTest("2"); Assert.DoesNotContain(window.CanvasForTest.Children.OfType<Border>(), b => Equals(b.Tag, id));
            Assert.Equal("未保存", window.SessionForTest.Draft!.Values.Name); Assert.True(window.SessionForTest.Draft.IsDirty);
            await window.SaveForTest(); Assert.Contains("未保存", window.StatusForTest); Assert.True(window.SessionForTest.Draft.IsDirty);
            window.ChooseForTest = () => DraftLeaveChoice.Discard; Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Equal(2, await verify.People.CountAsync());
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Stage_b_four_additions_survive_switch_and_reload_without_cross_spectrum_writes()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var other = new Genealogy { Name = "另一虚构谱" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.AddRange(g, other); await seed.SaveChangesAsync(); }
        Guid rootId = default, spouseId = default, childId = default, siblingId = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            async Task Confirm(string name)
            {
                var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); editor.Input.Text = name;
                var id = window.SessionForTest.Draft!.Original.PersonId;
                editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                await WaitUntil(() => window.SessionForTest.Draft is { Original.IsNew: false } && window.SessionForTest.Draft.Original.PersonId == id);
            }
            async Task BeginRelated(Guid personId, string title)
            {
                await window.SelectCanvasForTest(personId);
                var card = window.CanvasCardForTest(personId);
                card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
                await WaitUntil(() => window.CanvasCardForTest(personId).ContextMenu is not null);
                window.CanvasCardForTest(personId).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, title)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            }
            await window.BeginInlineRootForTest(1); rootId = window.SessionForTest.Draft!.Original.PersonId; await Confirm("父亲");
            Assert.Equal(rootId, window.SessionForTest.Draft!.Original.PersonId);
            await BeginRelated(rootId, "新增配偶（婚配）"); spouseId = window.SessionForTest.Draft!.Original.PersonId; await Confirm("母亲");
            Assert.Equal(spouseId, window.SessionForTest.Draft!.Original.PersonId);
            await BeginRelated(rootId, "新增子女（生物关系）"); childId = window.SessionForTest.Draft!.Original.PersonId; await Confirm("子女");
            Assert.Contains("母亲", window.DraftParentsForTest);
            await BeginRelated(childId, "新增兄妹"); siblingId = window.SessionForTest.Draft!.Original.PersonId; await Confirm("兄妹");
            window.SetNameForTest("兄妹草稿");
            window.ChooseForTest = () => DraftLeaveChoice.Cancel; await window.SelectGenealogyForTest(other.Id);
            Assert.Equal(g.Id, window.SessionForTest.Draft!.Original.GenealogyId); Assert.Equal(siblingId, window.SessionForTest.Draft.Original.PersonId);
            await using (var check = db.CreateContext()) { Assert.Equal("兄妹", (await check.People.SingleAsync(p => p.Id == siblingId)).Name); Assert.False(await check.People.AnyAsync(p => p.GenealogyId == other.Id)); }
            window.ChooseForTest = () => DraftLeaveChoice.Save; await window.SelectGenealogyForTest(other.Id);
            Assert.Null(window.SessionForTest.Draft); Assert.False(window.CanvasContainsForTest(rootId));
            await window.SelectGenealogyForTest(g.Id); Assert.True(window.CanvasContainsForTest(rootId)); Assert.True(window.CanvasContainsForTest(childId)); Assert.True(window.CanvasContainsForTest(siblingId));
            await window.SelectCanvasForTest(siblingId); Assert.Equal(siblingId, window.SessionForTest.Draft!.Original.PersonId);
            Assert.Contains("父亲", window.DraftParentsForTest); Assert.Contains("母亲", window.DraftParentsForTest);
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
            var reopened = new WorkbenchProfileWindow(MakeCoordinator(db)); reopened.Show(); await reopened.InitializeForTest(); await reopened.SelectGenealogyForTest(g.Id);
            Assert.True(reopened.CanvasContainsForTest(childId)); Assert.True(reopened.CanvasContainsForTest(siblingId));
            await reopened.SelectCanvasForTest(siblingId); Assert.Contains("父亲", reopened.DraftParentsForTest); Assert.Contains("母亲", reopened.DraftParentsForTest);
            Assert.True(await reopened.PrepareForOwnerExitAsync()); reopened.Close();
        });
        await using var verify = db.CreateContext();
        Assert.Equal(4, await verify.People.CountAsync(p => p.GenealogyId == g.Id)); Assert.False(await verify.People.AnyAsync(p => p.GenealogyId == other.Id));
        var unions = await verify.FamilyUnions.ToListAsync(); Assert.Single(unions); Assert.Contains(rootId, new[] { unions[0].PartnerOneId, unions[0].PartnerTwoId }); Assert.Contains(spouseId, new[] { unions[0].PartnerOneId, unions[0].PartnerTwoId });
        var links = await verify.PersonRelationships.ToListAsync();
        foreach (var id in new[] { childId, siblingId })
        {
            Assert.Contains(links, r => r.PersonId == id && r.RelatedPersonId == rootId && r.Kind == PersonRelationshipKind.BiologicalParent);
            Assert.Contains(links, r => r.PersonId == id && r.RelatedPersonId == spouseId && r.Kind == PersonRelationshipKind.BiologicalParent);
        }
        Assert.Contains(links, r => r.PersonId == siblingId && r.RelatedPersonId == childId && r.Kind == PersonRelationshipKind.Sibling);
        Assert.All(links, r => Assert.Equal(g.Id, r.GenealogyId));
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
            await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            rootId = window.SessionForTest.Draft!.Original.PersonId;
            Assert.True(window.SessionForTest.Draft.IsDirty); Assert.Equal(PersonSex.Male, window.SessionForTest.Draft.Values.Sex);
            var rootEditor = canvas.Children.OfType<InlineNameEditor>().Single(); rootEditor.Input.Text = "人物";
            rootEditor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(rootEditor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.NotNull(window.CanvasCardForTest(rootId));
            await window.SaveForTest(); Assert.True(window.CanvasContainsForTest(rootId)); Assert.Equal(1, window.CanvasColumnForTest(rootId));
            var card = window.CanvasCardForTest(rootId);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(rootId).ContextMenu is not null);
            var childMenu = window.CanvasCardForTest(rootId).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增子女（生物关系）"));
            childMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            childId = window.SessionForTest.Draft!.Original.PersonId;
            Assert.Equal("2", window.SessionForTest.Draft.Values.RecordedGeneration);
            Assert.Equal(PersonSex.Male, window.SessionForTest.Draft.Values.Sex);
            var childEditor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single();
            childEditor.Input.Text = "子女";
            childEditor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(childEditor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Equal(childId, window.SessionForTest.Draft.Original.PersonId);
            Assert.Equal("子女", window.SessionForTest.Draft.Values.Name);
            Assert.NotNull(window.CanvasCardForTest(childId));
            await window.SaveForTest();
            Assert.True(window.CanvasContainsForTest(childId)); Assert.Equal(2, window.CanvasColumnForTest(childId));
            Assert.Equal(1, window.CanvasEdgesForTest);
            card = window.CanvasCardForTest(rootId);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(rootId).ContextMenu is not null);
            var spouseMenu = window.CanvasCardForTest(rootId).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增配偶（婚配）"));
            spouseMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            spouseId = window.SessionForTest.Draft!.Original.PersonId; Assert.Null(window.SessionForTest.Draft.Values.RecordedGeneration);
            Assert.Equal(PersonSex.Female, window.SessionForTest.Draft.Values.Sex);
            var spouseEditor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); spouseEditor.Input.Text = "配偶";
            spouseEditor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(spouseEditor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Equal(spouseId, window.SessionForTest.Draft.Original.PersonId); await window.SaveForTest();
            Assert.Equal(spouseId, window.SessionForTest.Draft!.Original.PersonId);
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
            var sibling = window.CanvasCardForTest(person.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增同世人物"));
            sibling.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.SessionForTest.Draft?.Original.IsNew == true);
            await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); editor.Input.Text = "取消";
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await WaitUntil(() => window.SessionForTest.Draft?.Original.PersonId == person.Id);
            await using (var check = db.CreateContext()) { Assert.Single(await check.People.ToListAsync()); Assert.Empty(await check.PersonRelationships.ToListAsync()); }
            card = window.CanvasCardForTest(person.Id);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(person.Id).ContextMenu is not null);
            window.CanvasCardForTest(person.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增同世人物")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            siblingId = window.SessionForTest.Draft!.Original.PersonId; Assert.Equal("2", window.SessionForTest.Draft.Values.RecordedGeneration);
            editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); editor.Input.Text = "兄妹";
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Equal(PersonSex.Male, window.SessionForTest.Draft.Values.Sex);
            window.SetNameForTest("兄妹草稿"); Assert.EndsWith("兄妹草稿", Assert.IsType<TextBlock>(window.CanvasCardForTest(siblingId).Child).Text);
            window.SetNameForTest("兄妹"); window.SetGenerationForTest("3"); Assert.Equal(20 + 2 * 164, Canvas.GetLeft(window.CanvasCardForTest(siblingId)));
            window.SetGenerationForTest("2"); await window.SaveForTest();
            Assert.True(window.CanvasContainsForTest(siblingId)); Assert.Empty(window.CanvasSiblingsForTest(siblingId));
            Assert.Empty(window.CanvasSiblingsForTest(person.Id));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Sibling_inherits_biological_parents_without_special_relationships()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var person = new Person { GenealogyId = g.Id, Name = "人物", RecordedGeneration = "二世" };
        var father = new Person { GenealogyId = g.Id, Name = "生父", Sex = PersonSex.Male, RecordedGeneration = "一世" };
        var mother = new Person { GenealogyId = g.Id, Name = "生母", Sex = PersonSex.Female, RecordedGeneration = "一世" };
        var heir = new Person { GenealogyId = g.Id, Name = "嗣父", Sex = PersonSex.Male, RecordedGeneration = "一世" };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(person, father, mother, heir);
            seed.PersonRelationships.AddRange(new PersonRelationship { GenealogyId = g.Id, PersonId = person.Id, RelatedPersonId = father.Id, Kind = PersonRelationshipKind.BiologicalParent },
                new PersonRelationship { GenealogyId = g.Id, PersonId = person.Id, RelatedPersonId = mother.Id, Kind = PersonRelationshipKind.BiologicalParent },
                new PersonRelationship { GenealogyId = g.Id, PersonId = person.Id, RelatedPersonId = heir.Id, Kind = PersonRelationshipKind.HeirParent });
            await seed.SaveChangesAsync();
        }
        Guid siblingId = default;
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            var card = window.CanvasCardForTest(person.Id);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(person.Id).ContextMenu is not null);
            window.CanvasCardForTest(person.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增兄妹")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            var editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single();
            Assert.Equal(Canvas.GetLeft(window.CanvasCardForTest(person.Id)), Canvas.GetLeft(editor));
            Assert.Contains("生父", window.SessionForTest.Draft!.Original.Parents!.Select(p => p.Name));
            Assert.Contains("生母", window.SessionForTest.Draft.Original.Parents!.Select(p => p.Name));
            siblingId = window.SessionForTest.Draft.Original.PersonId; editor.Input.Text = "兄妹";
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Equal(siblingId, window.SessionForTest.Draft.Original.PersonId); Assert.Contains("生父", window.DraftParentsForTest); Assert.Contains("生母", window.DraftParentsForTest);
            await window.SaveForTest(); Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); var relations = await verify.PersonRelationships.Where(r => r.PersonId == siblingId).ToListAsync();
        Assert.Contains(relations, r => r.Kind == PersonRelationshipKind.BiologicalParent && r.RelatedPersonId == father.Id);
        Assert.Contains(relations, r => r.Kind == PersonRelationshipKind.BiologicalParent && r.RelatedPersonId == mother.Id);
        Assert.DoesNotContain(relations, r => r.RelatedPersonId == heir.Id);
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
    [Fact]
    [Trait("Category", "UI")]
    public async Task Explicit_sibling_display_order_moves_without_writing_recorded_rank_and_expires_on_change()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var parent = new Person { GenealogyId = g.Id, Name = "父", RecordedGeneration = "一世" };
        var a = new Person { GenealogyId = g.Id, Name = "甲", RecordedGeneration = "二世", RecordedBirthOrder = "原文甲" };
        var b = new Person { GenealogyId = g.Id, Name = "乙", RecordedGeneration = "二世", RecordedBirthOrder = "原文乙" };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(parent, a, b);
            seed.PersonRelationships.AddRange(new PersonRelationship { GenealogyId = g.Id, PersonId = a.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent },
                new PersonRelationship { GenealogyId = g.Id, PersonId = b.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent });
            await seed.SaveChangesAsync();
        }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            Assert.Equal(0, window.CanvasEdgesForTest);
            await window.SelectCanvasForTest(a.Id); window.SetNameForTest("草稿甲"); await window.SetDisplayRankForTest(2);
            Assert.True(window.SessionForTest.Draft!.IsDirty); Assert.Equal(0, window.CanvasEdgesForTest);
            window.ChooseForTest = () => DraftLeaveChoice.Discard;
            await window.SelectCanvasForTest(b.Id); await window.SetDisplayRankForTest(2); Assert.Contains("已指定给另一人", window.StatusForTest);
            await window.SetDisplayRankForTest(1); Assert.Equal(2, window.CanvasEdgesForTest);
            Assert.True(Canvas.GetTop(window.CanvasCardForTest(b.Id)) < Canvas.GetTop(window.CanvasCardForTest(a.Id)));
            var card = window.CanvasCardForTest(b.Id);
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            await WaitUntil(() => window.CanvasCardForTest(b.Id).ContextMenu is not null);
            var down = window.CanvasCardForTest(b.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "同胞下移"));
            Assert.True(down.IsEnabled); down.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => Canvas.GetTop(window.CanvasCardForTest(a.Id)) < Canvas.GetTop(window.CanvasCardForTest(b.Id)));
            await using (var verify = db.CreateContext()) { Assert.Equal("原文甲", (await verify.People.SingleAsync(p => p.Id == a.Id)).RecordedBirthOrder); Assert.Equal("原文乙", (await verify.People.SingleAsync(p => p.Id == b.Id)).RecordedBirthOrder); }
            await using (var other = db.CreateContext()) await other.People.Where(p => p.Id == a.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.RecordedBirthOrder, "外部改动"));
            await window.PollCanvasForTest(); Assert.False(window.CanvasContainsForTest(a.Id));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
    }
    [Fact]
    [Trait("Category", "UI")]
    public async Task Explicit_siblings_without_parents_can_be_ordered_without_creating_parents()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var a = new Person { GenealogyId = g.Id, Name = "甲", RecordedGeneration = "一世" };
        var b = new Person { GenealogyId = g.Id, Name = "乙", RecordedGeneration = "一世" };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(a, b);
            seed.PersonRelationships.Add(new PersonRelationship { GenealogyId = g.Id, PersonId = b.Id, RelatedPersonId = a.Id, Kind = PersonRelationshipKind.Sibling }); await seed.SaveChangesAsync();
        }
        await RunSta(async () =>
        {
            var window = new WorkbenchProfileWindow(MakeCoordinator(db)); window.Show(); await window.InitializeForTest(); await window.SelectGenealogyForTest(g.Id);
            await window.SelectCanvasForTest(a.Id); await window.SetDisplayRankForTest(2);
            await window.SelectCanvasForTest(b.Id); await window.SetDisplayRankForTest(1);
            Assert.True(Canvas.GetTop(window.CanvasCardForTest(b.Id)) < Canvas.GetTop(window.CanvasCardForTest(a.Id)));
            var gap = Canvas.GetTop(window.CanvasCardForTest(a.Id)) - Canvas.GetTop(window.CanvasCardForTest(b.Id)) - window.CanvasCardForTest(b.Id).Height;
            Assert.InRange(gap, 0, 100);
            await window.MoveForTest(1); Assert.True(Canvas.GetTop(window.CanvasCardForTest(a.Id)) < Canvas.GetTop(window.CanvasCardForTest(b.Id)));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); Assert.Single(await verify.PersonRelationships.ToListAsync());
        Assert.All(await verify.People.ToListAsync(), p => Assert.Null(p.RecordedBirthOrder));
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
