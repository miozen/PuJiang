using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.IO.Compression;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace PuJiang.Tests;

public class LineageChartTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Old_database_migrates_empty_and_old_backup_is_rejected_by_current_preflight()
    {
        await using var database = new TestDatabase();
        await using (var old = database.CreateContext())
            await old.GetService<IMigrator>().MigrateAsync("20261030000200_AddScopedUnlinkGenerationV1");
        Directory.CreateDirectory(database.BackupDirectory);
        var oldPackage = Path.Combine(database.BackupDirectory, "old.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, oldPackage);
        await database.MigrateAsync();
        await using (var current = database.CreateContext())
        {
            Assert.Empty(await current.LineageCharts.ToListAsync());
            Assert.Empty(await current.LineageChartChoices.ToListAsync());
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupRestorePreflightService(database.BackupDirectory).CheckAsync(oldPackage));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Charts_are_isolated_and_stale_choices_are_preserved_for_review()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var data = new GenealogyDataService(context);
        var genealogy = await data.CreateGenealogyAsync(new Genealogy { Name = "甲" });
        var other = await data.CreateGenealogyAsync(new Genealogy { Name = "乙" });
        var child = await data.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "子" });
        var parent = await data.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "父" });
        var bio = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent };
        var adopt = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.AdoptiveParent };
        await data.AddRelationshipAsync(bio);
        await data.AddRelationshipAsync(adopt);
        var snapshot = await data.GetGraphSnapshotAsync(genealogy.Id);
        var charts = new LineageChartService(context);
        var one = await charts.SaveAsync(genealogy.Id, null, "生", null, snapshot, new Dictionary<Guid, Guid> { [child.Id] = bio.Id });
        var two = await charts.SaveAsync(genealogy.Id, null, "养", null, snapshot, new Dictionary<Guid, Guid> { [child.Id] = adopt.Id });
        Assert.Equal(2, (await charts.ListAsync(genealogy.Id)).Count);
        Assert.Empty(await charts.ListAsync(other.Id));
        Assert.Equal(bio.Id, Assert.Single((await charts.ReviewAsync(genealogy.Id, one.Id, snapshot)).Choices).Choice.RelationshipId);
        Assert.Equal(adopt.Id, Assert.Single((await charts.ReviewAsync(genealogy.Id, two.Id, snapshot)).Choices).Choice.RelationshipId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => charts.ReviewAsync(other.Id, one.Id, snapshot));
        await charts.SaveAsync(genealogy.Id, one.Id, "生", one.Revision, snapshot, new Dictionary<Guid, Guid> { [child.Id] = bio.Id });
        await Assert.ThrowsAsync<InvalidOperationException>(() => charts.SaveAsync(genealogy.Id, one.Id, "覆盖", 1, snapshot, new Dictionary<Guid, Guid>()));
        await data.AddPersonAsync(new Person { GenealogyId = other.Id, Name = "无关修改" });
        var unrelated = await data.GetGraphSnapshotAsync(genealogy.Id);
        var unchanged = await charts.ReviewAsync(genealogy.Id, two.Id, unrelated);
        Assert.True(unchanged.GenerationChanged);
        Assert.Null(Assert.Single(unchanged.Choices).Problem);
        adopt.Kind = PersonRelationshipKind.HeirParent;
        await context.SaveChangesAsync();
        var typeChanged = await data.GetGraphSnapshotAsync(genealogy.Id);
        Assert.Contains("类型已改变", Assert.Single((await charts.ReviewAsync(genealogy.Id, two.Id, typeChanged)).Choices).Problem);
        context.PersonRelationships.Remove(bio);
        await context.SaveChangesAsync();
        var changed = await data.GetGraphSnapshotAsync(genealogy.Id);
        var review = await charts.ReviewAsync(genealogy.Id, one.Id, changed);
        Assert.True(review.GenerationChanged);
        Assert.Contains("不存在", Assert.Single(review.Choices).Problem);
        var session = new LineageProjectionSession(changed);
        session.Suppress(child.Id);
        Assert.Null(session.Project().Single(p => p.Person.Id == child.Id).SelectedParent);
        Assert.Single(await context.LineageChartChoices.AsNoTracking().Where(c => c.ChartId == one.Id).ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => charts.SaveAsync(genealogy.Id, two.Id, "旧", two.Revision, snapshot, new Dictionary<Guid, Guid>()));

        Directory.CreateDirectory(database.BackupDirectory);
        var package = Path.Combine(database.BackupDirectory, "chart.zip");
        await OfflineBackupPackageService.CreateAsync(database.DatabasePath, package);
        Assert.True((await new BackupRestorePreflightService(database.BackupDirectory).CheckAsync(package)).IncludesDatabase);
        var extracted = Path.Combine(database.BackupDirectory, "inspect.db");
        using (var archive = ZipFile.OpenRead(package))
            archive.GetEntry("database/pujiang.db")!.ExtractToFile(extracted);
        var options = new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite($"Data Source={extracted};Pooling=False").Options;
        await using var backedUp = new ZupuDbContext(options);
        Assert.Equal(2, await backedUp.LineageCharts.CountAsync());
        Assert.Equal(2, await backedUp.LineageChartChoices.CountAsync());
    }
}
