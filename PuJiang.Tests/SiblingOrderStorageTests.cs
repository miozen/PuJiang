using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.IO.Compression;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class SiblingOrderStorageTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Explicit_chart_parent_selection_allows_two_parent_group_and_detects_stale_chart()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        var mother = new Person { GenealogyId = f.Genealogy.Id, Name = "母", Sex = PersonSex.Female, RecordedGeneration = "一世" };
        await using (var setup = db.CreateContext())
        {
            setup.People.Add(mother);
            setup.PersonRelationships.AddRange(new PersonRelationship { GenealogyId = f.Genealogy.Id, PersonId = f.A.Id, RelatedPersonId = mother.Id, Kind = PersonRelationshipKind.BiologicalParent },
                new PersonRelationship { GenealogyId = f.Genealogy.Id, PersonId = f.B.Id, RelatedPersonId = mother.Id, Kind = PersonRelationshipKind.BiologicalParent });
            await setup.SaveChangesAsync();
            await setup.People.Where(p => p.Id == f.A.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Sex, PersonSex.Male));
            await setup.People.Where(p => p.Id == f.B.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Sex, PersonSex.Female));
        }
        await using (var stale = await OpenAsync(db, f.Genealogy.Id))
            await Assert.ThrowsAsync<InvalidOperationException>(() => stale.CaptureSelectedAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id));
        await using (var update = db.CreateContext())
        {
            var snapshot = await new GenealogyDataService(update).GetGraphSnapshotAsync(f.Genealogy.Id);
            var revision = (await update.LineageCharts.SingleAsync(c => c.Id == f.ChartId)).Revision;
            await new LineageChartService(update).SaveAsync(f.Genealogy.Id, f.ChartId, "图一", revision, snapshot,
                new Dictionary<Guid, Guid> { [f.A.Id] = f.LinkA.Id });
        }
        await using (var missing = await OpenAsync(db, f.Genealogy.Id))
            await Assert.ThrowsAsync<InvalidOperationException>(() => missing.CaptureSelectedAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id));
        await using (var update = db.CreateContext())
        {
            var snapshot = await new GenealogyDataService(update).GetGraphSnapshotAsync(f.Genealogy.Id);
            var revision = (await update.LineageCharts.SingleAsync(c => c.Id == f.ChartId)).Revision;
            await new LineageChartService(update).SaveAsync(f.Genealogy.Id, f.ChartId, "图一", revision, snapshot,
                new Dictionary<Guid, Guid> { [f.A.Id] = f.LinkA.Id, [f.B.Id] = f.LinkB.Id });
        }
        Guid group;
        await using (var session = await OpenAsync(db, f.Genealogy.Id))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id));
            var capture = await session.CaptureSelectedAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
            group = await session.SaveGroupedAsync(capture, new[] { f.A.Id, f.B.Id });
        }
        await using var reloaded = await OpenAsync(db, f.Genealogy.Id);
        var review = await reloaded.ReviewAsync(f.Genealogy.Id, f.ChartId, group);
        Assert.True(review.CanApplyAtCapturedRevision); await reloaded.ValidateReviewAsync(review);
        Assert.Equal(new[] { f.A.Id, f.B.Id }, review.Historical.Select(item => item.Member.PersonId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Selected_group_rejects_when_only_one_child_is_assigned_to_that_parent()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        var mother = new Person { GenealogyId = f.Genealogy.Id, Name = "母", RecordedGeneration = "一世" };
        var motherA = new PersonRelationship { GenealogyId = f.Genealogy.Id, PersonId = f.A.Id, RelatedPersonId = mother.Id, Kind = PersonRelationshipKind.BiologicalParent };
        var motherB = new PersonRelationship { GenealogyId = f.Genealogy.Id, PersonId = f.B.Id, RelatedPersonId = mother.Id, Kind = PersonRelationshipKind.BiologicalParent };
        await using (var setup = db.CreateContext()) { setup.People.Add(mother); setup.PersonRelationships.AddRange(motherA, motherB); await setup.SaveChangesAsync(); }
        await using (var update = db.CreateContext())
        {
            var snapshot = await new GenealogyDataService(update).GetGraphSnapshotAsync(f.Genealogy.Id);
            var revision = (await update.LineageCharts.SingleAsync(c => c.Id == f.ChartId)).Revision;
            await new LineageChartService(update).SaveAsync(f.Genealogy.Id, f.ChartId, "图一", revision, snapshot,
                new Dictionary<Guid, Guid> { [f.A.Id] = f.LinkA.Id, [f.B.Id] = motherB.Id });
        }
        await using var session = await OpenAsync(db, f.Genealogy.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.CaptureSelectedAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id));
        await using var verify = db.CreateContext(); Assert.Empty(await verify.LineageSiblingOrders.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Grouped_save_rejects_unknown_and_cross_sex_order_without_writing_then_persists_per_chart()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var seed = db.CreateContext())
        {
            await seed.People.Where(p => p.Id == f.A.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Sex, PersonSex.Male));
        }
        await using (var unknown = await OpenAsync(db, f.Genealogy.Id))
        {
            var capture = await unknown.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => unknown.SaveGroupedAsync(capture, new[] { f.A.Id, f.B.Id }));
            await using var verify = db.CreateContext(); Assert.Empty(await verify.LineageSiblingOrders.ToListAsync());
            Assert.Equal(capture.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.ChartId)).Revision);
        }
        await using (var seed = db.CreateContext()) await seed.People.Where(p => p.Id == f.B.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Sex, PersonSex.Female));
        await using var session = await OpenAsync(db, f.Genealogy.Id);
        var next = await session.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveGroupedAsync(next, new[] { f.B.Id, f.A.Id }));
        await using (var verify = db.CreateContext()) { Assert.Empty(await verify.LineageSiblingOrders.ToListAsync()); Assert.Equal(next.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.ChartId)).Revision); }
        var group = await session.SaveGroupedAsync(next, new[] { f.A.Id, f.B.Id });
        await using var reopened = await OpenAsync(db, f.Genealogy.Id);
        Assert.Equal(new[] { f.A.Id, f.B.Id }, (await reopened.ReviewAsync(f.Genealogy.Id, f.ChartId, group)).Historical.Select(i => i.Member.PersonId));
        Assert.Empty(await reopened.ListAsync(f.Genealogy.Id, f.OtherChartId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Grouped_save_rejects_stale_chart_and_sex_change_without_partial_writes()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var seed = db.CreateContext())
        {
            await seed.People.Where(p => p.Id == f.A.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Sex, PersonSex.Male));
            await seed.People.Where(p => p.Id == f.B.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Sex, PersonSex.Female));
        }
        await using var first = await OpenAsync(db, f.Genealogy.Id);
        await using var second = await OpenAsync(db, f.Genealogy.Id);
        var one = await first.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
        var stale = await second.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
        var group = await first.SaveGroupedAsync(one, new[] { f.A.Id, f.B.Id });
        await Assert.ThrowsAnyAsync<Exception>(() => second.SaveGroupedAsync(stale, new[] { f.A.Id, f.B.Id }));
        await using var reopened = await OpenAsync(db, f.Genealogy.Id);
        var fresh = await reopened.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id, group);
        await using (var external = db.CreateContext()) await external.People.Where(p => p.Id == f.A.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Sex, PersonSex.Female));
        await Assert.ThrowsAnyAsync<Exception>(() => reopened.SaveGroupedAsync(fresh, new[] { f.A.Id, f.B.Id }));
        await using var verify = db.CreateContext();
        Assert.Equal(fresh.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.ChartId)).Revision);
        Assert.Equal(new[] { f.A.Id, f.B.Id }, await verify.LineageSiblingOrderItems.Where(i => i.GroupId == group).OrderBy(i => i.Position).Select(i => i.PersonId).ToArrayAsync());
    }

    private sealed record Fixture(Genealogy Genealogy, Person Parent, Person A, Person B, PersonRelationship LinkA, PersonRelationship LinkB, Guid ChartId, Guid OtherChartId);
    private static Task<SiblingOrderStorageSession> OpenAsync(TestDatabase db, Guid genealogy)
    {
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()).Options;
        return SiblingOrderStorageSession.OpenAsync(new TargetContextCoordinator(options), genealogy);
    }
    private static async Task<Fixture> SeedAsync(TestDatabase db)
    {
        await using var context = db.CreateContext();
        var genealogy = new Genealogy { Name = "虚构谱" };
        var parent = new Person { GenealogyId = genealogy.Id, Name = "父", RecordedGeneration = "一世" };
        var a = new Person { GenealogyId = genealogy.Id, Name = "甲", RecordedGeneration = "二世", RecordedBirthOrder = "原文甲" };
        var b = new Person { GenealogyId = genealogy.Id, Name = "乙", RecordedGeneration = "二世", RecordedBirthOrder = "原文乙" };
        var ra = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = a.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent };
        var rb = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = b.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent };
        context.Genealogies.Add(genealogy); context.People.AddRange(parent, a, b); context.PersonRelationships.AddRange(ra, rb); await context.SaveChangesAsync();
        var snapshot = await new GenealogyDataService(context).GetGraphSnapshotAsync(genealogy.Id); var charts = new LineageChartService(context);
        var first = await charts.SaveAsync(genealogy.Id, null, "图一", null, snapshot, new Dictionary<Guid, Guid> { [a.Id] = ra.Id });
        var second = await charts.SaveAsync(genealogy.Id, null, "图二", null, snapshot, new Dictionary<Guid, Guid> { [b.Id] = rb.Id });
        return new(genealogy, parent, a, b, ra, rb, first.Id, second.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Independent_charts_cas_stale_review_and_existing_choices_are_preserved()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using var firstSession = await OpenAsync(db, f.Genealogy.Id);
        await using var competing = await OpenAsync(db, f.Genealogy.Id);
        var first = await firstSession.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
        var stale = await competing.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstSession.SaveAsync(first, new[] { f.A.Id, f.A.Id }));
        var group = await firstSession.SaveAsync(first, new[] { f.B.Id, f.A.Id });
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstSession.SaveAsync(first, new[] { f.B.Id, f.A.Id }));
        await Assert.ThrowsAnyAsync<Exception>(() => competing.SaveAsync(stale, new[] { f.A.Id, f.B.Id }));
        await using var reviewSession = await OpenAsync(db, f.Genealogy.Id);
        Assert.Equal(group, Assert.Single(await reviewSession.ListAsync(f.Genealogy.Id, f.ChartId)).GroupId);
        Assert.Empty(await reviewSession.ListAsync(f.Genealogy.Id, f.OtherChartId));
        var review = await reviewSession.ReviewAsync(f.Genealogy.Id, f.ChartId, group);
        Assert.True(review.CanApplyAtCapturedRevision); Assert.Equal(new[] { f.B.Id, f.A.Id }, review.Historical.Select(x => x.Member.PersonId));
        await reviewSession.ValidateReviewAsync(review);
        await using (var otherSession = await OpenAsync(db, f.Genealogy.Id))
        {
            var otherCapture = await otherSession.CaptureAsync(f.Genealogy.Id, f.OtherChartId, f.Parent.Id);
            Assert.NotEqual(group, await otherSession.SaveAsync(otherCapture, new[] { f.A.Id, f.B.Id }));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => reviewSession.ValidateReviewAsync(review));
        await using (var verify = db.CreateContext())
        {
            Assert.Equal(2, await verify.LineageChartChoices.CountAsync()); Assert.Equal(2, await verify.LineageSiblingOrders.CountAsync());
            Assert.Equal("原文甲", (await verify.People.SingleAsync(p => p.Id == f.A.Id)).RecordedBirthOrder);
        }
        await using (var other = db.CreateContext()) await other.People.Where(p => p.Id == f.B.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.RecordedBirthOrder, "新原文"));
        await using var reviewAfterChange = await OpenAsync(db, f.Genealogy.Id);
        var needsReview = await reviewAfterChange.ReviewAsync(f.Genealogy.Id, f.ChartId, group);
        Assert.False(needsReview.CanApplyAtCapturedRevision); Assert.Equal(2, needsReview.Historical.Count); Assert.Contains(needsReview.Problems, p => p.Contains("变化"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reviewAfterChange.ValidateReviewAsync(needsReview));
        var newCapture = await reviewAfterChange.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id, group);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reviewAfterChange.SaveAsync(newCapture, new[] { f.A.Id, f.B.Id }));
        await reviewAfterChange.SaveAsync(newCapture, new[] { f.A.Id, f.B.Id }, confirmChangedHistory: true);
        await using var final = db.CreateContext(); var chart = await final.LineageCharts.SingleAsync(c => c.Id == f.ChartId);
        var graph = await new GenealogyDataService(final).GetGraphSnapshotAsync(f.Genealogy.Id);
        Assert.True((await new LineageChartService(final).ReviewAsync(f.Genealogy.Id, chart.Id, graph)).GenerationChanged);
        Assert.Single(await final.LineageChartChoices.Where(c => c.ChartId == f.ChartId).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Item_failure_rolls_back_chart_revision_and_prior_order_and_can_retry()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        Guid group;
        await using (var first = await OpenAsync(db, f.Genealogy.Id))
        {
            var capture = await first.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
            group = await first.SaveAsync(capture, new[] { f.A.Id, f.B.Id });
        }
        await using var service = await OpenAsync(db, f.Genealogy.Id);
        var next = await service.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id, group);
        service.AfterItemForTest = n => n == 1 ? Task.FromException(new InvalidOperationException("injected")) : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(next, new[] { f.B.Id, f.A.Id }));
        await using (var verify = db.CreateContext())
        {
            Assert.Equal(next.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.ChartId)).Revision);
            Assert.Equal(new[] { f.A.Id, f.B.Id }, await verify.LineageSiblingOrderItems.Where(i => i.GroupId == group).OrderBy(i => i.Position).Select(i => i.PersonId).ToArrayAsync());
        }
        service.AfterItemForTest = null; await service.SaveAsync(next, new[] { f.B.Id, f.A.Id });
        await using var final = db.CreateContext();
        Assert.Equal(new[] { f.B.Id, f.A.Id }, await final.LineageSiblingOrderItems.Where(i => i.GroupId == group).OrderBy(i => i.Position).Select(i => i.PersonId).ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Deleted_or_added_members_preserve_history_and_do_not_automatically_apply()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db); Guid group;
        await using (var initial = await OpenAsync(db, f.Genealogy.Id))
        {
            var capture = await initial.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
            group = await initial.SaveAsync(capture, new[] { f.A.Id, f.B.Id });
        }
        await using (var other = db.CreateContext())
        {
            var child = new Person { GenealogyId = f.Genealogy.Id, Name = "丙", RecordedGeneration = "二世" }; other.People.Add(child);
            other.PersonRelationships.Add(new PersonRelationship { GenealogyId = f.Genealogy.Id, PersonId = child.Id, RelatedPersonId = f.Parent.Id, Kind = PersonRelationshipKind.BiologicalParent }); await other.SaveChangesAsync();
        }
        await using (var service = await OpenAsync(db, f.Genealogy.Id))
        {
            var review = await service.ReviewAsync(f.Genealogy.Id, f.ChartId, group);
            Assert.False(review.CanApplyAtCapturedRevision); Assert.Equal(2, review.Historical.Count); Assert.Equal(3, review.Current.Count);
        }
        await using (var other = db.CreateContext()) { await other.PersonRelationships.Where(r => r.Id == f.LinkB.Id).ExecuteDeleteAsync(); await other.People.Where(p => p.Id == f.B.Id).ExecuteDeleteAsync(); }
        await using var afterDelete = await OpenAsync(db, f.Genealogy.Id);
        var deleted = await afterDelete.ReviewAsync(f.Genealogy.Id, f.ChartId, group);
        Assert.False(deleted.CanApplyAtCapturedRevision); Assert.Contains(deleted.Historical, i => i.Member.PersonId == f.B.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => afterDelete.CaptureAsync(Guid.NewGuid(), f.ChartId, f.Parent.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Prior_database_migrates_preserving_charts_and_current_backup_preflight_succeeds()
    {
        await using var db = new TestDatabase();
        await using (var old = db.CreateContext()) await old.GetService<IMigrator>().MigrateAsync("20261103075225_AddLineageCharts");
        var f = await SeedAsync(db);
        Directory.CreateDirectory(db.BackupDirectory); var oldPackage = Path.Combine(db.BackupDirectory, "old-chart.zip");
        await OfflineBackupPackageService.CreateAsync(db.DatabasePath, oldPackage);
        await db.MigrateAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupRestorePreflightService(db.BackupDirectory).CheckAsync(oldPackage));
        await using (var verify = db.CreateContext())
        {
            Assert.Equal(2, await verify.LineageCharts.CountAsync()); Assert.Equal(2, await verify.LineageChartChoices.CountAsync());
            Assert.Empty(await verify.LineageSiblingOrders.ToListAsync());
        }
        await using (var ordering = await OpenAsync(db, f.Genealogy.Id))
        {
            var capture = await ordering.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
            await ordering.SaveAsync(capture, new[] { f.B.Id, f.A.Id });
        }
        Directory.CreateDirectory(db.BackupDirectory); var package = Path.Combine(db.BackupDirectory, "sibling-order.zip");
        await OfflineBackupPackageService.CreateAsync(db.DatabasePath, package);
        Assert.True((await new BackupRestorePreflightService(db.BackupDirectory).CheckAsync(package)).IncludesDatabase);
        var extracted = Path.Combine(db.BackupDirectory, "inspect.db");
        using (var archive = ZipFile.OpenRead(package)) archive.GetEntry("database/pujiang.db")!.ExtractToFile(extracted);
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={extracted};Pooling=False").Options;
        await using (var backedUp = new ZupuDbContext(options))
        {
            Assert.Single(await backedUp.LineageSiblingOrders.ToListAsync());
            Assert.Equal(new[] { f.B.Id, f.A.Id }, await backedUp.LineageSiblingOrderItems.OrderBy(i => i.Position).Select(i => i.PersonId).ToArrayAsync());
        }
        await using var current = db.CreateContext(); var snapshot = await new GenealogyDataService(current).GetGraphSnapshotAsync(f.Genealogy.Id);
        var document = GenealogyExchangeService.Export(snapshot);
        Assert.Equal(1, document.FormatVersion); Assert.Equal(2, document.Relationships.Count);
        Assert.DoesNotContain("LineageSiblingOrder", System.Text.Json.JsonSerializer.Serialize(document));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Old_chart_choice_save_preserves_order_and_invalidates_old_review()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db); Guid group;
        await using (var ordering = await OpenAsync(db, f.Genealogy.Id))
        {
            var capture = await ordering.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
            group = await ordering.SaveAsync(capture, new[] { f.B.Id, f.A.Id });
        }
        await using var reader = await OpenAsync(db, f.Genealogy.Id);
        var old = await reader.ReviewAsync(f.Genealogy.Id, f.ChartId, group); Assert.True(old.CanApplyAtCapturedRevision);
        await using (var context = db.CreateContext())
        {
            var graph = await new GenealogyDataService(context).GetGraphSnapshotAsync(f.Genealogy.Id);
            await new LineageChartService(context).SaveAsync(f.Genealogy.Id, f.ChartId, "更新选线", old.ChartRevision, graph,
                new Dictionary<Guid, Guid> { [f.B.Id] = f.LinkB.Id });
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ValidateReviewAsync(old));
        await using var final = db.CreateContext();
        Assert.Equal(new[] { f.B.Id, f.A.Id }, await final.LineageSiblingOrderItems.Where(i => i.GroupId == group).OrderBy(i => i.Position).Select(i => i.PersonId).ToArrayAsync());
        Assert.Equal(f.LinkB.Id, (await final.LineageChartChoices.SingleAsync(c => c.ChartId == f.ChartId)).RelationshipId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Over_one_hundred_children_are_all_required_and_not_silently_truncated()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var context = db.CreateContext())
        {
            for (var i = 0; i < 100; i++)
            {
                var child = new Person { GenealogyId = f.Genealogy.Id, Name = $"虚构{i}", RecordedGeneration = "二世" };
                context.People.Add(child); context.PersonRelationships.Add(new PersonRelationship { GenealogyId = f.Genealogy.Id, PersonId = child.Id, RelatedPersonId = f.Parent.Id, Kind = PersonRelationshipKind.BiologicalParent });
            }
            await context.SaveChangesAsync();
        }
        await using var session = await OpenAsync(db, f.Genealogy.Id);
        var capture = await session.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id); Assert.Equal(102, capture.Members.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveAsync(capture, capture.Members.Take(100).Select(m => m.PersonId).ToArray()));
        var id = await session.SaveAsync(capture, capture.Members.Select(m => m.PersonId).ToArray());
        await using var verify = db.CreateContext(); Assert.Equal(102, await verify.LineageSiblingOrderItems.CountAsync(i => i.GroupId == id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Concurrent_writers_cannot_both_commit_the_same_revision()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using var one = await OpenAsync(db, f.Genealogy.Id); await using var two = await OpenAsync(db, f.Genealogy.Id);
        var a = await one.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
        var b = await two.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
        async Task<bool> TrySave(SiblingOrderStorageSession session, SiblingOrderCapture capture, Guid[] order)
        {
            try { await session.SaveAsync(capture, order); return true; }
            catch (Exception ex) when (ex is InvalidOperationException or SqliteException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => TrySave(one, a, new[] { f.A.Id, f.B.Id })), Task.Run(() => TrySave(two, b, new[] { f.B.Id, f.A.Id })));
        Assert.Single(results, x => x);
        await using var verify = db.CreateContext(); Assert.Single(await verify.LineageSiblingOrders.ToListAsync());
        Assert.Equal(2, await verify.LineageSiblingOrderItems.CountAsync());
        Assert.Equal(2, (await verify.LineageCharts.SingleAsync(c => c.Id == f.ChartId)).Revision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Coordinator_drain_closes_session_and_rejects_old_capture()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()).Options;
        var coordinator = new TargetContextCoordinator(options);
        await using var session = await SiblingOrderStorageSession.OpenAsync(coordinator, f.Genealogy.Id);
        var capture = await session.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveAsync(capture, new[] { f.A.Id, f.B.Id }));
        await using var verify = db.CreateContext(); Assert.Empty(await verify.LineageSiblingOrders.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Saving_a_second_group_preserves_first_group_and_chart_choices()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db); Guid firstGroup;
        await using (var first = await OpenAsync(db, f.Genealogy.Id))
        {
            var capture = await first.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
            firstGroup = await first.SaveAsync(capture, new[] { f.B.Id, f.A.Id });
        }
        var parent = new Person { GenealogyId = f.Genealogy.Id, Name = "另一父", RecordedGeneration = "一世" };
        var a = new Person { GenealogyId = f.Genealogy.Id, Name = "另甲", RecordedGeneration = "二世" };
        var b = new Person { GenealogyId = f.Genealogy.Id, Name = "另乙", RecordedGeneration = "二世" };
        await using (var other = db.CreateContext())
        {
            other.People.AddRange(parent, a, b);
            other.PersonRelationships.AddRange(new PersonRelationship { GenealogyId = f.Genealogy.Id, PersonId = a.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent },
                new PersonRelationship { GenealogyId = f.Genealogy.Id, PersonId = b.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent });
            await other.SaveChangesAsync();
        }
        await using (var second = await OpenAsync(db, f.Genealogy.Id))
        {
            var capture = await second.CaptureAsync(f.Genealogy.Id, f.ChartId, parent.Id);
            await second.SaveAsync(capture, new[] { a.Id, b.Id });
        }
        await using var verify = db.CreateContext();
        Assert.Equal(2, await verify.LineageSiblingOrders.CountAsync(g => g.ChartId == f.ChartId));
        Assert.Equal(new[] { f.B.Id, f.A.Id }, await verify.LineageSiblingOrderItems.Where(i => i.GroupId == firstGroup).OrderBy(i => i.Position).Select(i => i.PersonId).ToArrayAsync());
        Assert.Single(await verify.LineageChartChoices.Where(c => c.ChartId == f.ChartId).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cross_genealogy_chart_is_rejected_even_when_both_genealogies_exist()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        var other = new Genealogy { Name = "另一虚构谱" };
        await using (var context = db.CreateContext()) { context.Genealogies.Add(other); await context.SaveChangesAsync(); }
        await using var session = await OpenAsync(db, other.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ListAsync(other.Id, f.ChartId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.CaptureAsync(other.Id, f.ChartId, f.Parent.Id));
        await using var verify = db.CreateContext(); Assert.Empty(await verify.LineageSiblingOrders.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Real_sqlite_insert_failure_rolls_back_and_reopen_can_retry()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using (var dbSetup = db.CreateContext())
            await dbSetup.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectOrderItem BEFORE INSERT ON LineageSiblingOrderItems WHEN NEW.Position=2 BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await using (var session = await OpenAsync(db, f.Genealogy.Id))
        {
            var capture = await session.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
            await Assert.ThrowsAnyAsync<Exception>(() => session.SaveAsync(capture, new[] { f.A.Id, f.B.Id }));
            await using var verify = db.CreateContext();
            Assert.Empty(await verify.LineageSiblingOrders.ToListAsync());
            Assert.Equal(capture.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.ChartId)).Revision);
        }
        await using (var dbSetup = db.CreateContext()) await dbSetup.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectOrderItem");
        await using var retry = await OpenAsync(db, f.Genealogy.Id);
        var fresh = await retry.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
        await retry.SaveAsync(fresh, new[] { f.A.Id, f.B.Id });
        await using var final = db.CreateContext(); Assert.Equal(2, await final.LineageSiblingOrderItems.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Aba_of_recorded_order_still_requires_review()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db); Guid group;
        await using (var session = await OpenAsync(db, f.Genealogy.Id))
        {
            var capture = await session.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
            group = await session.SaveAsync(capture, new[] { f.A.Id, f.B.Id });
        }
        await using (var other = db.CreateContext())
        {
            await other.People.Where(p => p.Id == f.A.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.RecordedBirthOrder, "临时"));
            await other.People.Where(p => p.Id == f.A.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.RecordedBirthOrder, "原文甲"));
        }
        await using var review = await OpenAsync(db, f.Genealogy.Id);
        var result = await review.ReviewAsync(f.Genealogy.Id, f.ChartId, group);
        Assert.False(result.CanApplyAtCapturedRevision); Assert.Equal("原文甲", result.Current.Single(x => x.PersonId == f.A.Id).RecordedBirthOrder);
        Assert.Contains(result.Problems, p => p.Contains("版本已变化"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => review.ValidateReviewAsync(result));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancel_before_commit_rolls_back_and_preserves_capture()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var f = await SeedAsync(db);
        await using var session = await OpenAsync(db, f.Genealogy.Id);
        var capture = await session.CaptureAsync(f.Genealogy.Id, f.ChartId, f.Parent.Id);
        using var cancellation = new CancellationTokenSource();
        session.AfterItemForTest = n => { if (n == 1) cancellation.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SaveAsync(capture, new[] { f.A.Id, f.B.Id }, cancellationToken: cancellation.Token));
        await using (var verify = db.CreateContext())
        {
            Assert.Empty(await verify.LineageSiblingOrders.ToListAsync());
            Assert.Equal(capture.ChartRevision, (await verify.LineageCharts.SingleAsync(c => c.Id == f.ChartId)).Revision);
        }
        session.AfterItemForTest = null;
        await session.SaveAsync(capture, new[] { f.A.Id, f.B.Id });
    }
}
