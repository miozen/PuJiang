using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class SpouseOrderStorageTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Migrating_previous_database_preserves_charts_and_unions()
    {
        await using var db = new TestDatabase();
        await using (var before = db.CreateContext()) await before.GetService<IMigrator>().MigrateAsync("20261105081258_AddLineageSiblingOrders");
        var f = await SeedAsync(db);
        await using (var upgrade = db.CreateContext()) await upgrade.Database.MigrateAsync();
        await using var verify = db.CreateContext();
        Assert.Equal(2, await verify.LineageCharts.CountAsync()); Assert.Equal(2, await verify.FamilyUnions.CountAsync());
        Assert.Empty(await verify.LineageSpouseOrders.ToListAsync());
        var capture = await new SpouseOrderStorageService(verify).CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
        Assert.Empty(capture.SavedOrder);
    }

    private sealed record Fixture(Genealogy G, Person Owner, FamilyUnion First, FamilyUnion Second, Guid Chart, Guid OtherChart);
    private static async Task<Fixture> SeedAsync(TestDatabase db)
    {
        await using var context = db.CreateContext(); var g = new Genealogy { Name = "虚构谱" };
        var owner = new Person { GenealogyId = g.Id, Name = "人物", Sex = PersonSex.Male, RecordedGeneration = "1" }; var a = new Person { GenealogyId = g.Id, Name = "配偶甲", Sex = PersonSex.Female }; var b = new Person { GenealogyId = g.Id, Name = "配偶乙", Sex = PersonSex.Female };
        var first = new FamilyUnion { GenealogyId = g.Id, PartnerOneId = owner.Id, PartnerTwoId = a.Id, Kind = FamilyUnionKind.Marriage };
        var second = new FamilyUnion { GenealogyId = g.Id, PartnerOneId = owner.Id, PartnerTwoId = b.Id, Kind = FamilyUnionKind.Marriage };
        context.Genealogies.Add(g); context.People.AddRange(owner, a, b); context.FamilyUnions.AddRange(first, second); await context.SaveChangesAsync();
        var graph = await new GenealogyDataService(context).GetGraphSnapshotAsync(g.Id); var charts = new LineageChartService(context);
        var chart = await charts.SaveAsync(g.Id, null, "图一", null, graph, new Dictionary<Guid, Guid>());
        var other = await charts.SaveAsync(g.Id, null, "图二", null, graph, new Dictionary<Guid, Guid>());
        return new(g, owner, first, second, chart.Id, other.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Order_is_explicit_per_chart_and_survives_reopen()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var context = db.CreateContext())
        {
            var service = new SpouseOrderStorageService(context); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
            Assert.Empty(capture.SavedOrder);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(capture, new[] { f.First.Id, f.First.Id }));
            await service.SaveAsync(capture, new[] { f.Second.Id, f.First.Id });
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(capture, new[] { f.Second.Id, f.First.Id }));
        }
        await using var verify = db.CreateContext(); var reopened = new SpouseOrderStorageService(verify);
        Assert.Equal(new[] { f.Second.Id, f.First.Id }, (await reopened.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id)).SavedOrder);
        Assert.Empty((await reopened.CaptureAsync(f.G.Id, f.OtherChart, f.Owner.Id)).SavedOrder);
        Assert.Equal(2, await verify.FamilyUnions.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Saving_existing_chart_choices_preserves_spouse_order()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var context = db.CreateContext())
        {
            var service = new SpouseOrderStorageService(context); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
            await service.SaveAsync(capture, new[] { f.Second.Id, f.First.Id });
        }
        await using (var update = db.CreateContext())
        {
            var graph = await new GenealogyDataService(update).GetGraphSnapshotAsync(f.G.Id);
            var revision = (await update.LineageCharts.SingleAsync(c => c.Id == f.Chart)).Revision;
            await new LineageChartService(update).SaveAsync(f.G.Id, f.Chart, "新图名", revision, graph, new Dictionary<Guid, Guid>());
        }
        await using var verify = db.CreateContext(); var reopened = new SpouseOrderStorageService(verify);
        Assert.Equal(new[] { f.Second.Id, f.First.Id }, (await reopened.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id)).SavedOrder);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Failed_write_rolls_back_items_and_revision_then_allows_retry()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var initial = db.CreateContext()) { var service = new SpouseOrderStorageService(initial); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await service.SaveAsync(capture, new[] { f.First.Id, f.Second.Id }); }
        await using var context = db.CreateContext(); var writer = new SpouseOrderStorageService(context);
        var next = await writer.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
        writer.AfterItemForTest = n => n == 1 ? Task.FromException(new InvalidOperationException("injected")) : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveAsync(next, new[] { f.Second.Id, f.First.Id }));
        await using (var verify = db.CreateContext())
        {
            Assert.Equal(next.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.Chart)).Revision);
            Assert.Equal(new[] { f.First.Id, f.Second.Id }, await verify.LineageSpouseOrderItems.Where(i => i.GroupId == next.GroupId).OrderBy(i => i.Position).Select(i => i.UnionId).ToArrayAsync());
        }
        writer.AfterItemForTest = null; await writer.SaveAsync(next, new[] { f.Second.Id, f.First.Id });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Competing_chart_revision_and_changed_relationship_are_rejected()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using var contextA = db.CreateContext(); await using var contextB = db.CreateContext();
        var a = new SpouseOrderStorageService(contextA); var b = new SpouseOrderStorageService(contextB);
        var first = await a.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); var stale = await b.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
        await a.SaveAsync(first, new[] { f.First.Id, f.Second.Id });
        await Assert.ThrowsAnyAsync<Exception>(() => b.SaveAsync(stale, new[] { f.Second.Id, f.First.Id }));
        await using var contextC = db.CreateContext(); var c = new SpouseOrderStorageService(contextC); var next = await c.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
        await using (var external = db.CreateContext()) await external.FamilyUnions.Where(u => u.Id == f.First.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Note, "外部变更"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveAsync(next, new[] { f.Second.Id, f.First.Id }));
        await using var verify = db.CreateContext();
        Assert.Equal(next.ChartRevision, (await verify.LineageCharts.SingleAsync(chart => chart.Id == f.Chart)).Revision);
        Assert.Equal(new[] { f.First.Id, f.Second.Id }, await verify.LineageSpouseOrderItems.Where(i => i.GroupId == next.GroupId).OrderBy(i => i.Position).Select(i => i.UnionId).ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancellation_and_cross_genealogy_access_leave_no_partial_order()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        var other = new Genealogy { Name = "另一谱" };
        await using (var setup = db.CreateContext()) { setup.Genealogies.Add(other); await setup.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var service = new SpouseOrderStorageService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(other.Id, f.Chart, f.Owner.Id));
        var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
        using var cancellation = new CancellationTokenSource();
        service.AfterItemForTest = n => { if (n == 1) cancellation.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(capture, new[] { f.First.Id, f.Second.Id }, cancellation.Token));
        await using var verify = db.CreateContext(); Assert.Empty(await verify.LineageSpouseOrders.ToListAsync());
        Assert.Equal(capture.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.Chart)).Revision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Changed_spouse_set_requires_review_and_explicit_confirmation()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var initial = db.CreateContext()) { var service = new SpouseOrderStorageService(initial); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await service.SaveAsync(capture, new[] { f.First.Id, f.Second.Id }); }
        var person = new Person { GenealogyId = f.G.Id, Name = "配偶丙" };
        var union = new FamilyUnion { GenealogyId = f.G.Id, PartnerOneId = f.Owner.Id, PartnerTwoId = person.Id, Kind = FamilyUnionKind.Marriage };
        await using (var changed = db.CreateContext()) { changed.People.Add(person); changed.FamilyUnions.Add(union); await changed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var serviceAfterChange = new SpouseOrderStorageService(context);
        var review = await serviceAfterChange.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
        Assert.True(review.NeedsReview); Assert.Equal(new[] { f.First.Id, f.Second.Id }, review.SavedOrder);
        Assert.Equal(2, review.Historical.Count); Assert.Equal(3, review.Evidence.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => serviceAfterChange.SaveAsync(review, new[] { f.First.Id, f.Second.Id, union.Id }));
        await serviceAfterChange.SaveAsync(review, new[] { f.First.Id, f.Second.Id, union.Id }, confirmChangedHistory: true);
        await using var verify = db.CreateContext(); var reopened = await new SpouseOrderStorageService(verify).CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
        Assert.False(reopened.NeedsReview); Assert.Equal(new[] { f.First.Id, f.Second.Id, union.Id }, reopened.SavedOrder);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Child_default_uses_selected_chart_order_and_rejects_concurrent_reorder()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var noOrder = db.CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkbenchPersonProfileCommand(noOrder).BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
        await using (var store = db.CreateContext())
        {
            var service = new SpouseOrderStorageService(store); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
            await service.SaveAsync(capture, new[] { f.Second.Id, f.First.Id });
        }
        await using (var store = db.CreateContext())
        {
            var service = new SpouseOrderStorageService(store); var capture = await service.CaptureAsync(f.G.Id, f.OtherChart, f.Owner.Id);
            await service.SaveAsync(capture, new[] { f.First.Id, f.Second.Id });
        }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
        Assert.Equal(f.Second.PartnerTwoId, draft.Original.SecondParentId);
        Assert.Equal(f.First.PartnerTwoId, (await command.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.OtherChart)).SecondParentId);
        draft.Values = draft.Values with { Name = "子女" };
        await using (var reorder = db.CreateContext())
        {
            var service = new SpouseOrderStorageService(reorder); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
            await service.SaveAsync(capture, new[] { f.First.Id, f.Second.Id });
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using (var verify = db.CreateContext()) Assert.False(await verify.People.AnyAsync(p => p.Id == draft.Original.PersonId));
        var fresh = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
        Assert.Equal(f.First.PartnerTwoId, fresh.Original.SecondParentId); fresh.Values = fresh.Values with { Name = "子女" }; await fresh.SaveAsync(command);
        await using var final = db.CreateContext(); var parents = await final.PersonRelationships.Where(r => r.PersonId == fresh.Original.PersonId).Select(r => r.RelatedPersonId).ToListAsync();
        Assert.Contains(f.Owner.Id, parents); Assert.Contains(f.First.PartnerTwoId, parents); Assert.DoesNotContain(f.Second.PartnerTwoId, parents);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task New_spouse_and_chart_append_commit_together_and_rollback_on_order_failure()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var store = db.CreateContext())
        {
            var service = new SpouseOrderStorageService(store); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
            await service.SaveAsync(capture, new[] { f.Second.Id, f.First.Id });
        }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginNewSpouseForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
        draft.Values = draft.Values with { Name = "新配偶" };
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectNewSpouseOrder BEFORE INSERT ON LineageSpouseOrderItems WHEN NEW.Position=3 BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using (var verify = db.CreateContext())
        {
            Assert.False(await verify.People.AnyAsync(p => p.Id == draft.Original.PersonId));
            Assert.Equal(2, await verify.FamilyUnions.CountAsync());
            Assert.Equal(draft.Original.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.Chart)).Revision);
            Assert.Equal(new[] { f.Second.Id, f.First.Id }, await verify.LineageSpouseOrderItems.Where(i => i.GroupId == draft.Original.ChartSpouseGroupId).OrderBy(i => i.Position).Select(i => i.UnionId).ToArrayAsync());
        }
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectNewSpouseOrder"); await draft.SaveAsync(command);
        await using var final = db.CreateContext(); var newUnion = await final.FamilyUnions.SingleAsync(u => u.PartnerOneId == draft.Original.PersonId || u.PartnerTwoId == draft.Original.PersonId);
        var order = await new SpouseOrderStorageService(final).CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
        Assert.False(order.NeedsReview); Assert.Equal(new[] { f.Second.Id, f.First.Id, newUnion.Id }, order.SavedOrder);
        Assert.Equal(3, await final.FamilyUnions.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task New_spouse_draft_rejects_chart_reorder_without_partial_creation()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var store = db.CreateContext()) { var service = new SpouseOrderStorageService(store); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await service.SaveAsync(capture, new[] { f.First.Id, f.Second.Id }); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginNewSpouseForChartAsync(f.G.Id, f.Owner.Id, f.Chart)); draft.Values = draft.Values with { Name = "新配偶" };
        await using (var other = db.CreateContext()) { var service = new SpouseOrderStorageService(other); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await service.SaveAsync(capture, new[] { f.Second.Id, f.First.Id }); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using var verify = db.CreateContext(); Assert.False(await verify.People.AnyAsync(p => p.Id == draft.Original.PersonId)); Assert.Equal(2, await verify.FamilyUnions.CountAsync());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    [Trait("Category", "Integration")]
    public async Task First_chart_spouse_append_creates_order_without_guessing(int existingCount)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var remove = db.CreateContext())
        {
            await remove.FamilyUnions.Where(u => u.Id == f.Second.Id).ExecuteDeleteAsync();
            if (existingCount == 0) await remove.FamilyUnions.Where(u => u.Id == f.First.Id).ExecuteDeleteAsync();
        }
        await using (var context = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(context);
            var draft = new WorkbenchPersonDraft(await command.BeginNewSpouseForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
            draft.Values = draft.Values with { Name = "新配偶" }; await draft.SaveAsync(command);
            await using var verify = db.CreateContext(); var created = await verify.FamilyUnions.SingleAsync(u => u.PartnerOneId == draft.Original.PersonId || u.PartnerTwoId == draft.Original.PersonId);
            var order = await new SpouseOrderStorageService(verify).CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
            Assert.False(order.NeedsReview);
            Assert.Equal(existingCount == 0 ? new[] { created.Id } : new[] { f.First.Id, created.Id }, order.SavedOrder);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Chart_children_append_by_sex_with_links_and_preserve_spouse_order_across_saves()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var setup = db.CreateContext()) { var s = new SpouseOrderStorageService(setup); var capture = await s.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await s.SaveAsync(capture, new[] { f.Second.Id, f.First.Id }); }
        async Task<Guid> Create(string name, PersonSex sex)
        {
            await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
            var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
            Assert.Equal(f.Second.PartnerTwoId, draft.Original.SecondParentId);
            draft.Values = draft.Values with { Name = name, Sex = sex }; await draft.SaveAsync(command); return draft.Original.PersonId;
        }
        var girl = await Create("长女", PersonSex.Female);
        var boy = await Create("长子", PersonSex.Male);
        var youngerGirl = await Create("次女", PersonSex.Female);
        await using var verify = db.CreateContext();
        var group = await verify.LineageSiblingOrders.Include(g => g.Items).SingleAsync(g => g.ChartId == f.Chart && g.ParentId == f.Owner.Id);
        Assert.Equal(new[] { boy, girl, youngerGirl }, group.Items.OrderBy(i => i.Position).Select(i => i.PersonId));
        Assert.Equal(3, await verify.LineageChartChoices.CountAsync(c => c.ChartId == f.Chart));
        var chart = await verify.LineageCharts.SingleAsync(c => c.Id == f.Chart);
        var graph = await new GenealogyDataService(verify).GetGraphSnapshotAsync(f.G.Id);
        Assert.Equal(graph.UnlinkGeneration, chart.Generation); Assert.Equal(graph.UnlinkGeneration, group.EvidenceGeneration);
        Assert.All(await verify.PersonRelationships.Where(r => r.PersonId == girl || r.PersonId == boy || r.PersonId == youngerGirl).ToListAsync(), r => Assert.Equal(PersonRelationshipKind.BiologicalParent, r.Kind));
        Assert.Equal(6, await verify.PersonRelationships.CountAsync());
        var spouses = await new SpouseOrderStorageService(verify).CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
        Assert.False(spouses.NeedsReview); Assert.Equal(new[] { f.Second.Id, f.First.Id }, spouses.SavedOrder);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Adding_root_sibling_preserves_unaffected_child_and_spouse_orders()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var setup = db.CreateContext()) { var s = new SpouseOrderStorageService(setup); var capture = await s.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await s.SaveAsync(capture, new[] { f.First.Id, f.Second.Id }); }
        async Task AddChild(string name)
        {
            await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
            var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
            draft.Values = draft.Values with { Name = name }; await draft.SaveAsync(command);
        }
        await AddChild("长子"); await AddChild("次子");
        await using (var context = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(context); var sibling = new WorkbenchPersonDraft(await command.BeginOrdinarySiblingForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
            sibling.Values = sibling.Values with { Name = "父亲的妹妹", Sex = PersonSex.Female }; await sibling.SaveAsync(command);
        }
        await using var verify = db.CreateContext(); var spouseOrder = await new SpouseOrderStorageService(verify).CaptureAsync(f.G.Id, f.Chart, f.Owner.Id);
        Assert.False(spouseOrder.NeedsReview); Assert.Equal(new[] { f.First.Id, f.Second.Id }, spouseOrder.SavedOrder);
        var next = await new WorkbenchPersonProfileCommand(verify).BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart);
        Assert.Equal(2, next.ChartChildOrder!.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Chart_independent_person_appends_without_relationship_and_rolls_back_on_failure()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var root = new Person { GenealogyId = g.Id, Name = "原人物", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart;
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.Add(root); await seed.SaveChangesAsync();
            var graph = await new GenealogyDataService(seed).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(seed).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        await using var left = db.CreateContext(); await using var right = db.CreateContext();
        var writer = new WorkbenchPersonProfileCommand(left); var otherWriter = new WorkbenchPersonProfileCommand(right);
        var first = new WorkbenchPersonDraft(await writer.BeginNewAsync(g.Id)); var stale = new WorkbenchPersonDraft(await otherWriter.BeginNewAsync(g.Id));
        var revision = (await left.LineageCharts.AsNoTracking().SingleAsync(c => c.Id == chart)).Revision;
        first.AttachIndependentChart(chart, revision); stale.AttachIndependentChart(chart, revision);
        first.Values = first.Values with { Name = "新人物", Sex = PersonSex.Female, RecordedGeneration = "1" };
        stale.Values = stale.Values with { Name = "过期人物", RecordedGeneration = "1" };
        await left.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectIndependentOrder BEFORE INSERT ON LineageRootSiblingOrderItems BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => first.SaveAsync(writer)); Assert.True(first.IsDirty);
        await using (var check = db.CreateContext())
        {
            Assert.False(await check.People.AnyAsync(p => p.Id == first.Original.PersonId));
            Assert.Equal(revision, (await check.LineageCharts.SingleAsync(c => c.Id == chart)).Revision);
            Assert.Empty(await check.PersonRelationships.ToListAsync());
        }
        await left.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectIndependentOrder"); await first.SaveAsync(writer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.SaveAsync(otherWriter)); Assert.True(stale.IsDirty);
        await using var verify = db.CreateContext();
        var group = await verify.LineageRootSiblingOrders.Include(o => o.Items).SingleAsync(o => o.ChartId == chart);
        Assert.Equal(new[] { root.Id, first.Original.PersonId }, group.Items.OrderBy(i => i.Position).Select(i => i.PersonId));
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Independent_person_generation_change_rolls_back_order_and_relationships()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var root = new Person { GenealogyId = g.Id, Name = "首节点", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart;
        await using (var setup = db.CreateContext())
        {
            setup.Genealogies.Add(g); setup.People.Add(root); await setup.SaveChangesAsync();
            var graph = await new GenealogyDataService(setup).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(setup).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        Guid added;
        await using (var setup = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(setup);
            var draft = new WorkbenchPersonDraft(await command.BeginOrdinarySiblingForChartAsync(g.Id, root.Id, chart));
            draft.Values = draft.Values with { Name = "同世人物" }; added = draft.Original.PersonId; await draft.SaveAsync(command);
        }
        await using var context = db.CreateContext(); var writer = new WorkbenchPersonProfileCommand(context);
        var edit = new WorkbenchPersonDraft(await writer.ReadAsync(g.Id, added));
        var revision = (await context.LineageCharts.AsNoTracking().SingleAsync(c => c.Id == chart)).Revision;
        edit.AttachChart(chart, revision); edit.Values = edit.Values with { RecordedGeneration = "2" };
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectMovedOrder BEFORE INSERT ON LineageRootSiblingOrderItems BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => edit.SaveAsync(writer)); Assert.True(edit.IsDirty);
        await using (var check = db.CreateContext())
        {
            Assert.Equal("1", (await check.People.SingleAsync(p => p.Id == added)).RecordedGeneration);
            Assert.Equal(revision, (await check.LineageCharts.SingleAsync(c => c.Id == chart)).Revision);
            Assert.Equal(2, (await check.LineageRootSiblingOrders.Include(g => g.Items).SingleAsync()).Items.Count);
            Assert.Empty(await check.PersonRelationships.ToListAsync());
        }
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectMovedOrder");
        await edit.SaveAsync(writer);
        await using var verify = db.CreateContext();
        Assert.Equal("2", (await verify.People.SingleAsync(p => p.Id == added)).RecordedGeneration);
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        Assert.Single((await verify.LineageRootSiblingOrders.Include(g => g.Items).SingleAsync()).Items);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Parentless_person_generation_change_preserves_children_and_chart_order()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var root = new Person { GenealogyId = g.Id, Name = "父亲", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart;
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.Add(root); await seed.SaveChangesAsync();
            var graph = await new GenealogyDataService(seed).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(seed).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        var ids = new List<Guid>();
        foreach (var name in new[] { "长子", "次子" })
        {
            await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
            var child = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(g.Id, root.Id, chart));
            child.Values = child.Values with { Name = name, RecordedGeneration = "3" }; ids.Add(child.Original.PersonId); await child.SaveAsync(command);
        }
        Guid[] before;
        await using (var context = db.CreateContext()) before = await context.PersonRelationships.OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync();
        await using (var context = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(context); var edit = new WorkbenchPersonDraft(await command.ReadAsync(g.Id, root.Id));
            edit.AttachChart(chart, (await context.LineageCharts.AsNoTracking().SingleAsync(c => c.Id == chart)).Revision);
            edit.Values = edit.Values with { RecordedGeneration = "2" };
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectDescendantShift BEFORE UPDATE ON People WHEN OLD.Name='次子' BEGIN SELECT RAISE(ABORT, 'injected'); END");
            await Assert.ThrowsAnyAsync<Exception>(() => edit.SaveAsync(command)); Assert.True(edit.IsDirty);
            await using (var unchanged = db.CreateContext())
            {
                Assert.Equal("1", (await unchanged.People.SingleAsync(p => p.Id == root.Id)).RecordedGeneration);
                Assert.All(await unchanged.People.Where(p => ids.Contains(p.Id)).ToListAsync(), p => Assert.Equal("3", p.RecordedGeneration));
                Assert.Equal(before, await unchanged.PersonRelationships.OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync());
            }
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectDescendantShift"); await edit.SaveAsync(command);
        }
        await using var verify = db.CreateContext();
        Assert.Equal(before, await verify.PersonRelationships.OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync());
        Assert.Equal("2", (await verify.People.SingleAsync(p => p.Id == root.Id)).RecordedGeneration);
        Assert.All(await verify.People.Where(p => ids.Contains(p.Id)).ToListAsync(), p => Assert.Equal("4", p.RecordedGeneration));
        var group = await verify.LineageSiblingOrders.Include(o => o.Items).SingleAsync(o => o.ChartId == chart && o.ParentId == root.Id);
        Assert.Equal("2", group.ParentGeneration); Assert.Equal(ids, group.Items.OrderBy(i => i.Position).Select(i => i.PersonId));
        var graphAfter = await new GenealogyDataService(verify).GetGraphSnapshotAsync(g.Id);
        Assert.NotNull(graphAfter);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Root_sibling_insert_rolls_back_and_rejects_stale_drafts()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var root = new Person { GenealogyId = g.Id, Name = "长兄", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart;
        await using (var setup = db.CreateContext())
        {
            setup.Genealogies.Add(g); setup.People.Add(root); await setup.SaveChangesAsync();
            var graph = await new GenealogyDataService(setup).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(setup).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        await using var left = db.CreateContext(); await using var right = db.CreateContext();
        var firstCommand = new WorkbenchPersonProfileCommand(left); var secondCommand = new WorkbenchPersonProfileCommand(right);
        var first = new WorkbenchPersonDraft(await firstCommand.BeginOrdinarySiblingForChartAsync(g.Id, root.Id, chart));
        var stale = new WorkbenchPersonDraft(await secondCommand.BeginOrdinarySiblingForChartAsync(g.Id, root.Id, chart));
        first.Values = first.Values with { Name = "弟弟" }; stale.Values = stale.Values with { Name = "妹妹", Sex = PersonSex.Female };
        await left.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectRootOrder BEFORE INSERT ON LineageRootSiblingOrderItems BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => first.SaveAsync(firstCommand)); Assert.True(first.IsDirty);
        await using (var check = db.CreateContext())
        {
            Assert.False(await check.People.AnyAsync(p => p.Id == first.Original.PersonId));
            Assert.False(await check.PersonRelationships.AnyAsync(r => r.PersonId == first.Original.PersonId));
            Assert.Empty(await check.LineageRootSiblingOrders.ToListAsync());
            Assert.Equal(first.Original.ChartRevision, (await check.LineageCharts.SingleAsync(c => c.Id == chart)).Revision);
        }
        await left.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectRootOrder"); await first.SaveAsync(firstCommand);
        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.SaveAsync(secondCommand)); Assert.True(stale.IsDirty);
        await using var verify = db.CreateContext(); Assert.False(await verify.People.AnyAsync(p => p.Id == stale.Original.PersonId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Root_siblings_append_and_reorder_per_chart_without_fabricated_parents()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var genealogy = new Genealogy { Name = "虚构谱" }; var root = new Person { GenealogyId = genealogy.Id, Name = "长兄", Sex = PersonSex.Male, RecordedGeneration = "1" }; Guid chart;
        await using (var setup = db.CreateContext())
        {
            setup.Genealogies.Add(genealogy); setup.People.Add(root); await setup.SaveChangesAsync();
            var graph = await new GenealogyDataService(setup).GetGraphSnapshotAsync(genealogy.Id);
            chart = (await new LineageChartService(setup).SaveAsync(genealogy.Id, null, "图表", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        async Task<Guid> Add(string name, PersonSex sex)
        {
            await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
            var draft = new WorkbenchPersonDraft(await command.BeginOrdinarySiblingForChartAsync(genealogy.Id, root.Id, chart));
            draft.Values = draft.Values with { Name = name, Sex = sex }; await draft.SaveAsync(command); return draft.Original.PersonId;
        }
        var girl = await Add("妹妹", PersonSex.Female); var boy = await Add("弟弟", PersonSex.Male);
        await using (var context = db.CreateContext())
        {
            var service = new RootSiblingOrderService(context); var capture = await service.CaptureAsync(genealogy.Id, chart, root.Id);
            Assert.Equal(new[] { root.Id, boy, girl }, capture.Members.Select(m => m.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(capture, new[] { girl, root.Id, boy }));
            await service.SaveAsync(capture, new[] { boy, root.Id, girl });
        }
        await using var verify = db.CreateContext(); var reopened = await new RootSiblingOrderService(verify).CaptureAsync(genealogy.Id, chart, root.Id);
        Assert.Equal(new[] { boy, root.Id, girl }, reopened.Members.Select(m => m.Id));
        Assert.False(await verify.PersonRelationships.AnyAsync(r => (r.PersonId == root.Id || r.PersonId == boy || r.PersonId == girl) &&
            (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent)));
        var currentGraph = await new GenealogyDataService(verify).GetGraphSnapshotAsync(genealogy.Id);
        var otherChart = (await new LineageChartService(verify).SaveAsync(genealogy.Id, null, "第二张图", null, currentGraph, new Dictionary<Guid, Guid>())).Id;
        var otherService = new RootSiblingOrderService(verify); var other = await otherService.CaptureAsync(genealogy.Id, otherChart, root.Id);
        Assert.False(other.HasSavedOrder);
        await otherService.SaveAsync(other, new[] { root.Id, boy, girl });
        Assert.Equal(new[] { boy, root.Id, girl }, (await new RootSiblingOrderService(verify).CaptureAsync(genealogy.Id, chart, root.Id)).Members.Select(m => m.Id));
        Assert.Equal(new[] { root.Id, boy, girl }, (await new RootSiblingOrderService(verify).CaptureAsync(genealogy.Id, otherChart, root.Id)).Members.Select(m => m.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Concurrent_chart_adoption_and_sibling_drafts_reject_stale_save()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var setup = db.CreateContext()) { var service = new SpouseOrderStorageService(setup); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await service.SaveAsync(capture, new[] { f.First.Id, f.Second.Id }); }
        Guid older;
        await using (var setup = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(setup); var first = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
            first.Values = first.Values with { Name = "已有子女" }; await first.SaveAsync(command); older = first.Original.PersonId;
        }
        await using var left = db.CreateContext(); await using var right = db.CreateContext();
        var firstCommand = new WorkbenchPersonProfileCommand(left); var secondCommand = new WorkbenchPersonProfileCommand(right);
        var adoption = new WorkbenchPersonDraft(await firstCommand.BeginAdoptedChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
        var sibling = new WorkbenchPersonDraft(await secondCommand.BeginOrdinarySiblingForChartAsync(f.G.Id, older, f.Chart));
        adoption.Values = adoption.Values with { Name = "收养子女" }; sibling.Values = sibling.Values with { Name = "并发兄妹" };
        await adoption.SaveAsync(firstCommand);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sibling.SaveAsync(secondCommand)); Assert.True(sibling.IsDirty);
        await using var verify = db.CreateContext();
        Assert.False(await verify.People.AnyAsync(p => p.Id == sibling.Original.PersonId));
        Assert.False(await verify.PersonRelationships.AnyAsync(r => r.PersonId == sibling.Original.PersonId));
        Assert.False(await verify.LineageChartChoices.AnyAsync(c => c.PersonId == sibling.Original.PersonId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Chart_sibling_inherits_parents_and_atomically_appends_to_selected_parent_order()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var setup = db.CreateContext()) { var service = new SpouseOrderStorageService(setup); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await service.SaveAsync(capture, new[] { f.First.Id, f.Second.Id }); }
        Guid older;
        await using (var context = db.CreateContext())
        {
            var writer = new WorkbenchPersonProfileCommand(context); var draft = new WorkbenchPersonDraft(await writer.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
            draft.Values = draft.Values with { Name = "长女", Sex = PersonSex.Female }; await draft.SaveAsync(writer); older = draft.Original.PersonId;
        }
        await using var next = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(next);
        var sibling = new WorkbenchPersonDraft(await command.BeginOrdinarySiblingForChartAsync(f.G.Id, older, f.Chart));
        sibling.Values = sibling.Values with { Name = "弟弟", Sex = PersonSex.Male };
        await next.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectSiblingOrder BEFORE INSERT ON LineageSiblingOrderItems BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => sibling.SaveAsync(command)); Assert.True(sibling.IsDirty);
        await using (var verify = db.CreateContext())
        {
            Assert.False(await verify.People.AnyAsync(p => p.Id == sibling.Original.PersonId));
            Assert.False(await verify.PersonRelationships.AnyAsync(r => r.PersonId == sibling.Original.PersonId));
            Assert.Equal(sibling.Original.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.Chart)).Revision);
        }
        await next.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectSiblingOrder"); await sibling.SaveAsync(command);
        await using var final = db.CreateContext();
        var links = await final.PersonRelationships.Where(r => r.PersonId == sibling.Original.PersonId).ToListAsync();
        Assert.Equal(2, links.Count(r => r.Kind == PersonRelationshipKind.BiologicalParent));
        Assert.Contains(links, r => r.Kind == PersonRelationshipKind.Sibling && r.RelatedPersonId == older);
        var group = await final.LineageSiblingOrders.Include(g => g.Items).SingleAsync(g => g.ChartId == f.Chart && g.ParentId == f.Owner.Id);
        Assert.Equal(new[] { sibling.Original.PersonId, older }, group.Items.OrderBy(i => i.Position).Select(i => i.PersonId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Adopted_chart_child_is_grouped_and_atomic_when_order_write_fails()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var setup = db.CreateContext()) { var s = new SpouseOrderStorageService(setup); var capture = await s.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await s.SaveAsync(capture, new[] { f.First.Id, f.Second.Id }); }
        await using (var first = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(first); var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
            draft.Values = draft.Values with { Name = "女儿", Sex = PersonSex.Female }; await draft.SaveAsync(command);
        }
        await using var context = db.CreateContext(); var writer = new WorkbenchPersonProfileCommand(context);
        var adopted = new WorkbenchPersonDraft(await writer.BeginAdoptedChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
        adopted.Values = adopted.Values with { Name = "养子", Sex = PersonSex.Male };
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectAdoptedOrder BEFORE INSERT ON LineageSiblingOrderItems BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => adopted.SaveAsync(writer)); Assert.True(adopted.IsDirty);
        await using (var verify = db.CreateContext())
        {
            Assert.False(await verify.People.AnyAsync(p => p.Id == adopted.Original.PersonId));
            Assert.False(await verify.PersonRelationships.AnyAsync(r => r.PersonId == adopted.Original.PersonId));
            Assert.False(await verify.LineageChartChoices.AnyAsync(c => c.PersonId == adopted.Original.PersonId));
            Assert.Equal(adopted.Original.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.Chart)).Revision);
        }
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectAdoptedOrder"); await adopted.SaveAsync(writer);
        await using var final = db.CreateContext();
        var relationship = await final.PersonRelationships.SingleAsync(r => r.PersonId == adopted.Original.PersonId);
        Assert.Equal(PersonRelationshipKind.AdoptiveParent, relationship.Kind);
        var group = await final.LineageSiblingOrders.Include(g => g.Items).SingleAsync(g => g.ChartId == f.Chart && g.ParentId == f.Owner.Id);
        Assert.Equal(adopted.Original.PersonId, group.Items.OrderBy(i => i.Position).First().PersonId);
        Assert.Equal(relationship.Id, (await final.LineageChartChoices.SingleAsync(c => c.PersonId == adopted.Original.PersonId)).RelationshipId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Chart_child_order_failure_rolls_back_person_relationships_choice_and_revisions()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var setup = db.CreateContext()) { var s = new SpouseOrderStorageService(setup); var capture = await s.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await s.SaveAsync(capture, new[] { f.First.Id, f.Second.Id }); }
        await using (var first = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(first); var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
            draft.Values = draft.Values with { Name = "首子" }; await draft.SaveAsync(command);
        }
        await using var context = db.CreateContext(); var writer = new WorkbenchPersonProfileCommand(context);
        var second = new WorkbenchPersonDraft(await writer.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart)); second.Values = second.Values with { Name = "次子" };
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectChildOrder BEFORE INSERT ON LineageSiblingOrderItems BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => second.SaveAsync(writer)); Assert.True(second.IsDirty);
        await using (var verify = db.CreateContext())
        {
            Assert.False(await verify.People.AnyAsync(p => p.Id == second.Original.PersonId));
            Assert.False(await verify.PersonRelationships.AnyAsync(r => r.PersonId == second.Original.PersonId));
            Assert.False(await verify.LineageChartChoices.AnyAsync(c => c.PersonId == second.Original.PersonId));
            Assert.Equal(second.Original.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.Chart)).Revision);
            Assert.Empty(await verify.LineageSiblingOrders.ToListAsync());
        }
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectChildOrder"); await second.SaveAsync(writer);
        await using var final = db.CreateContext(); Assert.True(await final.People.AnyAsync(p => p.Id == second.Original.PersonId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Chart_child_with_invalid_generation_fails_without_creating_any_records()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var setup = db.CreateContext()) { var service = new SpouseOrderStorageService(setup); var capture = await service.CaptureAsync(f.G.Id, f.Chart, f.Owner.Id); await service.SaveAsync(capture, new[] { f.First.Id, f.Second.Id }); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
        draft.Values = draft.Values with { Name = "冲突子女", RecordedGeneration = "1" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using var verify = db.CreateContext();
        Assert.False(await verify.People.AnyAsync(p => p.Id == draft.Original.PersonId));
        Assert.Empty(await verify.PersonRelationships.Where(r => r.PersonId == draft.Original.PersonId).ToListAsync());
        Assert.Empty(await verify.LineageChartChoices.Where(c => c.PersonId == draft.Original.PersonId).ToListAsync());
        Assert.Equal(draft.Original.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.Chart)).Revision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Adding_spouse_does_not_mark_previously_stale_chart_choices_as_current()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var changed = db.CreateContext())
        {
            await changed.FamilyUnions.Where(u => u.Id == f.First.Id || u.Id == f.Second.Id).ExecuteDeleteAsync();
        }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var oldChartGeneration = (await context.LineageCharts.AsNoTracking().SingleAsync(c => c.Id == f.Chart)).Generation;
        var draft = new WorkbenchPersonDraft(await command.BeginNewSpouseForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
        Assert.NotEqual(oldChartGeneration, draft.Original.Generation);
        draft.Values = draft.Values with { Name = "新配偶" }; await draft.SaveAsync(command);
        await using var verify = db.CreateContext();
        Assert.Equal(oldChartGeneration, (await verify.LineageCharts.SingleAsync(c => c.Id == f.Chart)).Generation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkbenchPersonProfileCommand(verify).BeginOrdinaryChildForChartAsync(f.G.Id, f.Owner.Id, f.Chart));
    }
}
