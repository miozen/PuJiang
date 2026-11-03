using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

public class AncestryPathTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Preview_uses_scoped_snapshot_and_explicit_choice_without_writing()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var child = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "同名" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "同名" });
        var adoptive = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "养亲" });
        var grandparent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "上代" });
        var biological = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id,
            Kind = PersonRelationshipKind.BiologicalParent, IsPreferredForLineage = true, SourceCitation = "旧谱" };
        await service.AddRelationshipAsync(biological);
        var adopted = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = adoptive.Id, Kind = PersonRelationshipKind.AdoptiveParent };
        await service.AddRelationshipAsync(adopted);
        await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = parent.Id,
            RelatedPersonId = grandparent.Id, Kind = PersonRelationshipKind.HeirParent });
        await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, Kind = PersonRelationshipKind.LostAncestry });
        var other = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var foreign = await service.AddPersonAsync(new Person { GenealogyId = other.Id, Name = "外谱" });
        var vm = new GenealogySelectionViewModel(database.CreateContext);
        await vm.LoadAsync();
        await vm.SelectAsync(vm.Genealogies.Single(g => g.Id == genealogy.Id));
        vm.SelectedPerson = vm.People.Single(p => p.Id == child.Id);
        var preview = await vm.GetAncestryPreviewAsync();
        var session = new AncestryPathSession(preview.Snapshot, child.Id);
        Assert.Single(session.Path);
        Assert.Contains("失考", session.Path[0]);
        Assert.Equal(2, session.Choices.Count);
        Assert.Contains(session.Choices, c => c.RelationshipId == biological.Id && c.Label.Contains("谱载首选标记") && c.Label.Contains("旧谱"));
        Assert.DoesNotContain(session.Choices, c => c.ParentId == foreign.Id);
        var abandoned = new AncestryPathSession(preview.Snapshot, child.Id);
        abandoned.Choose(adopted.Id);
        Assert.Equal(2, abandoned.Path.Count);
        Assert.Contains(adoptive.Id.ToString(), abandoned.Path[1]);
        session.Choose(biological.Id);
        Assert.Equal(3, session.Path.Count);
        Assert.Contains(parent.Id.ToString(), session.Path[1]);
        Assert.Contains(grandparent.Id.ToString(), session.Path[2]);
        Assert.Contains("未记录", session.Status);
        Assert.Throws<InvalidOperationException>(() => session.Choose(adopted.Id));
        Assert.Equal(4, await context.PersonRelationships.CountAsync());
        Assert.Equal(preview.Snapshot.UnlinkGeneration, (await service.GetGraphSnapshotAsync(genealogy.Id)).UnlinkGeneration);
        Assert.True(vm.IsAncestryPreviewCurrent(preview));
        await vm.SelectAsync(genealogy);
        Assert.False(vm.IsAncestryPreviewCurrent(preview));
        vm.SelectedPerson = vm.People.Single(p => p.Id == child.Id);
        preview = await vm.GetAncestryPreviewAsync();
        await vm.SelectAsync(other);
        Assert.False(vm.IsAncestryPreviewCurrent(preview));
        var cyclic = preview.Snapshot with { Relationships = preview.Snapshot.Relationships.Where(r => r.Id != adopted.Id)
            .Append(new GenealogyRelationshipData(Guid.NewGuid(), grandparent.Id, child.Id, PersonRelationshipKind.BiologicalParent, false, null, null)).ToList() };
        var failure = new AncestryPathSession(cyclic, child.Id);
        Assert.Empty(failure.Path);
        Assert.Contains("循环", failure.Status);
    }
}
