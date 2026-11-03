using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

public class LineageProjectionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Full_projection_keeps_every_person_and_requires_explicit_relation_choice()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var child = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "同名" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "同名" });
        var branch = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "另支" });
        var isolated = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "独立" });
        var biological = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id,
            Kind = PersonRelationshipKind.BiologicalParent, IsPreferredForLineage = true, SourceCitation = "原谱" };
        var adoptive = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id,
            Kind = PersonRelationshipKind.AdoptiveParent, Note = "养嗣依据" };
        await service.AddRelationshipAsync(biological);
        await service.AddRelationshipAsync(adoptive);
        await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = branch.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.HeirParent });
        var lost = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, Kind = PersonRelationshipKind.LostAncestry, SourceCitation = "失考原谱" };
        await service.AddRelationshipAsync(lost);
        var other = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var foreign = await service.AddPersonAsync(new Person { GenealogyId = other.Id, Name = "外谱" });
        var vm = new GenealogySelectionViewModel(database.CreateContext);
        await vm.LoadAsync();
        await vm.SelectAsync(genealogy);
        var preview = await vm.GetLineagePreviewAsync();
        var session = new LineageProjectionSession(preview.Snapshot);
        var rows = session.Project();
        Assert.Equal(4, rows.Count);
        Assert.DoesNotContain(rows, row => row.Person.Id == foreign.Id);
        var childRow = rows.Single(row => row.Person.Id == child.Id);
        Assert.True(childRow.NeedsChoice);
        Assert.Null(childRow.SelectedParent);
        Assert.Equal(2, childRow.ParentCandidates.Count);
        Assert.Contains(childRow.ParentCandidates, c => c.Relationship.Id == biological.Id && c.Relationship.SourceCitation == "原谱");
        Assert.Single(childRow.LostAncestry);
        Assert.Equal("失考原谱", childRow.LostAncestry.Single().SourceCitation);
        Assert.Equal(parent.Id, rows.Single(row => row.Person.Id == branch.Id).SelectedParent!.Parent.Id);
        Assert.Null(rows.Single(row => row.Person.Id == isolated.Id).SelectedParent);
        Assert.Contains("未记录", rows.Single(row => row.Person.Id == isolated.Id).Label);
        var tree = new LineageTreeState(new LineageProjectionSession(preview.Snapshot));
        Assert.Equal(4, tree.People.Count);
        Assert.Equal(2, tree.Search("同名").Count);
        Assert.Equal(child.Id, Assert.Single(tree.Search(child.Id.ToString())).PersonId);
        Assert.Contains(tree.Roots, n => n.PersonId == child.Id && n.Projection.NeedsChoice);
        Assert.Contains(tree.Roots, n => n.PersonId == isolated.Id);
        Assert.Equal(parent.Id, tree.GetPerson(branch.Id).Parent!.PersonId);
        tree.Choose(child.Id, adoptive.Id);
        Assert.Equal(parent.Id, tree.GetPerson(child.Id).Parent!.PersonId);
        Assert.Equal(adoptive.Id, tree.GetPerson(child.Id).Projection.SelectedParent!.Relationship.Id);
        var revealed = tree.Reveal(child.Id);
        Assert.True(revealed.IsSelected);
        Assert.True(tree.GetPerson(parent.Id).IsExpanded);
        tree.Choose(child.Id, null);
        Assert.Contains(tree.Roots, n => n.PersonId == child.Id && n.Projection.NeedsChoice);
        Assert.Throws<InvalidOperationException>(() => session.Choose(child.Id, Guid.NewGuid()));
        session.Choose(child.Id, adoptive.Id);
        Assert.Equal(adoptive.Id, session.Project().Single(row => row.Person.Id == child.Id).SelectedParent!.Relationship.Id);
        session.Choose(child.Id, biological.Id);
        Assert.Equal(biological.Id, session.Project().Single(row => row.Person.Id == child.Id).SelectedParent!.Relationship.Id);
        session.Choose(child.Id, null);
        Assert.True(session.Project().Single(row => row.Person.Id == child.Id).NeedsChoice);
        Assert.Equal(4, await context.PersonRelationships.AsNoTracking().CountAsync());
        Assert.Equal(preview.Snapshot.UnlinkGeneration, (await service.GetGraphSnapshotAsync(genealogy.Id)).UnlinkGeneration);
        Assert.True(vm.IsLineagePreviewCurrent(preview));
        await vm.SelectAsync(genealogy);
        Assert.False(vm.IsLineagePreviewCurrent(preview));
        preview = await vm.GetLineagePreviewAsync();
        await vm.SelectAsync(other);
        Assert.False(vm.IsLineagePreviewCurrent(preview));
        var missing = preview.Snapshot with { Relationships = preview.Snapshot.Relationships.Append(
            new GenealogyRelationshipData(Guid.NewGuid(), child.Id, foreign.Id, PersonRelationshipKind.BiologicalParent, false, null, null)).ToList() };
        Assert.Throws<InvalidOperationException>(() => new LineageProjectionSession(missing));
        var cyclic = preview.Snapshot with { Relationships = preview.Snapshot.Relationships.Append(
            new GenealogyRelationshipData(Guid.NewGuid(), parent.Id, child.Id, PersonRelationshipKind.BiologicalParent, false, null, null)).ToList() };
        Assert.Throws<InvalidOperationException>(() => new LineageProjectionSession(cyclic));
    }
}
