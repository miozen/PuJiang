using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

public class TargetMemberTreeProjectionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Multiple_parents_and_missing_lineage_remain_visible_without_choosing_a_mainline()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "虚构谱" });
        var child = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "同名", RecordedGeneration = "三世", DerivedGeneration = 4 });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "同名" });
        var otherParent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "另一生物父母" });
        var adoptive = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "养亲" });
        var unrelated = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "未详上系" });
        await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent, IsPreferredForLineage = true, SourceCitation = "虚构来源" });
        await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = otherParent.Id, Kind = PersonRelationshipKind.BiologicalParent });
        await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = adoptive.Id, Kind = PersonRelationshipKind.AdoptiveParent });
        await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = parent.Id, Kind = PersonRelationshipKind.LostAncestry });
        context.FamilyUnions.Add(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = parent.Id, PartnerTwoId = adoptive.Id, Note = "虚构婚配" });
        await context.SaveChangesAsync();

        var snapshot = await service.GetGraphSnapshotAsync(genealogy.Id);
        var nodes = TargetMemberTreeProjection.Create(snapshot);
        var childNode = nodes.Single(n => n.PersonId == child.Id);
        Assert.Contains(child.Id.ToString(), childNode.Label);
        Assert.Contains(parent.Id.ToString(), nodes.Single(n => n.PersonId == parent.Id).Label);
        Assert.Contains(childNode.Details, d => d.Contains("未自动选择展示主线"));
        Assert.Contains(childNode.Details, d => d.Contains("生物父母") && d.Contains(parent.Id.ToString()) && d.Contains("虚构来源") && d.Contains("谱载首选标记"));
        Assert.Contains(childNode.Details, d => d.Contains("生物父母") && d.Contains(otherParent.Id.ToString()));
        Assert.Contains(childNode.Details, d => d.Contains("收养父母") && d.Contains(adoptive.Id.ToString()));
        Assert.Contains(nodes.Single(n => n.PersonId == parent.Id).Details, d => d.Contains("上系失考"));
        Assert.Contains(nodes.Single(n => n.PersonId == parent.Id).Details, d => d.Contains("婚配") && d.Contains("虚构婚配"));
        Assert.Contains(nodes.Single(n => n.PersonId == unrelated.Id).Details, d => d.Contains("不推断为始祖"));
        Assert.Equal("三世", snapshot.People.Single(p => p.Id == child.Id).RecordedGeneration);
        Assert.Equal(4, snapshot.People.Single(p => p.Id == child.Id).DerivedGeneration);
        Assert.Equal(4, (await service.GetGraphSnapshotAsync(genealogy.Id)).Relationships.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Switching_genealogies_clears_tree_and_never_displays_other_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        Assert.Contains("甲人物", Assert.Single(vm.TargetTree).Label);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        Assert.Empty(vm.TargetTree);
        vm.NewPersonName = "乙人物";
        await vm.AddPersonAsync();
        Assert.Contains("乙人物", Assert.Single(vm.TargetTree).Label);
        await vm.SelectAsync(first);
        Assert.Contains("甲人物", Assert.Single(vm.TargetTree).Label);
        await vm.SelectAsync(null);
        Assert.Empty(vm.TargetTree);
    }
}
