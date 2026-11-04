using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace PuJiang.Tests;

public class AncestryPathReaderTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Path_preserves_ambiguity_until_explicit_choice_and_does_not_write()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        Guid gid, rootId, parentId, childId, linkId;
        await using (var seed = db.CreateContext())
        {
            var g = new Genealogy { Name = "虚构谱" }; var root = new Person { GenealogyId = g.Id, Name = "祖" };
            var parent = new Person { GenealogyId = g.Id, Name = "父" }; var other = new Person { GenealogyId = g.Id, Name = "养父" };
            var child = new Person { GenealogyId = g.Id, Name = "子" };
            var link = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent };
            seed.Genealogies.Add(g); seed.People.AddRange(root, parent, other, child);
            seed.PersonRelationships.AddRange(link, new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = other.Id, Kind = PersonRelationshipKind.AdoptiveParent },
                new PersonRelationship { GenealogyId = g.Id, PersonId = parent.Id, RelatedPersonId = root.Id, Kind = PersonRelationshipKind.BiologicalParent });
            await seed.SaveChangesAsync(); (gid, rootId, parentId, childId, linkId) = (g.Id, root.Id, parent.Id, child.Id, link.Id);
        }
        var coordinator = new TargetContextCoordinator(new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()).Options);
        await using var session = await ProjectionReadSession.OpenAsync(coordinator, gid); var token = session.Token;
        var pending = await AncestryPathReader.ReadAsync(session, token, childId, new HashSet<Guid> { rootId }, null, new Dictionary<Guid, Guid>());
        Assert.Equal(AncestryPathReader.Boundary.ChoiceRequired, pending.Status); Assert.Equal(2, pending.Candidates.Count); Assert.Empty(pending.Steps);
        var selected = await AncestryPathReader.ReadAsync(session, token, childId, new HashSet<Guid> { rootId }, null, new Dictionary<Guid, Guid> { [childId] = linkId });
        Assert.Equal(AncestryPathReader.Boundary.Connected, selected.Status); Assert.Equal(rootId, selected.AtPerson);
        Assert.Equal(new[] { childId, parentId }, selected.Steps.Select(x => x.Person.Id));
        var limited = await AncestryPathReader.ReadAsync(session, token, childId, new HashSet<Guid> { rootId }, null, new Dictionary<Guid, Guid>(), maxCandidates: 1);
        Assert.Equal(AncestryPathReader.Boundary.LimitReached, limited.Status);
        await using var verify = db.CreateContext(); Assert.Equal(3, await verify.PersonRelationships.CountAsync()); Assert.Equal(4, await verify.People.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Missing_parent_and_changed_version_never_return_connected_path()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); Guid gid, personId;
        await using (var seed = db.CreateContext()) { var g = new Genealogy { Name = "谱" }; var p = new Person { GenealogyId = g.Id, Name = "未知上系" }; seed.Genealogies.Add(g); seed.People.Add(p); await seed.SaveChangesAsync(); (gid, personId) = (g.Id, p.Id); }
        var coordinator = new TargetContextCoordinator(new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()).Options);
        await using var session = await ProjectionReadSession.OpenAsync(coordinator, gid); var token = session.Token;
        var result = await AncestryPathReader.ReadAsync(session, token, personId, new HashSet<Guid>(), null, new Dictionary<Guid, Guid>());
        Assert.Equal(AncestryPathReader.Boundary.MissingParent, result.Status);
        await using (var writer = db.CreateContext()) await writer.People.Where(p => p.Id == personId).ExecuteUpdateAsync(p => p.SetProperty(x => x.Name, "已改"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AncestryPathReader.ReadAsync(session, token, personId, new HashSet<Guid>(), null, new Dictionary<Guid, Guid>()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Chart_choice_is_applied_when_current_and_historical_choice_is_reported_when_last_parent_was_removed()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        Guid genealogyId, rootId, childId, relationId, chartId;
        await using (var seed = db.CreateContext())
        {
            var g = new Genealogy { Name = "谱" }; var root = new Person { GenealogyId = g.Id, Name = "父" }; var child = new Person { GenealogyId = g.Id, Name = "子" };
            var relation = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = root.Id, Kind = PersonRelationshipKind.BiologicalParent };
            seed.Genealogies.Add(g); seed.People.AddRange(root, child); seed.PersonRelationships.Add(relation); await seed.SaveChangesAsync();
            var snapshot = await new GenealogyDataService(seed).GetGraphSnapshotAsync(g.Id);
            var chart = await new LineageChartService(seed).SaveAsync(g.Id, null, "图表", null, snapshot, new Dictionary<Guid, Guid> { [child.Id] = relation.Id });
            (genealogyId, rootId, childId, relationId, chartId) = (g.Id, root.Id, child.Id, relation.Id, chart.Id);
        }
        var coordinator = new TargetContextCoordinator(new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()).Options);
        await using (var session = await ProjectionReadSession.OpenAsync(coordinator, genealogyId))
        {
            var result = await AncestryPathReader.ReadAsync(session, session.Token, childId, new HashSet<Guid> { rootId }, chartId, new Dictionary<Guid, Guid>());
            Assert.Equal(AncestryPathReader.Boundary.Connected, result.Status); Assert.Equal(relationId, Assert.Single(result.Steps).Parent.RelationshipId);
        }
        await using (var writer = db.CreateContext()) await writer.PersonRelationships.Where(r => r.Id == relationId).ExecuteDeleteAsync();
        await using var refreshed = await ProjectionReadSession.OpenAsync(coordinator, genealogyId);
        var stale = await AncestryPathReader.ReadAsync(refreshed, refreshed.Token, childId, new HashSet<Guid> { rootId }, chartId, new Dictionary<Guid, Guid>());
        Assert.Equal(AncestryPathReader.Boundary.ChartNeedsReview, stale.Status); Assert.Empty(stale.Candidates);
        Assert.NotNull(stale.ChartChoice); Assert.Equal(relationId, stale.ChartChoice.RelationshipId); Assert.Equal("父", stale.ChartChoice.ParentName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Parent_candidates_are_fully_read_across_page_before_deciding_and_limit_is_not_unique()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); Guid genealogyId, childId, chosenId, chosenParentId;
        await using (var seed = db.CreateContext())
        {
            var g = new Genealogy { Name = "跨页谱" }; var child = new Person { GenealogyId = g.Id, Name = "子" };
            seed.Genealogies.Add(g); seed.People.Add(child);
            var parents = Enumerable.Range(0, 201).Select(i => new Person { GenealogyId = g.Id, Name = $"父候选{i}" }).ToArray();
            var links = parents.Select(p => new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = p.Id, Kind = PersonRelationshipKind.BiologicalParent }).ToArray();
            seed.People.AddRange(parents); seed.PersonRelationships.AddRange(links); await seed.SaveChangesAsync();
            (genealogyId, childId, chosenId, chosenParentId) = (g.Id, child.Id, links[200].Id, parents[200].Id);
        }
        var coordinator = new TargetContextCoordinator(new DbContextOptionsBuilder<ZupuDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()).Options);
        await using var session = await ProjectionReadSession.OpenAsync(coordinator, genealogyId); var token = session.Token;
        var limited = await AncestryPathReader.ReadAsync(session, token, childId, new HashSet<Guid> { chosenParentId }, null, new Dictionary<Guid, Guid>(), maxCandidates: 200);
        Assert.Equal(AncestryPathReader.Boundary.LimitReached, limited.Status); Assert.Equal(200, limited.Candidates.Count);
        var pending = await AncestryPathReader.ReadAsync(session, token, childId, new HashSet<Guid> { chosenParentId }, null, new Dictionary<Guid, Guid>());
        Assert.Equal(AncestryPathReader.Boundary.ChoiceRequired, pending.Status); Assert.Equal(201, pending.Candidates.Count);
        var selected = await AncestryPathReader.ReadAsync(session, token, childId, new HashSet<Guid> { chosenParentId }, null, new Dictionary<Guid, Guid> { [childId] = chosenId });
        Assert.Equal(AncestryPathReader.Boundary.Connected, selected.Status); Assert.Equal(chosenId, Assert.Single(selected.Steps).Parent.RelationshipId);
    }
}
