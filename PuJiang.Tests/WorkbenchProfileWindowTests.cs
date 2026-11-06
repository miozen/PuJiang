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
            Assert.Equal(createdId, window.SessionForTest.Draft!.Original.PersonId); Assert.Equal("新配偶", window.SessionForTest.Draft.Values.Name);
            var spouseCard = window.CanvasForTest.Children.OfType<Border>().Single(b => Equals(b.Tag, createdId));
            Assert.Contains("新配偶", Assert.IsType<TextBlock>(spouseCard.Child).Text);
            Assert.Equal(System.Windows.Media.Brushes.DarkOrange, spouseCard.Background);
            await using (var verify = db.CreateContext()) { Assert.Equal(3, await verify.People.CountAsync()); Assert.Single(await verify.FamilyUnions.ToListAsync()); }
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
            Assert.Equal("新子女", window.SessionForTest.Draft!.Values.Name); Assert.NotNull(window.CanvasCardForTest(childId));
            Assert.Contains(parentSex == PersonSex.Male ? "生父：所选父母" : "生母：所选父母", window.DraftParentsForTest);
            Assert.Contains(parentSex == PersonSex.Male ? "生母：配偶甲" : "生父：配偶甲", window.DraftParentsForTest);
            await using (var verify = db.CreateContext()) Assert.Equal(3, await verify.People.CountAsync());
            await window.SaveForTest(); Assert.True(window.CanvasContainsForTest(childId)); Assert.Equal(1, window.CanvasEdgesForTest);
            Assert.Contains(parentSex == PersonSex.Male ? "生父：所选父母" : "生母：所选父母", window.DraftParentsForTest);
            Assert.Contains(parentSex == PersonSex.Male ? "生母：配偶甲" : "生父：配偶甲", window.DraftParentsForTest);
            editor = await Start();
            Assert.Equal(2, window.CanvasForTest.Children.OfType<System.Windows.Shapes.Polyline>().Count());
            editor.Input.Text = "第二位子女"; ClickBlank();
            var secondChild = window.SessionForTest.Draft!.Original.PersonId;
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
            Assert.True(window.SessionForTest.Draft.IsDirty); window.SetNameForTest("始祖"); window.SetSexForTest(PersonSex.Male);
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
            var sibling = window.CanvasCardForTest(person.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增兄妹"));
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
            window.CanvasCardForTest(person.Id).ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "新增兄妹")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => window.CanvasForTest.Children.OfType<InlineNameEditor>().Any());
            siblingId = window.SessionForTest.Draft!.Original.PersonId; Assert.Equal("2", window.SessionForTest.Draft.Values.RecordedGeneration);
            editor = window.CanvasForTest.Children.OfType<InlineNameEditor>().Single(); editor.Input.Text = "兄妹";
            editor.Input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor.Input)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Equal(PersonSex.Male, window.SessionForTest.Draft.Values.Sex); await window.SaveForTest();
            Assert.True(window.CanvasContainsForTest(siblingId)); Assert.Contains("人物", window.CanvasSiblingsForTest(siblingId));
            Assert.Contains("兄妹", window.CanvasSiblingsForTest(person.Id));
            Assert.True(await window.PrepareForOwnerExitAsync()); window.Close();
        });
        await using var verify = db.CreateContext(); var relation = await verify.PersonRelationships.SingleAsync();
        Assert.Equal(PersonRelationshipKind.Sibling, relation.Kind); Assert.Equal(siblingId, relation.PersonId); Assert.Equal(person.Id, relation.RelatedPersonId);
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
