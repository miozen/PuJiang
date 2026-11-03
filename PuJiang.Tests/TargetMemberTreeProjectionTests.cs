using Xunit;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

public class TargetMemberTreeProjectionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Directed_spouse_is_visible_on_both_sides_without_creating_or_merging_records()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var first = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "同名" });
        var second = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "同名" });
        var relationship = new PersonRelationship { GenealogyId = genealogy.Id, PersonId = first.Id,
            RelatedPersonId = second.Id, Kind = PersonRelationshipKind.Spouse, Note = "原备注", SourceCitation = "原来源" };
        await service.AddRelationshipAsync(relationship);
        await service.AddFamilyUnionAsync(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = first.Id, PartnerTwoId = second.Id });
        var vm = new GenealogySelectionViewModel(database.CreateContext);
        await vm.LoadAsync();
        await vm.SelectAsync(vm.Genealogies.Single(g => g.Id == genealogy.Id));
        var forward = vm.TargetTree.Single(n => n.PersonId == first.Id).Entries.Single(e => e.Label.Contains(relationship.Id.ToString()));
        var reverse = vm.TargetTree.Single(n => n.PersonId == second.Id).Entries.Single(e => e.Label.Contains(relationship.Id.ToString()));
        Assert.Equal(second.Id, vm.GetRelationshipTargetNode(forward).PersonId);
        Assert.Equal(first.Id, vm.GetRelationshipTargetNode(reverse).PersonId);
        Assert.Contains("原记录方向", reverse.Label);
        Assert.Contains(first.Id.ToString(), reverse.Label);
        Assert.Contains(second.Id.ToString(), reverse.Label);
        Assert.Contains("原备注", reverse.Label);
        Assert.Contains("原来源", reverse.Label);
        Assert.Contains(vm.TargetTree.Single(n => n.PersonId == second.Id).Entries, e => e.Label.StartsWith("婚配："));
        Assert.Single(await context.PersonRelationships.AsNoTracking().ToListAsync());
        Assert.Single(await context.FamilyUnions.AsNoTracking().ToListAsync());
        await vm.SelectAsync(vm.SelectedGenealogy);
        Assert.Throws<InvalidOperationException>(() => vm.GetRelationshipTargetNode(reverse));
        var reloaded = vm.TargetTree.Single(n => n.PersonId == second.Id).Entries.Single(e => e.Label.Contains(relationship.Id.ToString()));
        var other = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        await vm.SelectAsync(other);
        Assert.Throws<InvalidOperationException>(() => vm.GetRelationshipTargetNode(reloaded));
        await vm.SelectAsync(genealogy);
        vm.SelectedPerson = vm.People.Single(p => p.Id == second.Id);
        var candidates = await vm.GetTargetLinkRemovalCandidatesAsync();
        await vm.RemoveTargetLinkAsync(candidates.Single(c => c.Kind == TargetLinkRemovalKind.FamilyUnion));
        Assert.Single(await context.PersonRelationships.AsNoTracking().ToListAsync());
        Assert.Empty(await context.FamilyUnions.AsNoTracking().ToListAsync());
        await service.AddFamilyUnionAsync(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = first.Id, PartnerTwoId = second.Id });
        vm.SelectedPerson = vm.People.Single(p => p.Id == second.Id);
        candidates = await vm.GetTargetLinkRemovalCandidatesAsync();
        await vm.RemoveTargetLinkAsync(candidates.Single(c => c.Kind == TargetLinkRemovalKind.Relationship));
        Assert.Empty(await context.PersonRelationships.AsNoTracking().ToListAsync());
        Assert.Single(await context.FamilyUnions.AsNoTracking().ToListAsync());
    }

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
        context.FamilyUnions.Add(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = child.Id, PartnerTwoId = null });
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

        var vm = new GenealogySelectionViewModel(database.CreateContext);
        await vm.LoadAsync();
        await vm.SelectAsync(vm.Genealogies.Single(g => g.Id == genealogy.Id));
        var currentChild = vm.TargetTree.Single(n => n.PersonId == child.Id);
        var childToParent = currentChild.Entries.Single(e => e.OtherPersonId == parent.Id);
        Assert.Equal(parent.Id, vm.GetRelationshipTargetNode(childToParent).PersonId);
        var currentParent = vm.TargetTree.Single(n => n.PersonId == parent.Id);
        Assert.Equal(child.Id, vm.GetRelationshipTargetNode(currentParent.Entries.Single(e => e.OtherPersonId == child.Id)).PersonId);
        Assert.Equal(adoptive.Id, vm.GetRelationshipTargetNode(currentParent.Entries.Single(e => e.OtherPersonId == adoptive.Id)).PersonId);
        Assert.Throws<InvalidOperationException>(() => vm.GetRelationshipTargetNode(currentParent.Entries.Single(e => e.Label.Contains("上系失考"))));
        Assert.Throws<InvalidOperationException>(() => vm.GetRelationshipTargetNode(currentChild.Entries.Single(e => e.Label.Contains("对方资料未记录"))));
        var secondGenealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "另一谱" });
        await vm.SelectAsync(vm.SelectedGenealogy);
        Assert.Throws<InvalidOperationException>(() => vm.GetRelationshipTargetNode(childToParent));
        var reloadedEntry = vm.TargetTree.Single(n => n.PersonId == child.Id).Entries.Single(e => e.OtherPersonId == parent.Id);
        await vm.SelectAsync(secondGenealogy);
        Assert.Throws<InvalidOperationException>(() => vm.GetRelationshipTargetNode(reloadedEntry));
        await vm.SelectAsync(null);
        Assert.Throws<InvalidOperationException>(() => vm.GetRelationshipTargetNode(childToParent));
        Assert.Equal(4, await context.PersonRelationships.CountAsync());
        Assert.Equal(2, await context.FamilyUnions.CountAsync());
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
