using System.Data.Common;
using Xunit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

public class GenealogySelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Union_kind_correction_preserves_data_and_rejects_conflicts(bool unknownPartner)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "主体"; await vm.AddPersonAsync();
        vm.NewPersonName = "对方"; await vm.AddPersonAsync();
        vm.SelectedPerson = vm.People.Single(p => p.Name == "主体");
        var subject = vm.SelectedPerson.Id;
        Guid? partner = unknownPartner ? null : vm.People.Single(p => p.Name == "对方").Id;
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, partner, "始", "止", "备注", unknownPartner);
        var original = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CorrectFamilyUnionKindAsync(original, FamilyUnionKind.Marriage));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CorrectFamilyUnionKindAsync(original, (FamilyUnionKind)99));
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CorrectFamilyUnionKindAsync(Guid.NewGuid(), original.UnionSnapshot!, subject,
                original.FirstName, original.SecondName, original.UnlinkGeneration, FamilyUnionKind.Partnership));
            await service.AddFamilyUnionAsync(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = subject, PartnerTwoId = partner, Kind = FamilyUnionKind.Partnership });
        }
        var duplicates = await vm.GetTargetLinkRemovalCandidatesAsync();
        var conflict = duplicates.Single(c => c.Id == original.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CorrectFamilyUnionKindAsync(conflict, FamilyUnionKind.Partnership));
        await vm.RemoveTargetLinkAsync(duplicates.Single(c => c.Id != original.Id));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => vm.CorrectFamilyUnionKindAsync(original, FamilyUnionKind.Unknown));
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailUnionKind BEFORE UPDATE ON FamilyUnions BEGIN SELECT RAISE(ABORT, 'test'); END");
        var failureCandidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        await Assert.ThrowsAnyAsync<Exception>(() => vm.CorrectFamilyUnionKindAsync(failureCandidate, FamilyUnionKind.Unknown));
        await using (var context = database.CreateContext())
        {
            Assert.Equal(FamilyUnionKind.Marriage, (await context.FamilyUnions.AsNoTracking().SingleAsync()).Kind);
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER FailUnionKind");
        }
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        vm.SelectedPerson = null;
        vm.SelectedPerson = vm.People.Single(p => p.Id == subject);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CorrectFamilyUnionKindAsync(candidate, FamilyUnionKind.Unknown));
        await vm.CorrectFamilyUnionKindAsync((await vm.GetTargetLinkRemovalCandidatesAsync()).Single(), FamilyUnionKind.Unknown);
        await using var verify = database.CreateContext();
        var saved = await verify.FamilyUnions.AsNoTracking().SingleAsync();
        Assert.Equal(original.Id, saved.Id); Assert.Equal(original.UnionSnapshot!.PartnerOneId, saved.PartnerOneId);
        Assert.Equal(original.UnionSnapshot.PartnerTwoId, saved.PartnerTwoId);
        Assert.Equal(FamilyUnionKind.Unknown, saved.Kind); Assert.Equal("始", saved.StartDateText);
        Assert.Equal("止", saved.EndDateText); Assert.Equal("备注", saved.Note);
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Complete_unknown_union_preserves_identity_and_rejects_invalid_changes(bool reverseOrder)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var subject = reverseOrder ? high : low;
        var partner = reverseOrder ? low : high;
        Guid foreignId;
        await using (var context = database.CreateContext())
        {
            var foreign = new Genealogy { Name = "乙谱" }; context.Genealogies.Add(foreign);
            var foreignPerson = new Person { GenealogyId = foreign.Id, Name = "外谱" }; foreignId = foreignPerson.Id;
            context.People.AddRange(new Person { Id = subject, GenealogyId = genealogy.Id, Name = "主体" },
                new Person { Id = partner, GenealogyId = genealogy.Id, Name = "对方" }, foreignPerson);
            await context.SaveChangesAsync();
        }
        await vm.SelectAsync(genealogy);
        vm.SelectedPerson = vm.People.Single(p => p.Id == subject);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, null, "始", "止", "备注", unknownPartner: true);
        var original = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CompleteUnknownUnionPartnerAsync(original, vm.People.Single(p => p.Id == subject)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CompleteUnknownUnionPartnerAsync(original, new Person { Id = foreignId, GenealogyId = Guid.NewGuid(), Name = "外谱" }));
        await using (var context = database.CreateContext())
        {
            await new GenealogyDataService(context).AddFamilyUnionAsync(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = subject, PartnerTwoId = partner, Kind = FamilyUnionKind.Marriage });
        }
        var duplicate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.Id == original.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CompleteUnknownUnionPartnerAsync(duplicate, vm.People.Single(p => p.Id == partner)));
        var other = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.Id != original.Id);
        await vm.RemoveTargetLinkAsync(other);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => vm.CompleteUnknownUnionPartnerAsync(original, vm.People.Single(p => p.Id == partner)));
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailUnionComplete BEFORE UPDATE ON FamilyUnions BEGIN SELECT RAISE(ABORT, 'test'); END");
        var beforeFailure = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        await Assert.ThrowsAnyAsync<Exception>(() => vm.CompleteUnknownUnionPartnerAsync(beforeFailure, vm.People.Single(p => p.Id == partner)));
        await using (var context = database.CreateContext())
        {
            Assert.Null((await context.FamilyUnions.AsNoTracking().SingleAsync()).PartnerTwoId);
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER FailUnionComplete");
        }
        await vm.CompleteUnknownUnionPartnerAsync(await GetCandidate(), vm.People.Single(p => p.Id == partner));
        await using var verify = database.CreateContext();
        var saved = await verify.FamilyUnions.AsNoTracking().SingleAsync();
        Assert.Equal(original.Id, saved.Id); Assert.Equal(low, saved.PartnerOneId); Assert.Equal(high, saved.PartnerTwoId);
        Assert.Equal(FamilyUnionKind.Marriage, saved.Kind); Assert.Equal("始", saved.StartDateText); Assert.Equal("止", saved.EndDateText); Assert.Equal("备注", saved.Note);
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        async Task<TargetLinkRemovalCandidate> GetCandidate() => (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Unknown_partner_union_requires_explicit_choice_preserves_data_and_rejects_failures()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "主体"; await vm.AddPersonAsync();
        vm.SelectedPerson = vm.People.Single();
        var subject = vm.SelectedPerson.Id;
        var selection = vm.CapturePersonSelection();
        vm.SelectedPerson = null;
        vm.SelectedPerson = vm.People.Single();
        Assert.Throws<InvalidOperationException>(() => vm.ValidatePersonSelection(selection));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Unknown, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Unknown, subject, unknownPartner: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Unknown, Guid.NewGuid()));
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailUnknownUnion BEFORE INSERT ON FamilyUnions BEGIN SELECT RAISE(ABORT, 'test'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Unknown, null, unknownPartner: true));
        await using (var context = database.CreateContext())
        {
            Assert.Empty(await context.FamilyUnions.ToListAsync());
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER FailUnknownUnion");
        }
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Unknown, null, "始", "止", "备注", unknownPartner: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Unknown, null, unknownPartner: true));
        await using (var context = database.CreateContext())
        {
            var saved = await context.FamilyUnions.SingleAsync();
            Assert.Equal(genealogy.Id, saved.GenealogyId);
            Assert.Equal(subject, saved.PartnerOneId);
            Assert.Null(saved.PartnerTwoId);
            Assert.Equal("始", saved.StartDateText); Assert.Equal("止", saved.EndDateText); Assert.Equal("备注", saved.Note);
            Assert.Empty(await context.PersonRelationships.ToListAsync());
            Assert.Single(await context.People.ToListAsync());
        }
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        await vm.UpdateTargetFamilyUnionDetailsAsync(candidate, "更新", null, "保留");
        await vm.RemoveTargetLinkAsync((await vm.GetTargetLinkRemovalCandidatesAsync()).Single());
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.FamilyUnions.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Parent_kind_correction_preserves_identity_and_rejects_invalid_or_stale_changes()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "主体"; await vm.AddPersonAsync();
        vm.NewPersonName = "父母"; await vm.AddPersonAsync();
        vm.SelectedPerson = vm.People.Single(p => p.Name == "主体");
        var subject = vm.SelectedPerson.Id;
        var parent = vm.People.Single(p => p.Name == "父母").Id;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.BiologicalParent, parent, "备注", "来源");
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CorrectParentRelationshipKindAsync(candidate, PersonRelationshipKind.Spouse));
        await vm.CorrectParentRelationshipKindAsync(candidate, PersonRelationshipKind.AdoptiveParent);
        await Assert.ThrowsAsync<StaleTargetLinkException>(() => vm.CorrectParentRelationshipKindAsync(candidate, PersonRelationshipKind.HeirParent));
        var current = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        Assert.Equal(candidate.Id, current.Id);
        Assert.Equal("备注", current.RelationshipSnapshot!.Note);
        Assert.Equal("来源", current.RelationshipSnapshot.SourceCitation);
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CorrectParentRelationshipKindAsync(Guid.NewGuid(), current.RelationshipSnapshot, subject, current.FirstName, current.SecondName, current.UnlinkGeneration, PersonRelationshipKind.HeirParent));
            await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = subject, RelatedPersonId = parent, Kind = PersonRelationshipKind.HeirParent });
        }
        var duplicates = await vm.GetTargetLinkRemovalCandidatesAsync();
        var adoptive = duplicates.Single(c => c.Id == candidate.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CorrectParentRelationshipKindAsync(adoptive, PersonRelationshipKind.HeirParent));
        await vm.RemoveTargetLinkAsync(duplicates.Single(c => c.Id != candidate.Id));
        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PersonRelationships (Id,GenealogyId,PersonId,RelatedPersonId,Kind,IsPreferredForLineage) VALUES ({Guid.NewGuid()},{genealogy.Id},{parent},{subject},{(int)PersonRelationshipKind.HeirParent},{false})");
        }
        var cycle = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.Id == candidate.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CorrectParentRelationshipKindAsync(cycle, PersonRelationshipKind.BiologicalParent));
        await using (var context = database.CreateContext())
        {
            await context.PersonRelationships.Where(r => r.PersonId == parent).ExecuteDeleteAsync();
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailKindUpdate BEFORE UPDATE ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'test'); END");
        }
        var beforeFailure = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        await Assert.ThrowsAnyAsync<Exception>(() => vm.CorrectParentRelationshipKindAsync(beforeFailure, PersonRelationshipKind.HeirParent));
        await using var verify = database.CreateContext();
        var saved = await verify.PersonRelationships.AsNoTracking().SingleAsync();
        Assert.Equal(PersonRelationshipKind.AdoptiveParent, saved.Kind);
        Assert.Equal("备注", saved.Note);
        Assert.Equal("来源", saved.SourceCitation);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Person_delete_preview_blocks_links_and_stale_or_switched_selection()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var firstGenealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "主体"; await vm.AddPersonAsync();
        vm.NewPersonName = "对方"; await vm.AddPersonAsync();
        vm.SelectedPerson = vm.People.Single(p => p.Name == "主体");
        var subject = vm.SelectedPerson.Id;
        var other = vm.People.Single(p => p.Name == "对方").Id;
        var empty = await vm.GetPersonDeletePreviewAsync();
        Assert.Empty(empty.Links);
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = firstGenealogy.Id, PersonId = subject, RelatedPersonId = other, Kind = PersonRelationshipKind.Spouse });
            await service.AddFamilyUnionAsync(new FamilyUnion { GenealogyId = firstGenealogy.Id, PartnerOneId = subject, PartnerTwoId = other, Kind = FamilyUnionKind.Marriage });
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeletePersonFromPreviewAsync(empty));
        var linked = await vm.GetPersonDeletePreviewAsync();
        Assert.Equal(2, linked.Links.Count);
        Assert.Contains(linked.Links, link => link.Kind == TargetLinkRemovalKind.Relationship);
        Assert.Contains(linked.Links, link => link.Kind == TargetLinkRemovalKind.FamilyUnion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeletePersonFromPreviewAsync(linked));
        var relation = linked.Links.Single(link => link.Kind == TargetLinkRemovalKind.Relationship);
        await vm.RemoveTargetLinkAsync(relation);
        await vm.RemoveTargetLinkAsync((await vm.GetTargetLinkRemovalCandidatesAsync()).Single());
        var fresh = await vm.GetPersonDeletePreviewAsync();
        Assert.Empty(fresh.Links);
        vm.NewName = "乙谱"; await vm.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeletePersonFromPreviewAsync(fresh));
        await vm.SelectAsync(vm.Genealogies.Single(g => g.Id == firstGenealogy.Id));
        vm.SelectedPerson = vm.People.Single(p => p.Id == subject);
        await vm.DeletePersonFromPreviewAsync(await vm.GetPersonDeletePreviewAsync());
        await using var verify = database.CreateContext();
        Assert.False(await verify.People.AnyAsync(p => p.Id == subject));
        Assert.True(await verify.People.AnyAsync(p => p.Id == other));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Delete_branch_reassigns_contents_and_rolls_back_on_failure()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        vm.NewBranchName = "甲"; await vm.CreateRootBranchAsync();
        var source = vm.Branches.Single();
        vm.NewBranchName = "乙"; await vm.CreateRootBranchAsync();
        var target = vm.Branches.Single(b => b.Name == "乙");
        vm.SelectedParentBranch = vm.ParentBranchChoices.Single(b => b.Id == source.Id);
        vm.NewChildBranchName = "子"; await vm.CreateChildBranchAsync();
        var child = vm.Branches.Single(b => b.Name == "子");
        vm.NewPersonName = "人物"; await vm.AddPersonAsync();
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(vm.TargetTree.Single(), "人物", PersonSex.Unknown, null, null, null, null, null, true, source.Id);
        vm.BranchToRename = vm.ParentBranchChoices.Single(b => b.Id == source.Id);
        var candidate = vm.GetBranchReassignmentCandidate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteBranchAndReassignAsync(candidate, child.Id, null));
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteBranchAndReassignAsync(candidate.Snapshot, Guid.NewGuid(), null));
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailBranchDelete BEFORE DELETE ON FamilyBranches BEGIN SELECT RAISE(ABORT, 'test failure'); END");
            await Assert.ThrowsAnyAsync<Exception>(() => service.DeleteBranchAndReassignAsync(candidate.Snapshot, target.Id, null));
        }
        await using (var verify = database.CreateContext())
        {
            Assert.Equal(source.Id, (await verify.FamilyBranches.AsNoTracking().SingleAsync(b => b.Id == child.Id)).ParentBranchId);
            Assert.Equal(source.Id, (await verify.People.AsNoTracking().SingleAsync()).BranchId);
            await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER FailBranchDelete");
            await verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"已改名"} WHERE BranchId = {source.Id}");
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteBranchAndReassignAsync(candidate, target.Id, null));
        await vm.SelectAsync(vm.SelectedGenealogy);
        vm.BranchToRename = vm.ParentBranchChoices.Single(b => b.Id == source.Id);
        await vm.DeleteBranchAndReassignAsync(vm.GetBranchReassignmentCandidate(), target.Id, null);
        Assert.DoesNotContain(vm.Branches, b => b.Id == source.Id);
        Assert.Equal(target.Id, vm.Branches.Single(b => b.Id == child.Id).ParentBranchId);
        Assert.Null(vm.People.Single().BranchId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Delete_branch_rejects_conflicting_names_and_foreign_destination()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewBranchName = "源"; await vm.CreateRootBranchAsync();
        var source = vm.Branches.Single();
        vm.NewBranchName = "目标"; await vm.CreateRootBranchAsync();
        var target = vm.Branches.Single(b => b.Name == "目标");
        vm.SelectedParentBranch = vm.ParentBranchChoices.Single(b => b.Id == source.Id);
        vm.NewChildBranchName = "同名"; await vm.CreateChildBranchAsync();
        vm.SelectedParentBranch = vm.ParentBranchChoices.Single(b => b.Id == target.Id);
        vm.NewChildBranchName = "同名"; await vm.CreateChildBranchAsync();
        vm.NewBranchName = "同名"; await vm.CreateRootBranchAsync();
        vm.BranchToRename = vm.ParentBranchChoices.Single(b => b.Id == source.Id);
        var candidate = vm.GetBranchReassignmentCandidate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteBranchAndReassignAsync(candidate, target.Id, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteBranchAndReassignAsync(candidate, null, null));
        await using var context = database.CreateContext();
        var foreign = new Genealogy { Name = "乙谱" };
        context.Genealogies.Add(foreign);
        var foreignBranch = new FamilyBranch { GenealogyId = foreign.Id, Name = "外谱" };
        context.FamilyBranches.Add(foreignBranch);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GenealogyDataService(context).DeleteBranchAndReassignAsync(candidate.Snapshot, null, foreignBranch.Id));
        Assert.Equal(5, await context.FamilyBranches.CountAsync(b => b.GenealogyId == genealogy.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Branch_move_preserves_subtree_and_rejects_invalid_destinations()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewBranchName = "甲"; await vm.CreateRootBranchAsync();
        var a = vm.Branches.Single();
        vm.NewBranchName = "乙"; await vm.CreateRootBranchAsync();
        var b = vm.Branches.Single(x => x.Name == "乙");
        vm.SelectedParentBranch = vm.ParentBranchChoices.Single(x => x.Id == a.Id);
        vm.NewChildBranchName = "子"; await vm.CreateChildBranchAsync();
        var child = vm.Branches.Single(x => x.Name == "子");
        vm.NewPersonName = "人物"; await vm.AddPersonAsync();
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(vm.TargetTree.Single(), "人物", PersonSex.Unknown, null, null, null, null, null, true, child.Id);
        vm.BranchToRename = vm.ParentBranchChoices.Single(x => x.Id == a.Id);
        vm.MoveTargetBranch = vm.ParentBranchChoices.Single(x => x.Id == child.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.MoveBranchAsync(vm.GetBranchMoveCandidate()));
        vm.MoveTargetBranch = vm.ParentBranchChoices.Single(x => x.Id == b.Id);
        var candidate = vm.GetBranchMoveCandidate();
        await vm.MoveBranchAsync(candidate);
        Assert.Contains(vm.ParentBranchChoices, x => x.Id == child.Id && x.Label.Contains("乙 / 甲 / 子"));
        Assert.Equal(child.Id, vm.People.Single().BranchId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.MoveBranchAsync(candidate));
        vm.BranchToRename = vm.ParentBranchChoices.Single(x => x.Id == a.Id);
        vm.MoveToRoot = true;
        await vm.MoveBranchAsync(vm.GetBranchMoveCandidate());
        Assert.Null(vm.Branches.Single(x => x.Id == a.Id).ParentBranchId);
        await vm.SelectAsync(genealogy);
        Assert.Contains(vm.ParentBranchChoices, x => x.Id == child.Id && x.Label.Contains("甲 / 子"));
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var current = vm.Branches.Single(x => x.Id == a.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveBranchAsync(Guid.NewGuid(), current, b));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveBranchAsync(genealogy.Id, current with { Name = "旧名称" }, b));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveBranchAsync(genealogy.Id, current, b with { Name = "旧目标" }));
        await service.CreateChildBranchAsync(genealogy.Id, b.Id, "甲");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveBranchAsync(genealogy.Id, current, b));
        var duplicate = await context.FamilyBranches.AsNoTracking().SingleAsync(x => x.GenealogyId == genealogy.Id && x.ParentBranchId == b.Id && x.Name == "甲");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveBranchAsync(genealogy.Id,
            new GenealogyBranchData(duplicate.Id, duplicate.ParentBranchId, duplicate.Name, duplicate.SortOrder), null));
        Assert.Null((await context.FamilyBranches.AsNoTracking().SingleAsync(x => x.Id == a.Id)).ParentBranchId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Import_history_is_scoped_read_only_and_reports_read_failures()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Guid firstId, secondId;
        var sourceId = Guid.NewGuid();
        await using (var context = database.CreateContext())
        {
            var first = new Genealogy { Name = "同名谱" };
            var second = new Genealogy { Name = "同名谱" };
            firstId = first.Id; secondId = second.Id;
            context.Genealogies.AddRange(first, second);
            context.GenealogyImportRecords.AddRange(
                new GenealogyImportRecord { GenealogyId = firstId, SourceKind = "PuJiang.InternalJson", ImportMode = "Default", SourceFormatVersion = 1, SourceGenealogyId = sourceId, SourceDocumentFingerprint = "first" },
                new GenealogyImportRecord { GenealogyId = secondId, SourceKind = "PuJiang.ExcelLegacyV1", ImportMode = "Copy", SourceFormatVersion = 1, SourceGenealogyId = Guid.NewGuid(), SourceDocumentFingerprint = "second" });
            await context.SaveChangesAsync();
        }
        var vm = new GenealogySelectionViewModel(database.CreateContext);
        await vm.LoadAsync();
        await vm.SelectAsync(vm.Genealogies.Single(g => g.Id == firstId));
        var history = await vm.GetGenealogyImportHistoryAsync();
        Assert.Equal(firstId, history.Genealogy.Id);
        Assert.Equal(sourceId, Assert.Single(history.Records).SourceGenealogyId);
        await vm.SelectAsync(vm.Genealogies.Single(g => g.Id == secondId));
        Assert.Equal("second", Assert.Single((await vm.GetGenealogyImportHistoryAsync()).Records).SourceDocumentFingerprint);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.GenealogyImportRecords.CountAsync());
        Assert.Equal(2, await verify.Genealogies.CountAsync());
        await verify.Database.ExecuteSqlRawAsync("DROP TABLE GenealogyImportRecords");
        await Assert.ThrowsAsync<SqliteException>(() => vm.GetGenealogyImportHistoryAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Genealogy_profile_edits_are_scoped_preserve_data_and_reject_stale_values()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewPersonName = "不同姓人物"; await vm.AddPersonAsync();
        var original = await vm.GetGenealogyProfileCandidateAsync();
        Assert.Null(original.Snapshot.Surname);
        Assert.Null(original.Snapshot.Description);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveGenealogyProfileAsync(original, new string('姓', 101), "简介"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveGenealogyProfileAsync(original, "姓", new string('介', 2001)));
        await vm.SaveGenealogyProfileAsync(original, "张", "旧谱简介");
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveGenealogyProfileAsync(original, "覆盖", "旧候选"));
        Assert.Equal(first.Id, vm.SelectedGenealogy!.Id);
        Assert.Equal("不同姓人物", vm.People.Single().Name);
        var current = await vm.GetGenealogyProfileCandidateAsync();
        Assert.Equal("张", current.Snapshot.Surname);
        Assert.Equal("旧谱简介", current.Snapshot.Description);
        Guid secondId;
        await using (var context = database.CreateContext())
        {
            var other = await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "乙谱", Surname = "李", Description = "乙简介" });
            secondId = other.Id;
            var otherSnapshot = await new GenealogyDataService(context).GetGenealogyProfileAsync(secondId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveGenealogyProfileAsync(current with { Snapshot = otherSnapshot }, "误写", null));
            var temporary = await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "临时谱" });
            var deletedSnapshot = await new GenealogyDataService(context).GetGenealogyProfileAsync(temporary.Id);
            await context.Genealogies.Where(g => g.Id == temporary.Id).ExecuteDeleteAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => new GenealogyDataService(context).UpdateGenealogyProfileAsync(deletedSnapshot, "误写", null));
        }
        await vm.SaveGenealogyProfileAsync(current, "赵", "更正简介");
        var updated = await vm.GetGenealogyProfileCandidateAsync();
        await vm.SaveGenealogyProfileAsync(updated, null, null);
        await using (var verify = database.CreateContext())
        {
            var own = await verify.Genealogies.SingleAsync(g => g.Id == first.Id);
            var other = await verify.Genealogies.SingleAsync(g => g.Id == secondId);
            Assert.Equal("甲谱", own.Name);
            Assert.Null(own.Surname);
            Assert.Null(own.Description);
            Assert.Equal("李", other.Surname);
            Assert.Equal("乙简介", other.Description);
            Assert.Single(await verify.People.Where(p => p.GenealogyId == first.Id).ToListAsync());
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Genealogy_rename_uses_id_and_original_name_preserving_selection_and_data()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Guid firstId, secondId;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            var first = await service.CreateGenealogyAsync(new Genealogy { Name = "同名谱", Surname = "甲", Description = "保留" });
            var second = await service.CreateGenealogyAsync(new Genealogy { Name = "同名谱", Surname = "乙" });
            firstId = first.Id; secondId = second.Id;
            await service.AddPersonAsync(new Person { GenealogyId = firstId, Name = "甲人物" });
            await service.AddPersonAsync(new Person { GenealogyId = secondId, Name = "乙人物" });
        }
        var vm = new GenealogySelectionViewModel(database.CreateContext);
        await vm.LoadAsync();
        await vm.SelectAsync(vm.Genealogies.Single(g => g.Id == firstId));
        vm.RenamedGenealogyName = " ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenameGenealogyAsync());
        vm.RenamedGenealogyName = new string('谱', 201);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenameGenealogyAsync());
        vm.RenamedGenealogyName = "新名称";
        await vm.RenameGenealogyAsync();
        Assert.Equal(firstId, vm.SelectedGenealogy!.Id);
        Assert.Equal("新名称", vm.SelectedGenealogy.Name);
        Assert.Equal("甲人物", vm.People.Single().Name);
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            Assert.Equal("同名谱", (await context.Genealogies.SingleAsync(g => g.Id == secondId)).Name);
            await service.RenameGenealogyAsync(firstId, "新名称", "外部更名");
        }
        vm.RenamedGenealogyName = "陈旧提交";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenameGenealogyAsync());
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameGenealogyAsync(Guid.NewGuid(), "不存在", "误写"));
            var first = await context.Genealogies.SingleAsync(g => g.Id == firstId);
            Assert.Equal("外部更名", first.Name);
            Assert.Equal("甲", first.Surname);
            Assert.Equal("保留", first.Description);
            Assert.Equal(2, await context.People.CountAsync());
            await service.RenameGenealogyAsync(firstId, "外部更名", "同名谱");
            Assert.Equal(2, await context.Genealogies.CountAsync(g => g.Name == "同名谱"));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_union_details_preserve_identity_and_reject_stale_foreign_and_invalid_requests()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "主体"; await vm.AddPersonAsync();
        vm.NewPersonName = "对方"; await vm.AddPersonAsync();
        vm.SelectedPerson = vm.People.Single(p => p.Name == "主体");
        var subjectId = vm.SelectedPerson.Id;
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, vm.People.Single(p => p.Name == "对方").Id, "旧始", "旧止", "旧注");
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.Kind == TargetLinkRemovalKind.FamilyUnion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.UpdateTargetFamilyUnionDetailsAsync(candidate, new string('年', 101), null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.UpdateTargetFamilyUnionDetailsAsync(candidate, null, new string('年', 101), null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.UpdateTargetFamilyUnionDetailsAsync(candidate, null, null, new string('注', 2001)));
        await vm.UpdateTargetFamilyUnionDetailsAsync(candidate, "新始", "新止", "新注");
        await Assert.ThrowsAsync<StaleTargetLinkException>(() => vm.UpdateTargetFamilyUnionDetailsAsync(candidate, "误改", null, null));
        await using (var context = database.CreateContext())
        {
            var saved = await context.FamilyUnions.SingleAsync();
            Assert.Equal(candidate.Id, saved.Id);
            Assert.Equal(candidate.UnionSnapshot!.PartnerOneId, saved.PartnerOneId);
            Assert.Equal(candidate.UnionSnapshot.PartnerTwoId, saved.PartnerTwoId);
            Assert.Equal(candidate.UnionSnapshot.Kind, saved.Kind);
            Assert.Equal("新注", saved.Note);
            var foreign = await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
            var current = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.Kind == TargetLinkRemovalKind.FamilyUnion);
            await Assert.ThrowsAsync<StaleTargetLinkException>(() => new GenealogyDataService(context).UpdateFamilyUnionDetailsAsync(
                foreign.Id, current.UnionSnapshot!, subjectId, current.FirstName, current.SecondName, current.UnlinkGeneration, null, null, null));
            await new GenealogyDataService(context).AddFamilyUnionAsync(new FamilyUnion
            { GenealogyId = genealogy.Id, PartnerOneId = subjectId, PartnerTwoId = null, Kind = FamilyUnionKind.Unknown, Note = "未详" });
        }
        await vm.SelectAsync(genealogy);
        vm.SelectedPerson = vm.People.Single(p => p.Id == subjectId);
        var single = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.Kind == TargetLinkRemovalKind.FamilyUnion && c.UnionSnapshot!.PartnerTwoId is null);
        await vm.UpdateTargetFamilyUnionDetailsAsync(single, "记载", null, null);
        await using var verify = database.CreateContext();
        var incomplete = await verify.FamilyUnions.SingleAsync(u => u.Id == single.Id);
        Assert.Null(incomplete.PartnerTwoId);
        Assert.Equal("记载", incomplete.StartDateText);
        Assert.Null(incomplete.Note);
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_relationship_details_update_preserves_record_and_rejects_stale_or_foreign_candidates()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "主体"; await vm.AddPersonAsync();
        vm.NewPersonName = "父母"; await vm.AddPersonAsync();
        vm.SelectedPerson = vm.People.Single(p => p.Name == "主体");
        var subjectId = vm.SelectedPerson.Id;
        var parentId = vm.People.Single(p => p.Name == "父母").Id;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.AdoptiveParent, parentId, "旧备注", "旧来源");
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.UpdateTargetRelationshipDetailsAsync(candidate, new string('注', 2001), "来源"));
        await vm.UpdateTargetRelationshipDetailsAsync(candidate, "新备注", "新来源");
        await Assert.ThrowsAsync<StaleTargetLinkException>(() => vm.UpdateTargetRelationshipDetailsAsync(candidate, "旧候选", null));
        await using (var context = database.CreateContext())
        {
            var relation = await context.PersonRelationships.SingleAsync();
            Assert.Equal(candidate.Id, relation.Id);
            Assert.Equal(subjectId, relation.PersonId);
            Assert.Equal(parentId, relation.RelatedPersonId);
            Assert.Equal(PersonRelationshipKind.AdoptiveParent, relation.Kind);
            Assert.Equal("新备注", relation.Note);
            Assert.Equal("新来源", relation.SourceCitation);
            var service = new GenealogyDataService(context);
            var foreign = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
            var refreshed = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
            await Assert.ThrowsAsync<StaleTargetLinkException>(() => service.UpdateRelationshipDetailsAsync(foreign.Id,
                refreshed.RelationshipSnapshot!, subjectId, refreshed.FirstName, refreshed.SecondName, refreshed.UnlinkGeneration, "跨谱", null));
        }
        var current = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single();
        await vm.UpdateTargetRelationshipDetailsAsync(current, null, null);
        await vm.SelectAsync(genealogy);
        await using var final = database.CreateContext();
        var saved = await final.PersonRelationships.SingleAsync();
        Assert.Equal(candidate.Id, saved.Id);
        Assert.Null(saved.Note);
        Assert.Null(saved.SourceCitation);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Union_details_persist_and_invalid_requests_leave_no_records()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "主体", "对方", "另一方" }) { vm.NewPersonName = name; await vm.AddPersonAsync(); }
        vm.SelectedPerson = vm.People.Single(p => p.Name == "主体");
        var partner = vm.People.Single(p => p.Name == "对方").Id;
        Guid foreignId;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            var foreign = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
            foreignId = (await service.AddPersonAsync(new Person { GenealogyId = foreign.Id, Name = "外谱" })).Id;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, partner, new string('始', 101)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, partner, null, new string('止', 101)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, partner, null, null, new string('注', 2001)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, foreignId, "始", "止", "备注"));
        await using (var verifyEmpty = database.CreateContext()) Assert.Empty(await verifyEmpty.FamilyUnions.ToListAsync());
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, partner, "民国十年", "不详", "谱载备注");
        await vm.SelectAsync(genealogy);
        Assert.Contains(vm.TargetTree.SelectMany(node => node.Details), detail => detail.Contains("民国十年") && detail.Contains("不详") && detail.Contains("谱载备注"));
        vm.SelectedPerson = vm.People.Single(p => p.Name == "主体");
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, vm.People.Single(p => p.Name == "另一方").Id);
        await using var verify = database.CreateContext();
        var unions = await verify.FamilyUnions.ToListAsync();
        Assert.Equal(2, unions.Count);
        Assert.Contains(unions, u => u.StartDateText == "民国十年" && u.EndDateText == "不详" && u.Note == "谱载备注");
        Assert.Contains(unions, u => u.StartDateText is null && u.EndDateText is null && u.Note is null);
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Relationship_notes_and_sources_persist_and_reject_invalid_requests()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "主体"; await vm.AddPersonAsync();
        vm.NewPersonName = "父母"; await vm.AddPersonAsync();
        vm.SelectedPerson = vm.People.Single(p => p.Name == "主体");
        var parentId = vm.People.Single(p => p.Name == "父母").Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetRelationshipAsync(PersonRelationshipKind.BiologicalParent, parentId, new string('注', 2001), "来源"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetRelationshipAsync(PersonRelationshipKind.BiologicalParent, parentId, "备注", new string('源', 1001)));
        Guid foreignId;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            var foreign = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
            foreignId = (await service.AddPersonAsync(new Person { GenealogyId = foreign.Id, Name = "外谱人物" })).Id;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetRelationshipAsync(PersonRelationshipKind.BiologicalParent, foreignId, "备注", "来源"));
        await using (var verifyEmpty = database.CreateContext()) Assert.Empty(await verifyEmpty.PersonRelationships.ToListAsync());
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.BiologicalParent, parentId, "谱载备注", "旧谱卷一");
        await using (var verify = database.CreateContext())
        {
            var relation = await verify.PersonRelationships.SingleAsync();
            Assert.Equal("谱载备注", relation.Note);
            Assert.Equal("旧谱卷一", relation.SourceCitation);
            Assert.Equal(genealogy.Id, relation.GenealogyId);
        }
        await vm.SelectAsync(genealogy);
        Assert.Contains(vm.TargetTree.SelectMany(node => node.Details), detail => detail.Contains("谱载备注") && detail.Contains("旧谱卷一"));
        vm.SelectedPerson = vm.People.Single(p => p.Name == "主体");
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.LostAncestry, null);
        await using var final = database.CreateContext();
        var empty = await final.PersonRelationships.SingleAsync(r => r.Kind == PersonRelationshipKind.LostAncestry);
        Assert.Null(empty.Note);
        Assert.Null(empty.SourceCitation);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Empty_branch_deletion_protects_people_children_and_stale_or_foreign_targets()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewBranchName = "长房";
        await vm.CreateRootBranchAsync();
        var root = vm.Branches.Single();
        vm.SelectedParentBranch = vm.ParentBranchChoices.Single();
        vm.NewChildBranchName = "次支";
        await vm.CreateChildBranchAsync();
        var child = vm.Branches.Single(b => b.Name == "次支");
        vm.BranchToRename = vm.ParentBranchChoices.Single(b => b.Id == root.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteEmptyBranchAsync(vm.GetBranchDeleteCandidate()));
        vm.NewPersonName = "人物";
        await vm.AddPersonAsync();
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(vm.TargetTree.Single(), "人物", PersonSex.Unknown,
            null, null, null, null, null, updateBranch: true, branchId: child.Id);
        vm.BranchToRename = vm.ParentBranchChoices.Single(b => b.Id == child.Id);
        var candidate = vm.GetBranchDeleteCandidate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteEmptyBranchAsync(candidate));
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            var foreign = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteEmptyBranchAsync(foreign.Id, root.Id, root.Name, root.ParentBranchId, root.SortOrder));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteEmptyBranchAsync(genealogy.Id, root.Id, "旧名称", root.ParentBranchId, root.SortOrder));
            Assert.Equal(2, await context.FamilyBranches.CountAsync(b => b.GenealogyId == genealogy.Id));
            Assert.Equal(child.Id, (await context.People.SingleAsync()).BranchId);
        }
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(vm.TargetTree.Single(), "人物", PersonSex.Unknown,
            null, null, null, null, null, updateBranch: true, branchId: null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteEmptyBranchAsync(candidate));
        vm.BranchToRename = vm.ParentBranchChoices.Single(b => b.Id == child.Id);
        await vm.DeleteEmptyBranchAsync(vm.GetBranchDeleteCandidate());
        Assert.Single(vm.Branches);
        Assert.Single(vm.People);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Renaming_branches_preserves_hierarchy_and_rejects_conflicts_and_foreign_targets()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "人物";
        await vm.AddPersonAsync();
        vm.NewBranchName = "长房";
        await vm.CreateRootBranchAsync();
        var root = vm.Branches.Single().Id;
        vm.NewBranchName = "次房";
        await vm.CreateRootBranchAsync();
        vm.SelectedParentBranch = vm.ParentBranchChoices.Single(b => b.Id == root);
        vm.NewChildBranchName = "一支";
        await vm.CreateChildBranchAsync();
        var child = vm.Branches.Single(b => b.Name == "一支").Id;
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(vm.TargetTree.Single(), "人物", PersonSex.Unknown,
            null, null, null, "七世", "长", updateBranch: true, branchId: child);
        vm.BranchToRename = vm.ParentBranchChoices.Single(b => b.Id == root);
        vm.RenamedBranchName = "次房";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenameBranchAsync());
        vm.RenamedBranchName = " ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenameBranchAsync());
        vm.RenamedBranchName = "新长房";
        await vm.RenameBranchAsync();
        Assert.Contains(vm.ParentBranchChoices, b => b.Id == child && b.Label.Contains("新长房 / 一支"));
        Assert.Equal(child, vm.People.Single().BranchId);
        var stale = vm.ParentBranchChoices.Single(b => b.Id == child);
        vm.BranchToRename = stale;
        vm.RenamedBranchName = "新一支";
        await vm.RenameBranchAsync();
        Assert.Equal(child, vm.People.Single().BranchId);
        Assert.Contains(vm.ParentBranchChoices, b => b.Id == child && b.Label.Contains("新长房 / 新一支"));
        vm.BranchToRename = stale;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenameBranchAsync());
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        await service.CreateChildBranchAsync(genealogy.Id, root, "二支");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameBranchAsync(genealogy.Id, child, "新一支", root, "二支"));
        var foreign = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameBranchAsync(foreign.Id, child, "新一支", root, "误改"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameBranchAsync(genealogy.Id, Guid.NewGuid(), "新一支", root, "误改"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameBranchAsync(genealogy.Id, child, "新一支", root, new string('房', 201)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameBranchAsync(genealogy.Id, child, "旧名称", root, "误改"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameBranchAsync(genealogy.Id, child, "新一支", null, "误改"));
        var saved = await context.FamilyBranches.SingleAsync(b => b.Id == child);
        Assert.Equal(root, saved.ParentBranchId);
        Assert.Equal("新一支", saved.Name);
        Assert.Equal(child, (await context.People.SingleAsync()).BranchId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Child_branches_require_current_parent_and_reject_duplicate_or_stale_parent()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewBranchName = "长房";
        await vm.CreateRootBranchAsync();
        var root = vm.ParentBranchChoices.Single();
        vm.SelectedParentBranch = root;
        vm.NewChildBranchName = "次支";
        await vm.CreateChildBranchAsync();
        var child = vm.ParentBranchChoices.Single(b => b.Label.Contains("长房 / 次支"));
        vm.SelectedParentBranch = child;
        vm.NewChildBranchName = "三支";
        await vm.CreateChildBranchAsync();
        Assert.Contains(vm.ParentBranchChoices, b => b.Label.Contains("长房 / 次支 / 三支"));
        vm.SelectedParentBranch = vm.ParentBranchChoices.Single(b => b.Id == root.Id);
        vm.NewChildBranchName = "次支";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateChildBranchAsync());
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            var foreign = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
            await service.CreateRootBranchAsync(foreign.Id, "外谱" );
            var foreignRoot = await context.FamilyBranches.SingleAsync(b => b.GenealogyId == foreign.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateChildBranchAsync(first.Id, foreignRoot.Id, "误创建"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateChildBranchAsync(first.Id, Guid.NewGuid(), "误创建"));
            await context.FamilyBranches.Where(b => b.Id == foreignRoot.Id).ExecuteDeleteAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateChildBranchAsync(foreign.Id, foreignRoot.Id, "误创建"));
            Assert.Equal(3, await context.FamilyBranches.CountAsync(b => b.GenealogyId == first.Id));
        }
        var stale = vm.SelectedParentBranch!;
        await vm.SelectAsync(first);
        vm.SelectedParentBranch = stale;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateChildBranchAsync());
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.FamilyBranches.CountAsync(b => b.GenealogyId == first.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Root_branch_creation_and_assignment_reject_invalid_scope_and_preserve_profile()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "人物";
        await vm.AddPersonAsync();
        vm.NewBranchName = "长房";
        await vm.CreateRootBranchAsync();
        var branch = Assert.Single(vm.Branches);
        await using (var setup = database.CreateContext())
        {
            setup.FamilyBranches.Add(new FamilyBranch { GenealogyId = genealogy.Id, ParentBranchId = branch.Id, Name = "次支" });
            await setup.SaveChangesAsync();
        }
        await vm.SelectAsync(genealogy);
        var nested = vm.Branches.Single(b => b.Name == "次支");
        vm.NewBranchName = "长房";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateRootBranchAsync());
        vm.NewBranchName = " ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateRootBranchAsync());
        var personId = vm.People.Single().Id;
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(vm.TargetTree.Single(), "人物", PersonSex.Unknown,
            null, null, null, "七世", "长", updateBranch: true, branchId: branch.Id);
        Assert.Equal(branch.Id, vm.People.Single().BranchId);
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(vm.TargetTree.Single(), "人物", PersonSex.Unknown,
            null, null, null, "七世", "长", updateBranch: true, branchId: nested.Id);
        Assert.Equal(nested.Id, vm.People.Single().BranchId);
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(vm.TargetTree.Single(), "人物", PersonSex.Unknown,
            null, null, null, "七世", "长", updateBranch: true, branchId: branch.Id);
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            var foreign = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
            await service.CreateRootBranchAsync(foreign.Id, "外谱房支");
            var foreignBranch = await context.FamilyBranches.SingleAsync(b => b.GenealogyId == foreign.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdatePersonBasicProfileWithRecordedValuesAsync(
                genealogy.Id, personId, "误改", PersonSex.Unknown, null, null, null, null, null,
                updateBranch: true, branchId: foreignBranch.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdatePersonBasicProfileWithRecordedValuesAsync(
                genealogy.Id, personId, "误改", PersonSex.Unknown, null, null, null, null, null,
                updateBranch: true, branchId: Guid.NewGuid()));
            var saved = await context.People.SingleAsync(p => p.Id == personId);
            Assert.Equal("人物", saved.Name);
            Assert.Equal(branch.Id, saved.BranchId);
            Assert.Equal("七世", saved.RecordedGeneration);
            await context.FamilyBranches.Where(b => b.Id == nested.Id).ExecuteDeleteAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileWithRecordedValuesAsync(
            vm.TargetTree.Single(), "误改", PersonSex.Unknown, null, null, null, null, null, updateBranch: true, branchId: nested.Id));
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(vm.TargetTree.Single(), "人物", PersonSex.Unknown,
            null, null, null, "七世", "长", updateBranch: true, branchId: null);
        Assert.Null(vm.People.Single().BranchId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Recorded_values_are_scoped_validated_and_preserve_derived_values()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        Person person;
        await using (var context = database.CreateContext())
            person = await new GenealogyDataService(context).AddPersonAsync(new Person
            { GenealogyId = genealogy.Id, Name = "人物", DerivedGeneration = 8, DerivedBirthOrder = 2, Biography = "保留" });
        await vm.SelectAsync(genealogy);
        var node = vm.TargetTree.Single();
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(node, "人物", PersonSex.Unknown, null, null, "保留", "第七世", "长");
        Assert.Equal("第七世", vm.People.Single().RecordedGeneration);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileWithRecordedValuesAsync(
            vm.TargetTree.Single(), "人物", PersonSex.Unknown, null, null, "改动", new string('世', 101), "次"));
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            var otherGenealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdatePersonBasicProfileWithRecordedValuesAsync(
                otherGenealogy.Id, person.Id, "跨谱", PersonSex.Unknown, null, null, null, "八", "次"));
            var saved = await context.People.SingleAsync();
            Assert.Equal("第七世", saved.RecordedGeneration);
            Assert.Equal("长", saved.RecordedBirthOrder);
            Assert.Equal(8, saved.DerivedGeneration);
            Assert.Equal(2, saved.DerivedBirthOrder);
            Assert.Equal("保留", saved.Biography);
        }
        await vm.SaveTreePersonProfileWithRecordedValuesAsync(vm.TargetTree.Single(), "人物", PersonSex.Unknown, null, null, "保留", null, null);
        Assert.Null(vm.People.Single().RecordedGeneration);
        Assert.Null(vm.People.Single().RecordedBirthOrder);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Search_finds_current_genealogy_namesakes_and_rejects_stale_results()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "张同名" });
            await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "张同名" });
        }
        await vm.SelectAsync(first);
        vm.SearchText = " 同名 ";
        Assert.Equal(2, vm.SearchResults.Count);
        var stale = vm.SearchResults[0];
        Assert.Equal(stale.Id, vm.GetSearchResultNode(stale).PersonId);
        vm.SearchText = "没有匹配";
        Assert.Empty(vm.SearchResults);
        Assert.Throws<InvalidOperationException>(() => vm.GetSearchResultNode(stale));
        vm.SearchText = "同名";
        stale = vm.SearchResults[0];
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        Assert.Empty(vm.SearchResults);
        Assert.Throws<InvalidOperationException>(() => vm.GetSearchResultNode(stale));
        vm.NewPersonName = "张同名";
        await vm.AddPersonAsync();
        Assert.Single(vm.SearchResults);
        Assert.Equal(vm.SelectedGenealogy!.Id, vm.SearchResults[0].GenealogyId);
        vm.SearchText = " ";
        Assert.Empty(vm.SearchResults);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_shortcuts_resolve_selected_person_by_id_and_reject_missing_or_foreign_selection()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext);
        Assert.Contains("族谱", Assert.Throws<InvalidOperationException>(() => vm.GetSelectedTreePersonNode()).Message);
        vm.NewName = "甲谱";
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        Assert.Contains("人物", Assert.Throws<InvalidOperationException>(() => vm.GetSelectedTreePersonNode()).Message);
        Person firstPerson, namesake;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            firstPerson = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名" });
            namesake = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名" });
        }
        await vm.SelectAsync(first);
        vm.SelectedPerson = vm.People.Single(p => p.Id == namesake.Id);
        var selectedNode = vm.GetSelectedTreePersonNode();
        Assert.Equal(namesake.Id, selectedNode.PersonId);
        await vm.AddBiologicalChildForTreeNodeAsync(selectedNode, "快捷子女");
        await using (var verify = database.CreateContext())
        {
            Assert.Equal(namesake.Id, (await verify.PersonRelationships.SingleAsync()).RelatedPersonId);
            Assert.Equal("同名", (await verify.People.SingleAsync(p => p.Id == firstPerson.Id)).Name);
        }
        vm.SelectedPerson = vm.People.Single(p => p.Id == namesake.Id);
        await vm.SaveTreePersonProfileAsync(vm.GetSelectedTreePersonNode(), "快捷编辑", PersonSex.Female, null, null, "档案");
        Assert.Contains(vm.People, p => p.Id == namesake.Id && p.Name == "快捷编辑");
        vm.SelectedPerson = vm.People.Single(p => p.Id == firstPerson.Id);
        var foreign = vm.SelectedPerson;
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        Assert.Throws<InvalidOperationException>(() => vm.GetSelectedTreePersonNode());
        vm.SelectedPerson = foreign;
        Assert.Throws<InvalidOperationException>(() => vm.GetSelectedTreePersonNode());
        vm.ClearSelectedPerson();
        Assert.Throws<InvalidOperationException>(() => vm.GetSelectedTreePersonNode());
        await using var final = database.CreateContext();
        Assert.Single(await final.PersonRelationships.ToListAsync());
        Assert.Equal("同名", (await final.People.SingleAsync(p => p.Id == firstPerson.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tree_profile_save_targets_clicked_person_and_rejects_stale_or_foreign_nodes()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        Person firstPerson, namesake;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            firstPerson = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名", Biography = "甲档案" });
            namesake = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名", Biography = "乙档案", RecordedGeneration = "谱载七世" });
        }
        await vm.SelectAsync(first);
        vm.SelectedPerson = vm.People.Single(p => p.Id == firstPerson.Id);
        var clicked = vm.TargetTree.Single(n => n.PersonId == namesake.Id);
        Assert.Equal(namesake.Id, vm.GetTreePerson(clicked).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileAsync(clicked, "  ", PersonSex.Female, "出生", null, "不保存"));
        await vm.SaveTreePersonProfileAsync(clicked, " 新姓名 ", PersonSex.Female, "出生", "逝世", "新生平");
        await using (var verify = database.CreateContext())
        {
            var unchanged = await verify.People.SingleAsync(p => p.Id == firstPerson.Id);
            var saved = await verify.People.SingleAsync(p => p.Id == namesake.Id);
            Assert.Equal("同名", unchanged.Name);
            Assert.Equal("甲档案", unchanged.Biography);
            Assert.Equal("新姓名", saved.Name);
            Assert.Equal(PersonSex.Female, saved.Sex);
            Assert.Equal("出生", saved.BirthDateText);
            Assert.Equal("逝世", saved.DeathDateText);
            Assert.Equal("新生平", saved.Biography);
            Assert.Equal("谱载七世", saved.RecordedGeneration);
        }
        Assert.Contains(vm.TargetTree, n => n.PersonId == namesake.Id && n.Label.Contains("新姓名"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileAsync(clicked, "旧节点", PersonSex.Unknown, null, null, null));
        var currentNode = vm.TargetTree.Single(n => n.PersonId == namesake.Id);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileAsync(currentNode, "跨谱", PersonSex.Unknown, null, null, null));
        await using var foreignContext = database.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GenealogyDataService(foreignContext)
            .UpdatePersonBasicProfileAsync(vm.SelectedGenealogy!.Id, namesake.Id, "跨谱", PersonSex.Unknown, null, null, null));
        Assert.Equal("新姓名", (await foreignContext.People.SingleAsync(p => p.Id == namesake.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tree_profile_save_invalid_or_database_failure_keeps_all_original_fields()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "失败谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "原姓名";
        await vm.AddPersonAsync();
        var node = Assert.Single(vm.TargetTree);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileAsync(node, "新姓名", PersonSex.Female, new string('甲', 101), null, "新生平"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileAsync(node, "新姓名", (PersonSex)999, null, null, null));
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectProfile BEFORE UPDATE ON People BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => vm.SaveTreePersonProfileAsync(node, "新姓名", PersonSex.Female, "新出生", null, "新生平"));
        await using var verify = database.CreateContext();
        var person = await verify.People.SingleAsync();
        Assert.Equal("原姓名", person.Name);
        Assert.Equal(PersonSex.Unknown, person.Sex);
        Assert.Null(person.BirthDateText);
        Assert.Null(person.Biography);
        Assert.Equal("原姓名", Assert.Single(vm.People).Name);
        Assert.Contains(vm.TargetTree, item => item.PersonId == person.Id && item.Label.Contains("原姓名"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tree_context_add_targets_right_clicked_id_and_rejects_stale_or_foreign_nodes()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        Person left, right;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            left = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名" });
            right = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名" });
        }
        await vm.SelectAsync(first);
        vm.SelectedPerson = vm.People.Single(p => p.Id == left.Id);
        var clicked = vm.TargetTree.Single(n => n.PersonId == right.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildForTreeNodeAsync(clicked, "  "));
        await vm.AddBiologicalChildForTreeNodeAsync(clicked, "目标子女");
        await using (var verify = database.CreateContext())
        {
            var relation = await verify.PersonRelationships.SingleAsync();
            Assert.Equal(right.Id, relation.RelatedPersonId);
            Assert.Equal("目标子女", (await verify.People.SingleAsync(p => p.Id == relation.PersonId)).Name);
            Assert.Equal(first.Id, relation.GenealogyId);
        }
        Assert.Contains(vm.TargetTree, n => n.Label.Contains("目标子女"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildForTreeNodeAsync(clicked, "过期子女"));
        var currentNode = vm.TargetTree.Single(n => n.PersonId == right.Id);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildForTreeNodeAsync(currentNode, "跨谱子女"));
        await using var final = database.CreateContext();
        Assert.Equal(3, await final.People.CountAsync());
        Assert.Single(await final.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tree_context_add_rolls_back_person_if_relationship_insert_fails()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "失败谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "父母";
        await vm.AddPersonAsync();
        var node = Assert.Single(vm.TargetTree);
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectTreeChild BEFORE INSERT ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => vm.AddBiologicalChildForTreeNodeAsync(node, "子女"));
        await using var verify = database.CreateContext();
        Assert.Single(await verify.People.ToListAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        Assert.Equal("父母", Assert.Single(vm.People).Name);
        Assert.Single(vm.TargetTree);
        Assert.DoesNotContain(vm.TargetTree, item => item.Label.Contains("子女"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tree_selection_uses_person_id_and_clears_on_switch_or_cancel()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var second = vm.SelectedGenealogy!;
        Person firstPerson, namesake, child, foreign;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            firstPerson = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名", Biography = "甲档案" });
            namesake = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名", Biography = "乙档案" });
            child = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "子女" });
            foreign = await service.AddPersonAsync(new Person { GenealogyId = second.Id, Name = "同名", Biography = "外谱" });
            await service.AddExistingBiologicalChildAsync(first.Id, firstPerson.Id, child.Id);
        }

        await vm.SelectAsync(first);
        var oldNode = vm.TargetTree.Single(n => n.PersonId == firstPerson.Id);
        await vm.SelectTreePersonAsync(oldNode);
        Assert.Equal(firstPerson.Id, vm.SelectedPerson!.Id);
        Assert.Equal("甲档案", vm.EditedBiography);
        Assert.Equal(child.Id, Assert.Single(vm.BiologicalChildren).Id);

        await vm.SelectTreePersonAsync(vm.TargetTree.Single(n => n.PersonId == namesake.Id));
        Assert.Equal(namesake.Id, vm.SelectedPerson!.Id);
        Assert.Equal("乙档案", vm.EditedBiography);
        Assert.Empty(vm.BiologicalChildren);

        vm.ClearSelectedPerson();
        Assert.Null(vm.SelectedPerson);
        Assert.Null(vm.EditedBiography);
        Assert.Empty(vm.BiologicalChildren);
        await vm.SelectTreePersonAsync(oldNode);
        Assert.Equal(firstPerson.Id, vm.SelectedPerson!.Id);

        await vm.SelectAsync(second);
        Assert.Null(vm.SelectedPerson);
        Assert.Empty(vm.BiologicalChildren);
        await vm.SelectTreePersonAsync(oldNode);
        Assert.Null(vm.SelectedPerson);
        await vm.SelectTreePersonAsync(Assert.Single(vm.TargetTree));
        Assert.Equal(foreign.Id, vm.SelectedPerson!.Id);
        Assert.Equal("外谱", vm.EditedBiography);
        await vm.SelectAsync(null);
        Assert.Null(vm.SelectedPerson);
        Assert.Null(vm.EditedBiography);
        Assert.Empty(vm.BiologicalChildren);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_and_switch_show_only_people_in_selected_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext);
        vm.NewName = "  甲谱  ";
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        Assert.Equal("甲谱", first.Name);

        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var second = vm.SelectedGenealogy!;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "甲人物" });
            await service.AddPersonAsync(new Person { GenealogyId = second.Id, Name = "乙人物" });
        }

        await vm.SelectAsync(first);
        Assert.Equal("甲人物", Assert.Single(vm.People).Name);
        await vm.SelectAsync(second);
        Assert.Equal("乙人物", Assert.Single(vm.People).Name);
        await vm.SelectAsync(null);
        Assert.Empty(vm.People);

        var newSession = new GenealogySelectionViewModel(database.CreateContext);
        await newSession.LoadAsync();
        Assert.Equal(2, newSession.Genealogies.Count);
        Assert.Null(newSession.SelectedGenealogy);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Invalid_or_duplicate_name_keeps_existing_selection()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var selectedId = vm.SelectedGenealogy!.Id;

        vm.NewName = "  ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateAsync());
        vm.NewName = "甲谱";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateAsync());
        Assert.Equal(selectedId, vm.SelectedGenealogy!.Id);
        Assert.Single(vm.Genealogies);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Adding_people_uses_selected_genealogy_and_switching_keeps_them_separate()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewPersonName = "  虚构甲  ";
        await vm.AddPersonAsync();
        Assert.Equal("虚构甲", Assert.Single(vm.People).Name);

        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var second = vm.SelectedGenealogy!;
        Assert.Empty(vm.People);
        vm.NewPersonName = "虚构乙";
        await vm.AddPersonAsync();
        Assert.Equal("虚构乙", Assert.Single(vm.People).Name);
        await vm.SelectAsync(first);
        Assert.Equal(first.Id, Assert.Single(vm.People).GenealogyId);
        Assert.Equal("虚构甲", vm.People[0].Name);
        await vm.SelectAsync(second);
        Assert.Equal("虚构乙", Assert.Single(vm.People).Name);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Person_creation_rejects_missing_selection_invalid_name_and_deleted_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewPersonName = "虚构人物" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddPersonAsync());
        vm.NewName = "甲谱";
        await vm.CreateAsync();
        vm.NewPersonName = "   ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddPersonAsync());
        vm.NewPersonName = new string('甲', 201);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddPersonAsync());
        vm.NewPersonName = "虚构人物";
        await using (var context = database.CreateContext())
        {
            var genealogy = await context.Genealogies.SingleAsync();
            context.Genealogies.Remove(genealogy);
            await context.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddPersonAsync());
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.People.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Rename_updates_only_selected_person_and_preserves_other_fields()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        await using (var context = database.CreateContext())
            await new GenealogyDataService(context).AddPersonAsync(new Person
            {
                GenealogyId = first.Id, Name = "原名", Biography = "保留资料", RecordedGeneration = "谱载七世"
            });
        await vm.SelectAsync(first);
        vm.SelectedPerson = Assert.Single(vm.People);
        Assert.Equal("原名", vm.EditedPersonName);
        vm.EditedPersonName = "  新名  ";
        await vm.RenamePersonAsync();
        Assert.Equal("新名", Assert.Single(vm.People).Name);
        await using var verification = database.CreateContext();
        var saved = await verification.People.SingleAsync();
        Assert.Equal("保留资料", saved.Biography);
        Assert.Equal("谱载七世", saved.RecordedGeneration);
        Assert.Equal(first.Id, saved.GenealogyId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Rename_rejects_cross_genealogy_invalid_or_missing_person()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        var foreign = Assert.Single(vm.People);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.NewPersonName = "乙人物";
        await vm.AddPersonAsync();
        var secondPerson = Assert.Single(vm.People);

        vm.SelectedPerson = foreign;
        vm.EditedPersonName = "不能跨谱修改";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenamePersonAsync());
        vm.SelectedPerson = secondPerson;
        vm.EditedPersonName = "   ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenamePersonAsync());
        vm.EditedPersonName = new string('甲', 201);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenamePersonAsync());
        vm.SelectedPerson = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenamePersonAsync());
        await vm.SelectAsync(first);
        Assert.Equal("甲人物", Assert.Single(vm.People).Name);
        await using var verification = database.CreateContext();
        Assert.Equal(new[] { "乙人物", "甲人物" }, await verification.People.OrderBy(p => p.Name).Select(p => p.Name).ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Add_child_creates_child_and_biological_parent_relationship_in_current_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "虚构父母";
        await vm.AddPersonAsync();
        var parent = Assert.Single(vm.People);
        vm.SelectedPerson = parent;
        vm.NewChildName = "  虚构子女  ";
        await vm.AddBiologicalChildAsync();
        Assert.Contains(vm.People, p => p.Name == "虚构子女" && p.GenealogyId == genealogy.Id);
        Assert.Equal(string.Empty, vm.NewChildName);
        await using var verify = database.CreateContext();
        var child = await verify.People.SingleAsync(p => p.Name == "虚构子女");
        var relation = await verify.PersonRelationships.SingleAsync();
        Assert.Equal(genealogy.Id, relation.GenealogyId);
        Assert.Equal(child.Id, relation.PersonId);
        Assert.Equal(parent.Id, relation.RelatedPersonId);
        Assert.Equal(PersonRelationshipKind.BiologicalParent, relation.Kind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Add_child_rejects_invalid_input_and_foreign_or_missing_parent_without_writing_child()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewChildName = "子女" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        vm.NewName = "甲谱";
        await vm.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        vm.NewPersonName = "甲父母";
        await vm.AddPersonAsync();
        var foreignParent = Assert.Single(vm.People);
        vm.SelectedPerson = foreignParent;
        vm.NewChildName = "   ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        vm.NewChildName = new string('子', 201);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.SelectedPerson = foreignParent;
        vm.NewChildName = "跨谱子女";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        Assert.Contains("跨族谱", error.Message);
        vm.SelectedPerson = new Person { Id = Guid.NewGuid(), GenealogyId = vm.SelectedGenealogy!.Id, Name = "不存在" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        await using var verify = database.CreateContext();
        Assert.Single(await verify.People.ToListAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Relationship_insert_failure_rolls_back_new_child()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "写入失败谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "虚构父母" });
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectRelationships BEFORE INSERT ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => service.AddBiologicalChildAsync(genealogy.Id, parent.Id, "虚构子女"));
        await using var verify = database.CreateContext();
        Assert.Equal(parent.Id, (await verify.People.SingleAsync()).Id);
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Clearing_parent_selection_reports_no_parent_and_rejects_child_creation()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "选择测试谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "虚构父母";
        await vm.AddPersonAsync();
        vm.SelectedPerson = Assert.Single(vm.People);
        Assert.Contains("添加子女的父母：虚构父母", vm.SelectedParentStatus);
        Assert.Contains(vm.SelectedPerson!.Id.ToString(), vm.SelectedParentStatus);
        vm.ClearSelectedPerson();
        Assert.Null(vm.SelectedPerson);
        Assert.Equal("添加子女的父母：未选择", vm.SelectedParentStatus);
        vm.NewChildName = "虚构子女";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        Assert.Equal("请先选择父母人物。", exception.Message);
        await using var verification = database.CreateContext();
        Assert.Single(await verification.People.ToListAsync());
        Assert.Empty(await verification.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Selected_person_shows_only_their_same_genealogy_biological_children()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewPersonName = "甲父母";
        await vm.AddPersonAsync();
        var parent = Assert.Single(vm.People);
        vm.SelectedPerson = parent;
        vm.NewChildName = "生物子女";
        await vm.AddBiologicalChildAsync();
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            var adopted = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "收养子女" });
            await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = first.Id, PersonId = adopted.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.AdoptiveParent });
        }
        await vm.SelectAsync(first);
        vm.SelectedPerson = vm.People.Single(p => p.Id == parent.Id);
        await vm.LoadSelectedBiologicalChildrenAsync();
        Assert.Equal("生物子女", Assert.Single(vm.BiologicalChildren).Name);
        vm.SelectedPerson = vm.People.Single(p => p.Name == "收养子女");
        Assert.Empty(vm.BiologicalChildren);
        await vm.LoadSelectedBiologicalChildrenAsync();
        Assert.Empty(vm.BiologicalChildren);
        Assert.Contains("0 人", vm.ChildrenStatus);

        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.NewPersonName = "乙父母";
        await vm.AddPersonAsync();
        vm.SelectedPerson = Assert.Single(vm.People);
        vm.NewChildName = "乙子女";
        await vm.AddBiologicalChildAsync();
        await vm.SelectAsync(first);
        vm.SelectedPerson = vm.People.Single(p => p.Id == parent.Id);
        await vm.LoadSelectedBiologicalChildrenAsync();
        Assert.Equal("生物子女", Assert.Single(vm.BiologicalChildren).Name);
        vm.ClearSelectedPerson();
        Assert.Empty(vm.BiologicalChildren);
        Assert.Equal("请选择人物查看生物子女", vm.ChildrenStatus);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Biological_children_query_is_scoped_and_does_not_return_foreign_parent_children()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var first = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var second = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "甲父母" });
        await service.AddBiologicalChildAsync(first.Id, parent.Id, "甲子女");
        Assert.Single(await service.GetBiologicalChildrenAsync(first.Id, parent.Id));
        Assert.Empty(await service.GetBiologicalChildrenAsync(second.Id, parent.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Profile_edit_round_trips_text_and_preserves_genealogy_and_recorded_values()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        await using (var context = database.CreateContext())
            await new GenealogyDataService(context).AddPersonAsync(new Person
            {
                GenealogyId = genealogy.Id, Name = "虚构人物", RecordedGeneration = "谱载七世", DerivedGeneration = 8,
                RecordedBirthOrder = "长", DerivedBirthOrder = 1, PhotoPath = "保留照片"
            });
        await vm.SelectAsync(genealogy);
        vm.SelectedPerson = Assert.Single(vm.People);
        Assert.Equal(PersonSex.Unknown, vm.EditedSex);
        vm.EditedSex = PersonSex.Female;
        vm.EditedBirthDate = "清末某年";
        vm.EditedDeathDate = "失考";
        vm.EditedBiography = "虚构生平";
        await vm.SavePersonProfileAsync();
        await using var verify = database.CreateContext();
        var saved = await verify.People.SingleAsync();
        Assert.Equal(genealogy.Id, saved.GenealogyId);
        Assert.Equal("虚构人物", saved.Name);
        Assert.Equal(PersonSex.Female, saved.Sex);
        Assert.Equal("清末某年", saved.BirthDateText);
        Assert.Equal("失考", saved.DeathDateText);
        Assert.Equal("虚构生平", saved.Biography);
        Assert.Equal("谱载七世", saved.RecordedGeneration);
        Assert.Equal(8, saved.DerivedGeneration);
        Assert.Equal("长", saved.RecordedBirthOrder);
        Assert.Equal(1, saved.DerivedBirthOrder);
        Assert.Equal("保留照片", saved.PhotoPath);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Profile_edit_rejects_foreign_selection_and_invalid_values_without_modification()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        var foreign = Assert.Single(vm.People);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.NewPersonName = "乙人物";
        await vm.AddPersonAsync();
        var current = Assert.Single(vm.People);
        vm.SelectedPerson = foreign;
        vm.EditedBiography = "不应写入";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SavePersonProfileAsync());
        vm.SelectedPerson = current;
        vm.EditedBirthDate = new string('甲', 101);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SavePersonProfileAsync());
        vm.EditedBirthDate = null;
        vm.EditedBiography = new string('甲', 4001);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SavePersonProfileAsync());
        vm.EditedBiography = null;
        vm.EditedSex = (PersonSex)999;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SavePersonProfileAsync());
        await using var verify = database.CreateContext();
        Assert.All(await verify.People.ToListAsync(), p => { Assert.Null(p.Biography); Assert.Null(p.BirthDateText); Assert.Equal(PersonSex.Unknown, p.Sex); });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Delete_unrelated_person_is_scoped_to_selected_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        var foreign = Assert.Single(vm.People);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.NewPersonName = "乙人物";
        await vm.AddPersonAsync();
        var current = Assert.Single(vm.People);
        vm.SelectedPerson = foreign;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteUnrelatedPersonAsync());
        vm.SelectedPerson = current;
        await vm.DeleteUnrelatedPersonAsync();
        Assert.Empty(vm.People);
        Assert.Null(vm.SelectedPerson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteUnrelatedPersonAsync());
        await using var verify = database.CreateContext();
        Assert.Equal(foreign.Id, (await verify.People.SingleAsync()).Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Delete_rejects_relationship_subject_related_person_and_lost_ancestry()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "关系保护谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "父母" });
        var child = await service.AddBiologicalChildAsync(genealogy.Id, parent.Id, "子女");
        var lost = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "失考人物" });
        await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = lost.Id, Kind = PersonRelationshipKind.LostAncestry });
        foreach (var person in new[] { parent, child, lost })
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteUnrelatedPersonAsync(genealogy.Id, person.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.People.CountAsync());
        Assert.Equal(2, await verify.PersonRelationships.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Delete_rejects_either_partner_of_a_family_union()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "婚配保护谱" });
        var first = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        var second = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "乙" });
        context.FamilyUnions.Add(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = first.Id, PartnerTwoId = second.Id });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteUnrelatedPersonAsync(genealogy.Id, first.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteUnrelatedPersonAsync(genealogy.Id, second.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Single(await verify.FamilyUnions.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Remove_biological_parent_relation_preserves_people_and_other_relationships()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "关系谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "父母";
        await vm.AddPersonAsync();
        var parent = Assert.Single(vm.People);
        vm.SelectedPerson = parent;
        vm.NewChildName = "子女";
        await vm.AddBiologicalChildAsync();
        var child = vm.People.Single(p => p.Name == "子女");
        await using (var context = database.CreateContext())
        {
            await new GenealogyDataService(context).AddBiologicalChildAsync(genealogy.Id, parent.Id, "另一子女");
            await new GenealogyDataService(context).AddRelationshipAsync(new PersonRelationship
            {
                GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.AdoptiveParent
            });
        }
        vm.SelectedPerson = vm.People.Single(p => p.Id == parent.Id);
        await vm.LoadSelectedBiologicalChildrenAsync();
        vm.SelectedBiologicalChild = vm.BiologicalChildren.Single(p => p.Id == child.Id);
        await vm.RemoveBiologicalParentRelationshipAsync();
        Assert.Equal("另一子女", Assert.Single(vm.BiologicalChildren).Name);
        Assert.Null(vm.SelectedBiologicalChild);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveBiologicalParentRelationshipAsync());
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.People.CountAsync());
        Assert.Equal(2, await verify.PersonRelationships.CountAsync());
        Assert.Equal(PersonRelationshipKind.AdoptiveParent, (await verify.PersonRelationships.SingleAsync(r => r.PersonId == child.Id)).Kind);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GenealogyDataService(verify).RemoveBiologicalParentRelationshipAsync(genealogy.Id, parent.Id, child.Id));
        Assert.Equal(PersonRelationshipKind.AdoptiveParent, (await verify.PersonRelationships.SingleAsync(r => r.PersonId == child.Id)).Kind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Remove_relation_rejects_foreign_missing_or_wrong_kind_without_change()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var first = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var second = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "父母" });
        var child = await service.AddBiologicalChildAsync(first.Id, parent.Id, "子女");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveBiologicalParentRelationshipAsync(second.Id, parent.Id, child.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveBiologicalParentRelationshipAsync(first.Id, child.Id, parent.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveBiologicalParentRelationshipAsync(first.Id, parent.Id, Guid.NewGuid()));
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Single(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Failed_relation_removal_leaves_people_and_relation_intact()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "失败谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "父母" });
        var child = await service.AddBiologicalChildAsync(genealogy.Id, parent.Id, "子女");
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectRelationshipDelete BEFORE DELETE ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => service.RemoveBiologicalParentRelationshipAsync(genealogy.Id, parent.Id, child.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Single(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_people_can_be_linked_and_relinked_without_recreating_records()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "关联谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "父母";
        await vm.AddPersonAsync();
        vm.NewPersonName = "已有子女";
        await vm.AddPersonAsync();
        var parent = vm.People.Single(p => p.Name == "父母");
        var child = vm.People.Single(p => p.Name == "已有子女");
        vm.SelectedPerson = parent;
        vm.SelectedExistingChild = child;
        await vm.AddExistingBiologicalChildAsync();
        Assert.Equal(child.Id, Assert.Single(vm.BiologicalChildren).Id);
        Assert.Contains(vm.TargetTree.Single(n => n.PersonId == parent.Id).Details, d => d.Contains(child.Id.ToString()));
        vm.SelectedExistingChild = child;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddExistingBiologicalChildAsync());
        vm.SelectedBiologicalChild = Assert.Single(vm.BiologicalChildren);
        await vm.RemoveBiologicalParentRelationshipAsync();
        Assert.DoesNotContain(vm.TargetTree.Single(n => n.PersonId == parent.Id).Details, d => d.Contains(child.Id.ToString()));
        vm.SelectedExistingChild = child;
        await vm.AddExistingBiologicalChildAsync();
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        var relation = await verify.PersonRelationships.SingleAsync();
        Assert.Equal(genealogy.Id, relation.GenealogyId);
        Assert.Equal(child.Id, relation.PersonId);
        Assert.Equal(parent.Id, relation.RelatedPersonId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_child_link_rejects_cross_genealogy_self_and_cycle()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var first = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var second = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "父母" });
        var child = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "子女" });
        var foreign = await service.AddPersonAsync(new Person { GenealogyId = second.Id, Name = "外谱" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExistingBiologicalChildAsync(first.Id, parent.Id, foreign.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExistingBiologicalChildAsync(first.Id, foreign.Id, child.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExistingBiologicalChildAsync(first.Id, parent.Id, parent.Id));
        await service.AddExistingBiologicalChildAsync(first.Id, parent.Id, child.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExistingBiologicalChildAsync(first.Id, child.Id, parent.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.People.CountAsync());
        Assert.Single(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_child_link_failure_keeps_people_and_writes_no_relation()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "关联失败谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "父母" });
        var child = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "子女" });
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectExistingChild BEFORE INSERT ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => service.AddExistingBiologicalChildAsync(genealogy.Id, parent.Id, child.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_child_link_and_unlink_failures_keep_the_current_view_and_graph_intact()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "关联视图失败谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "父母", "子女" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }

        var parent = vm.People.Single(person => person.Name == "父母");
        var child = vm.People.Single(person => person.Name == "子女");
        vm.SelectedPerson = parent;
        vm.SelectedExistingChild = child;
        await using (var failure = database.CreateContext())
            await failure.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectViewLink BEFORE INSERT ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated link failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => vm.AddExistingBiologicalChildAsync());
        Assert.Equal(child.Id, vm.SelectedExistingChild!.Id);
        Assert.Empty(vm.BiologicalChildren);
        Assert.DoesNotContain(vm.TargetTree.Single(item => item.PersonId == parent.Id).Details, detail => detail.Contains(child.Id.ToString()));

        await using (var cleanup = database.CreateContext())
            await cleanup.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectViewLink;");
        await vm.AddExistingBiologicalChildAsync();
        Assert.Equal(child.Id, Assert.Single(vm.BiologicalChildren).Id);
        vm.SelectedBiologicalChild = vm.BiologicalChildren.Single();
        await using (var failure = database.CreateContext())
            await failure.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectViewUnlink BEFORE DELETE ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated unlink failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => vm.RemoveBiologicalParentRelationshipAsync());
        Assert.Equal(child.Id, vm.SelectedBiologicalChild!.Id);
        Assert.Equal(child.Id, Assert.Single(vm.BiologicalChildren).Id);
        Assert.Contains(vm.TargetTree.Single(item => item.PersonId == parent.Id).Details, detail => detail.Contains(child.Id.ToString()));

        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync(item => item.GenealogyId == genealogy.Id));
        Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Selection_busy_retry_exhaustion_keeps_a_consistent_target_state_and_can_reload()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False;Default Timeout=1").Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        vm.SelectedPerson = Assert.Single(vm.People);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var second = vm.SelectedGenealogy!;

        await using var blocker = new SqliteConnection($"Data Source={database.DatabasePath};Pooling=False;Default Timeout=1");
        await blocker.OpenAsync();
        await using var command = blocker.CreateCommand();
        command.CommandText = "BEGIN EXCLUSIVE;";
        await command.ExecuteNonQueryAsync();
        try
        {
            var failure = await Assert.ThrowsAsync<SqliteException>(() => vm.SelectAsync(first));
            Assert.Equal(5, failure.SqliteErrorCode);
            Assert.Equal(first.Id, vm.SelectedGenealogy!.Id);
            Assert.Null(vm.SelectedPerson);
            Assert.Empty(vm.BiologicalChildren);
            Assert.Empty(vm.People);
            Assert.Empty(vm.TargetTree);
            Assert.StartsWith("加载目标谱系失败：", vm.Status);
        }
        finally
        {
            command.CommandText = "ROLLBACK;";
            await command.ExecuteNonQueryAsync();
        }

        await vm.SelectAsync(first);
        Assert.Equal(first.Id, vm.SelectedGenealogy!.Id);
        Assert.Equal("甲人物", Assert.Single(vm.People).Name);
        Assert.Single(vm.TargetTree);
        await using var verify = new ZupuDbContext(options);
        Assert.Equal(1, await verify.People.CountAsync(person => person.GenealogyId == first.Id));
        Assert.Equal(0, await verify.People.CountAsync(person => person.GenealogyId == second.Id));
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Committed_tree_and_biological_child_writes_report_refresh_failure_and_can_reload()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var refreshFailure = new FailRefreshAfterWriteInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(refreshFailure).Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "刷新失败谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "父母", "已有子女" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }

        var parentId = vm.People.Single(person => person.Name == "父母").Id;
        var existingChildId = vm.People.Single(person => person.Name == "已有子女").Id;

        refreshFailure.Arm("UPDATE \"People\"");
        await vm.SaveTreePersonProfileAsync(vm.TargetTree.Single(item => item.PersonId == parentId), "已保存父母", PersonSex.Male, null, null, "已保存档案");
        Assert.StartsWith("档案已保存，但目标谱系刷新失败：", vm.Status);
        Assert.Empty(vm.People);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
            Assert.Equal("已保存档案", (await verify.People.SingleAsync(item => item.Id == parentId)).Biography);
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);
        Assert.Contains(vm.People, item => item.Id == parentId && item.Name == "已保存父母");

        refreshFailure.Arm("INSERT INTO \"PersonRelationships\"");
        await vm.AddBiologicalChildForTreeNodeAsync(vm.TargetTree.Single(item => item.PersonId == parentId), "新子女");
        Assert.StartsWith("子女已保存，但目标谱系刷新失败：", vm.Status);
        Assert.Empty(vm.People);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
        {
            Assert.Equal(3, await verify.People.CountAsync(item => item.GenealogyId == genealogy.Id));
            Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        }
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);

        vm.SelectedPerson = vm.People.Single(item => item.Id == parentId);
        vm.SelectedExistingChild = vm.People.Single(item => item.Id == existingChildId);
        refreshFailure.Arm("INSERT INTO \"PersonRelationships\"");
        await vm.AddExistingBiologicalChildAsync();
        Assert.StartsWith("生物父子关系已建立，但目标谱系刷新失败：", vm.Status);
        Assert.StartsWith("加载生物子女失败：", vm.ChildrenStatus);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
            Assert.Equal(2, await verify.PersonRelationships.CountAsync(item => item.GenealogyId == genealogy.Id));
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == parentId);
        await vm.LoadSelectedBiologicalChildrenAsync();
        Assert.Equal(2, vm.BiologicalChildren.Count);

        vm.SelectedBiologicalChild = vm.BiologicalChildren.Single(item => item.Id == existingChildId);
        refreshFailure.Arm("DELETE FROM \"PersonRelationships\"");
        await vm.RemoveBiologicalParentRelationshipAsync();
        Assert.StartsWith("生物父子关系已解除，但目标谱系刷新失败：", vm.Status);
        Assert.StartsWith("加载生物子女失败：", vm.ChildrenStatus);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
            Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == parentId);
        await vm.LoadSelectedBiologicalChildrenAsync();
        Assert.DoesNotContain(vm.BiologicalChildren, item => item.Id == existingChildId);
        Assert.Single(vm.BiologicalChildren);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Committed_target_link_writes_report_refresh_failure_and_can_reload()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var refreshFailure = new FailRefreshAfterWriteInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(refreshFailure).Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "关系刷新失败谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙", "丙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }

        var firstId = vm.People.Single(person => person.Name == "甲").Id;
        var secondId = vm.People.Single(person => person.Name == "乙").Id;
        var thirdId = vm.People.Single(person => person.Name == "丙").Id;
        vm.SelectedPerson = vm.People.Single(person => person.Id == firstId);

        refreshFailure.Arm("INSERT INTO \"PersonRelationships\"");
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, secondId);
        Assert.StartsWith("人物关系已新增，但目标谱系刷新失败：", vm.Status);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
            Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);
        Assert.Equal(3, vm.TargetTree.Count);

        vm.SelectedPerson = vm.People.Single(person => person.Id == firstId);
        refreshFailure.Arm("INSERT INTO \"FamilyUnions\"");
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, thirdId);
        Assert.StartsWith("婚配关系已新增，但目标谱系刷新失败：", vm.Status);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
            Assert.Single(await verify.FamilyUnions.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);

        vm.SelectedPerson = vm.People.Single(person => person.Id == firstId);
        var relationship = (await vm.GetTargetLinkRemovalCandidatesAsync())
            .Single(item => item.Kind == TargetLinkRemovalKind.Relationship);
        refreshFailure.Arm("DELETE FROM \"PersonRelationships\"");
        await vm.RemoveTargetLinkAsync(relationship);
        Assert.StartsWith("人物关系已解除，但目标谱系刷新失败：", vm.Status);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
            Assert.Empty(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);

        vm.SelectedPerson = vm.People.Single(person => person.Id == firstId);
        var union = (await vm.GetTargetLinkRemovalCandidatesAsync())
            .Single(item => item.Kind == TargetLinkRemovalKind.FamilyUnion);
        refreshFailure.Arm("DELETE FROM \"FamilyUnions\"");
        await vm.RemoveTargetLinkAsync(union);
        Assert.StartsWith("婚配关系已解除，但目标谱系刷新失败：", vm.Status);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
            Assert.Empty(await verify.FamilyUnions.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);
        Assert.Equal(3, vm.People.Count);
        Assert.Equal(3, vm.TargetTree.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Removal_candidates_fail_closed_on_busy_retry_exhaustion_and_can_reload()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False;Default Timeout=1").Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "候选忙锁谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);

        await using var blocker = new SqliteConnection($"Data Source={database.DatabasePath};Pooling=False;Default Timeout=1");
        await blocker.OpenAsync();
        await using var command = blocker.CreateCommand();
        command.CommandText = "BEGIN EXCLUSIVE;";
        await command.ExecuteNonQueryAsync();
        try
        {
            var failure = await Assert.ThrowsAsync<SqliteException>(() => vm.GetTargetLinkRemovalCandidatesAsync());
            Assert.Equal(5, failure.SqliteErrorCode);
            Assert.Equal(genealogy.Id, vm.SelectedGenealogy!.Id);
            Assert.Equal(first.Id, vm.SelectedPerson!.Id);
        }
        finally
        {
            command.CommandText = "ROLLBACK;";
            await command.ExecuteNonQueryAsync();
        }

        var candidates = await vm.GetTargetLinkRemovalCandidatesAsync();
        Assert.Single(candidates, item => item.Kind == TargetLinkRemovalKind.Relationship);
        await using var verify = new ZupuDbContext(options);
        Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Removal_candidates_reject_stale_selection_during_snapshot_read()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var pause = new PauseCommandInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(pause).Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "甲谱" };
        await vm.CreateAsync();
        var firstGenealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var secondGenealogy = vm.SelectedGenealogy!;
        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(person => person.Id == first.Id);

        pause.Arm("SELECT");
        var candidates = vm.GetTargetLinkRemovalCandidatesAsync();
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await vm.SelectAsync(null);
        pause.Release();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => candidates);
        Assert.Contains("当前选择已变化", failure.Message);
        await vm.SelectAsync(secondGenealogy);
        Assert.Equal(secondGenealogy.Id, vm.SelectedGenealogy!.Id);
        Assert.Null(vm.SelectedPerson);
        await using var verify = database.CreateContext();
        Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == firstGenealogy.Id).ToListAsync());
        Assert.Empty(await verify.PersonRelationships.Where(item => item.GenealogyId == secondGenealogy.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_relationship_and_union_editing_is_scoped_to_selected_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var firstGenealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙", "丙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        var third = vm.People.Single(person => person.Name == "丙");
        vm.SelectedPerson = first;

        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, third.Id);
        Assert.Equal("已新增婚配关系。", vm.Status);

        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var secondGenealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "外谱人物";
        await vm.AddPersonAsync();
        var foreign = Assert.Single(vm.People);
        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(person => person.Id == first.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, foreign.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, foreign.Id));

        await using var verify = database.CreateContext();
        var relationship = await verify.PersonRelationships.SingleAsync();
        Assert.Equal(firstGenealogy.Id, relationship.GenealogyId);
        Assert.Equal(first.Id, relationship.PersonId);
        Assert.Equal(second.Id, relationship.RelatedPersonId);
        var union = await verify.FamilyUnions.SingleAsync();
        Assert.Equal(firstGenealogy.Id, union.GenealogyId);
        Assert.Contains(first.Id, new[] { union.PartnerOneId, union.PartnerTwoId!.Value });
        Assert.Contains(third.Id, new[] { union.PartnerOneId, union.PartnerTwoId!.Value });
        Assert.Equal(secondGenealogy.Id, foreign.GenealogyId);
        Assert.Equal(0, await verify.PersonRelationships.CountAsync(item => item.GenealogyId == secondGenealogy.Id));
        Assert.Equal(0, await verify.FamilyUnions.CountAsync(item => item.GenealogyId == secondGenealogy.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_relationship_editing_rejects_stale_or_foreign_selected_person_without_writing()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        var firstPerson = Assert.Single(vm.People);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.NewPersonName = "乙人物";
        await vm.AddPersonAsync();
        var secondPerson = Assert.Single(vm.People);
        vm.SelectedPerson = firstPerson;

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, secondPerson.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, secondPerson.Id));
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        Assert.Empty(await verify.FamilyUnions.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_link_removal_deletes_only_selected_scoped_record_and_keeps_people()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "解除谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙", "丙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        var third = vm.People.Single(person => person.Name == "丙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, third.Id);

        var candidates = await vm.GetTargetLinkRemovalCandidatesAsync();
        var relationship = Assert.Single(candidates, item => item.Kind == TargetLinkRemovalKind.Relationship);
        var union = Assert.Single(candidates, item => item.Kind == TargetLinkRemovalKind.FamilyUnion);
        await vm.RemoveTargetLinkAsync(relationship);
        Assert.Equal("已解除人物关系。", vm.Status);
        await using (var afterRelationship = database.CreateContext())
        {
            Assert.Equal(3, await afterRelationship.People.CountAsync());
            Assert.Empty(await afterRelationship.PersonRelationships.ToListAsync());
            Assert.Single(await afterRelationship.FamilyUnions.ToListAsync());
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(union));
        union = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(item => item.Kind == TargetLinkRemovalKind.FamilyUnion);
        await vm.RemoveTargetLinkAsync(union);
        Assert.Equal("已解除婚配关系。", vm.Status);
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.People.CountAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        Assert.Empty(await verify.FamilyUnions.ToListAsync());
        Assert.Equal(genealogy.Id, first.GenealogyId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_link_removal_rejects_foreign_or_stale_record_without_deleting_anything()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var firstGenealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var secondGenealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var first = await service.AddPersonAsync(new Person { GenealogyId = firstGenealogy.Id, Name = "甲" });
        var firstOther = await service.AddPersonAsync(new Person { GenealogyId = firstGenealogy.Id, Name = "乙" });
        var foreign = await service.AddPersonAsync(new Person { GenealogyId = secondGenealogy.Id, Name = "外甲" });
        var foreignOther = await service.AddPersonAsync(new Person { GenealogyId = secondGenealogy.Id, Name = "外乙" });
        var ownRelationship = await service.AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = firstGenealogy.Id, PersonId = first.Id, RelatedPersonId = firstOther.Id, Kind = PersonRelationshipKind.Spouse
        });
        var foreignUnion = await service.AddFamilyUnionAsync(new FamilyUnion
        {
            GenealogyId = secondGenealogy.Id, PartnerOneId = foreign.Id, PartnerTwoId = foreignOther.Id
        });

        var vm = new GenealogySelectionViewModel(database.CreateContext);
        await vm.LoadAsync();
        await vm.SelectAsync(vm.Genealogies.Single(item => item.Id == firstGenealogy.Id));
        vm.SelectedPerson = vm.People.Single(item => item.Id == first.Id);
        var ownCandidate = Assert.Single(await vm.GetTargetLinkRemovalCandidatesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(
            ownCandidate with { Id = foreignUnion.Id, Kind = TargetLinkRemovalKind.FamilyUnion, Description = "伪造跨谱婚配" }));
        await vm.RemoveTargetLinkAsync(ownCandidate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(
            ownCandidate with { Description = "已解除关系" }));

        await using var verify = database.CreateContext();
        Assert.Empty(await verify.PersonRelationships.Where(item => item.GenealogyId == firstGenealogy.Id).ToListAsync());
        Assert.Single(await verify.FamilyUnions.Where(item => item.GenealogyId == secondGenealogy.Id).ToListAsync());
        Assert.Equal(4, await verify.People.CountAsync());
    }

    [Theory]
    [InlineData(TargetLinkRemovalKind.Relationship)]
    [InlineData(TargetLinkRemovalKind.FamilyUnion)]
    [Trait("Category", "Integration")]
    public async Task Removal_confirmation_rejects_external_removal_and_person_reselection(TargetLinkRemovalKind kind)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "确认谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(p => p.Name == "甲");
        var second = vm.People.Single(p => p.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, second.Id);
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.Kind == kind);

        // Switching to the other participant and back invalidates the original confirmation even though IDs match.
        vm.SelectedPerson = second;
        vm.SelectedPerson = first;
        var changed = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(candidate));
        Assert.Contains("当前选择已变化", changed.Message);
        await using (var verify = database.CreateContext())
        {
            Assert.Single(await verify.PersonRelationships.ToListAsync());
            Assert.Single(await verify.FamilyUnions.ToListAsync());
        }

        candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(c => c.Kind == kind);
        await using (var external = database.CreateContext())
        {
            if (kind == TargetLinkRemovalKind.Relationship)
                await external.PersonRelationships.Where(item => item.Id == candidate.Id).ExecuteDeleteAsync();
            else
                await external.FamilyUnions.Where(item => item.Id == candidate.Id).ExecuteDeleteAsync();
        }
        var stale = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(candidate));
        Assert.Contains("解除候选已过期", stale.Message);
        Assert.Equal("解除候选已过期，当前图数据已重新加载。", vm.Status);
        Assert.Equal(first.Id, vm.SelectedPerson!.Id);
        Assert.DoesNotContain(await vm.GetTargetLinkRemovalCandidatesAsync(), c => c.Id == candidate.Id);
        await using var final = database.CreateContext();
        Assert.Equal(2, await final.People.CountAsync());
        Assert.Equal(kind == TargetLinkRemovalKind.Relationship ? 0 : 1, await final.PersonRelationships.CountAsync());
        Assert.Equal(kind == TargetLinkRemovalKind.FamilyUnion ? 0 : 1, await final.FamilyUnions.CountAsync());
    }

    [Theory]
    [InlineData(TargetLinkRemovalKind.Relationship, true)]
    [InlineData(TargetLinkRemovalKind.Relationship, false)]
    [InlineData(TargetLinkRemovalKind.FamilyUnion, true)]
    [InlineData(TargetLinkRemovalKind.FamilyUnion, false)]
    [Trait("Category", "Integration")]
    public async Task Confirmation_rejects_changed_displayed_name_at_delete_and_can_reload(TargetLinkRemovalKind kind, bool renameSelected)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var pause = new PauseCommandInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").AddInterceptors(pause).Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "姓名竞争谱" };
        await vm.CreateAsync();
        foreach (var name in new[] { "甲", "乙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(item => item.Name == "甲");
        var second = vm.People.Single(item => item.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, second.Id);
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(item => item.Kind == kind);
        pause.Arm(kind == TargetLinkRemovalKind.Relationship ? "DELETE FROM \"PersonRelationships\"" : "DELETE FROM \"FamilyUnions\"");
        var removal = vm.RemoveTargetLinkAsync(candidate);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var renamedId = renameSelected ? first.Id : second.Id;
        var newName = renameSelected ? "乙" : "甲";
        try
        {
            await using var external = database.CreateContext();
            (await external.People.SingleAsync(person => person.Id == renamedId)).Name = newName;
            await external.SaveChangesAsync();
        }
        finally { pause.Release(); }
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => removal);
        Assert.Contains("解除候选已过期", failure.Message);
        Assert.Equal("解除候选已过期，当前图数据已重新加载。", vm.Status);
        Assert.Equal(newName, vm.People.Single(person => person.Id == renamedId).Name);
        Assert.Equal(2, vm.People.Count(person => person.Name == newName));
        Assert.Equal(first.Id, vm.SelectedPerson!.Id);
        await using (var verify = database.CreateContext())
        {
            Assert.Single(await verify.PersonRelationships.ToListAsync());
            Assert.Single(await verify.FamilyUnions.ToListAsync());
            Assert.Equal(newName, (await verify.People.SingleAsync(person => person.Id == renamedId)).Name);
        }
        var fresh = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(item => item.Kind == kind);
        Assert.Contains(newName, fresh.Description);
        await vm.RemoveTargetLinkAsync(fresh);
        await using var final = database.CreateContext();
        Assert.Equal(kind == TargetLinkRemovalKind.Relationship ? 0 : 1, await final.PersonRelationships.CountAsync());
        Assert.Equal(kind == TargetLinkRemovalKind.FamilyUnion ? 0 : 1, await final.FamilyUnions.CountAsync());
    }

    [Theory]
    [InlineData(TargetLinkRemovalKind.Relationship)]
    [InlineData(TargetLinkRemovalKind.FamilyUnion)]
    [Trait("Category", "Integration")]
    public async Task Name_change_with_failed_recovery_clears_stale_views_and_can_reselect(TargetLinkRemovalKind kind)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var failure = new FailRefreshAfterWriteInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").AddInterceptors(failure).Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "恢复谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, second.Id);
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(item => item.Kind == kind);
        await using (var external = database.CreateContext())
        {
            (await external.People.SingleAsync(person => person.Id == second.Id)).Name = "乙新";
            await external.SaveChangesAsync();
        }
        failure.Arm(kind == TargetLinkRemovalKind.Relationship ? "DELETE FROM \"PersonRelationships\"" : "DELETE FROM \"FamilyUnions\"");
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(candidate));
        Assert.Contains("解除候选已过期，但目标谱系刷新失败", vm.Status);
        Assert.Empty(vm.TargetTree);
        Assert.Empty(vm.People);
        Assert.Null(vm.SelectedPerson);
        failure.Disable();
        await vm.SelectAsync(genealogy);
        Assert.Contains(vm.People, person => person.Id == second.Id && person.Name == "乙新");
        await using var verify = database.CreateContext();
        Assert.Single(await verify.PersonRelationships.ToListAsync());
        Assert.Single(await verify.FamilyUnions.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Removal_with_unknown_counterpart_accepts_unchanged_snapshot()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "失考谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "甲";
        await vm.AddPersonAsync();
        var first = Assert.Single(vm.People);
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = first.Id,
                Kind = PersonRelationshipKind.LostAncestry });
            await service.AddFamilyUnionAsync(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = first.Id,
                PartnerTwoId = null, Kind = FamilyUnionKind.Unknown });
        }
        vm.SelectedPerson = first;
        var candidates = await vm.GetTargetLinkRemovalCandidatesAsync();
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Null(candidate.SecondName));
        await vm.RemoveTargetLinkAsync(candidates.Single(candidate => candidate.Kind == TargetLinkRemovalKind.Relationship));
        var freshUnion = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(candidate => candidate.Kind == TargetLinkRemovalKind.FamilyUnion);
        await vm.RemoveTargetLinkAsync(freshUnion);
        await using var verify = database.CreateContext();
        Assert.Single(await verify.People.ToListAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        Assert.Empty(await verify.FamilyUnions.ToListAsync());
    }

    [Theory]
    [InlineData("relationship-person")]
    [InlineData("relationship-related")]
    [InlineData("relationship-kind")]
    [InlineData("relationship-preferred")]
    [InlineData("relationship-note")]
    [InlineData("relationship-source")]
    [InlineData("union-first")]
    [InlineData("union-second")]
    [InlineData("union-kind")]
    [InlineData("union-start")]
    [InlineData("union-end")]
    [InlineData("union-note")]
    [Trait("Category", "Integration")]
    public async Task Confirmation_rejects_each_externally_modified_record_field_at_delete(string field)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var pause = new PauseCommandInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(pause).Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "竞争谱" };
        await vm.CreateAsync();
        foreach (var name in new[] { "甲", "乙", "丙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(item => item.Name == "甲");
        var second = vm.People.Single(item => item.Name == "乙");
        var third = vm.People.Single(item => item.Name == "丙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, second.Id);
        var kind = field.StartsWith("relationship", StringComparison.Ordinal)
            ? TargetLinkRemovalKind.Relationship : TargetLinkRemovalKind.FamilyUnion;
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(item => item.Kind == kind);
        pause.Arm(kind == TargetLinkRemovalKind.Relationship ? "DELETE FROM \"PersonRelationships\"" : "DELETE FROM \"FamilyUnions\"");
        var removal = vm.RemoveTargetLinkAsync(candidate);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var external = database.CreateContext();
            if (kind == TargetLinkRemovalKind.Relationship)
            {
                var record = await external.PersonRelationships.SingleAsync(item => item.Id == candidate.Id);
                switch (field)
                {
                    case "relationship-person": record.PersonId = third.Id; break;
                    case "relationship-related": record.RelatedPersonId = third.Id; break;
                    case "relationship-kind": record.Kind = PersonRelationshipKind.AdoptiveParent; break;
                    case "relationship-preferred": record.IsPreferredForLineage = !record.IsPreferredForLineage; break;
                    case "relationship-note": record.Note = "外部备注"; break;
                    case "relationship-source": record.SourceCitation = "外部来源"; break;
                }
            }
            else
            {
                var record = await external.FamilyUnions.SingleAsync(item => item.Id == candidate.Id);
                switch (field)
                {
                    case "union-first": record.PartnerOneId = third.Id; break;
                    case "union-second": record.PartnerTwoId = third.Id; break;
                    case "union-kind": record.Kind = FamilyUnionKind.Partnership; break;
                    case "union-start": record.StartDateText = "外部开始"; break;
                    case "union-end": record.EndDateText = "外部结束"; break;
                    case "union-note": record.Note = "外部备注"; break;
                }
            }
            await external.SaveChangesAsync();
        }
        finally { pause.Release(); }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => removal);
        Assert.Contains("解除候选已过期", error.Message);
        Assert.Equal("解除候选已过期，当前图数据已重新加载。", vm.Status);
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.People.CountAsync());
        Assert.Single(await verify.PersonRelationships.ToListAsync());
        Assert.Single(await verify.FamilyUnions.ToListAsync());
        var graph = await new GenealogyDataService(verify).GetGraphSnapshotAsync(vm.SelectedGenealogy!.Id);
        Assert.True(kind == TargetLinkRemovalKind.Relationship
            ? graph.Relationships.Single() != candidate.RelationshipSnapshot
            : graph.Unions.Single() != candidate.UnionSnapshot);
    }

    [Theory]
    [InlineData(TargetLinkRemovalKind.Relationship)]
    [InlineData(TargetLinkRemovalKind.FamilyUnion)]
    [Trait("Category", "Integration")]
    public async Task Modified_nullable_metadata_requires_new_confirmation_before_removal(TargetLinkRemovalKind kind)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "备注谱" };
        await vm.CreateAsync();
        foreach (var name in new[] { "甲", "乙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(item => item.Name == "甲");
        var second = vm.People.Single(item => item.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, second.Id);
        await using (var external = database.CreateContext())
        {
            if (kind == TargetLinkRemovalKind.Relationship)
                (await external.PersonRelationships.SingleAsync()).Note = "旧备注";
            else
                (await external.FamilyUnions.SingleAsync()).Note = "旧备注";
            await external.SaveChangesAsync();
        }
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(item => item.Kind == kind);
        await using (var external = database.CreateContext())
        {
            if (kind == TargetLinkRemovalKind.Relationship)
                (await external.PersonRelationships.SingleAsync()).Note = null;
            else
                (await external.FamilyUnions.SingleAsync()).Note = null;
            await external.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(candidate));
        var fresh = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(item => item.Kind == kind);
        await vm.RemoveTargetLinkAsync(fresh);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Equal(kind == TargetLinkRemovalKind.Relationship ? 0 : 1, await verify.PersonRelationships.CountAsync());
        Assert.Equal(kind == TargetLinkRemovalKind.FamilyUnion ? 0 : 1, await verify.FamilyUnions.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Switching_genealogy_during_profile_relationship_and_union_edits_keeps_the_original_scoped_transaction()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var pause = new PauseCommandInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(pause).Options;
        var coordinator = new TargetContextCoordinator(options);
        var vm = new GenealogySelectionViewModel(coordinator.CreateContext, coordinator) { NewName = "甲谱" };
        await vm.CreateAsync();
        var firstGenealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲人物", "乙人物" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var firstPerson = vm.People.Single(item => item.Name == "甲人物");
        var firstPeer = vm.People.Single(item => item.Name == "乙人物");
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var secondGenealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "外谱人物";
        await vm.AddPersonAsync();

        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == firstPerson.Id);
        vm.EditedBiography = "甲谱档案";
        pause.Arm("UPDATE \"People\"");
        var profile = vm.SavePersonProfileAsync();
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await vm.SelectAsync(secondGenealogy);
        Assert.Null(vm.SelectedPerson);
        pause.Release();
        await profile;
        Assert.Equal(secondGenealogy.Id, vm.SelectedGenealogy!.Id);
        Assert.All(vm.People, item => Assert.Equal(secondGenealogy.Id, item.GenealogyId));

        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == firstPerson.Id);
        pause.Arm("INSERT INTO \"PersonRelationships\"");
        var relationship = vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, firstPeer.Id);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await vm.SelectAsync(secondGenealogy);
        pause.Release();
        await relationship;
        Assert.Equal(secondGenealogy.Id, vm.SelectedGenealogy!.Id);
        Assert.All(vm.People, item => Assert.Equal(secondGenealogy.Id, item.GenealogyId));

        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == firstPerson.Id);
        pause.Arm("INSERT INTO \"FamilyUnions\"");
        var union = vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, firstPeer.Id);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await vm.SelectAsync(secondGenealogy);
        pause.Release();
        await union;
        Assert.Equal(secondGenealogy.Id, vm.SelectedGenealogy!.Id);
        Assert.All(vm.People, item => Assert.Equal(secondGenealogy.Id, item.GenealogyId));

        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == firstPerson.Id);
        var removableUnion = (await vm.GetTargetLinkRemovalCandidatesAsync())
            .Single(item => item.Kind == TargetLinkRemovalKind.FamilyUnion);
        pause.Arm("DELETE FROM \"FamilyUnions\"");
        var removal = vm.RemoveTargetLinkAsync(removableUnion);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await vm.SelectAsync(secondGenealogy);
        pause.Release();
        await removal;

        await using var verify = database.CreateContext();
        Assert.Equal("甲谱档案", (await verify.People.SingleAsync(item => item.Id == firstPerson.Id)).Biography);
        Assert.Equal(1, await verify.PersonRelationships.CountAsync(item => item.GenealogyId == firstGenealogy.Id));
        Assert.Equal(0, await verify.FamilyUnions.CountAsync(item => item.GenealogyId == firstGenealogy.Id));
        Assert.Equal(0, await verify.PersonRelationships.CountAsync(item => item.GenealogyId == secondGenealogy.Id));
        Assert.Equal(0, await verify.FamilyUnions.CountAsync(item => item.GenealogyId == secondGenealogy.Id));
        Assert.All(vm.People, item => Assert.Equal(secondGenealogy.Id, item.GenealogyId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Failed_target_link_removal_keeps_all_graph_records_intact()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "失败解除谱" });
        var first = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        var second = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "乙" });
        var relationship = await service.AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = genealogy.Id, PersonId = first.Id, RelatedPersonId = second.Id, Kind = PersonRelationshipKind.Spouse
        });
        var union = await service.AddFamilyUnionAsync(new FamilyUnion
        {
            GenealogyId = genealogy.Id, PartnerOneId = first.Id, PartnerTwoId = second.Id
        });
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectTargetRelationshipDelete BEFORE DELETE ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated relationship delete failure'); END;");
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectTargetUnionDelete BEFORE DELETE ON FamilyUnions BEGIN SELECT RAISE(ABORT, 'simulated union delete failure'); END;");

        var snapshot = await service.GetGraphSnapshotAsync(genealogy.Id);
        var relSnapshot = snapshot.Relationships.Single(item => item.Id == relationship.Id);
        var unionSnapshot = snapshot.Unions.Single(item => item.Id == union.Id);
        var names = snapshot.People.ToDictionary(item => item.Id, item => item.Name);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => service.RemoveRelationshipForParticipantAsync(genealogy.Id, relSnapshot, first.Id,
            names[relSnapshot.PersonId], names[relSnapshot.RelatedPersonId!.Value], snapshot.UnlinkGeneration));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => service.RemoveFamilyUnionForParticipantAsync(genealogy.Id, unionSnapshot, first.Id,
            names[unionSnapshot.PartnerOneId], names[unionSnapshot.PartnerTwoId!.Value], snapshot.UnlinkGeneration));
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Single(await verify.PersonRelationships.ToListAsync());
        Assert.Single(await verify.FamilyUnions.ToListAsync());
    }

    private sealed class PauseCommandInterceptor : DbCommandInterceptor
    {
        private string? _sqlFragment;
        private int _armed;
        private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _continue = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Arm(string sqlFragment)
        {
            _sqlFragment = sqlFragment;
            _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _continue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _armed, 1);
        }

        public void Release() => _continue.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PauseIfArmedAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PauseIfArmedAsync(command, cancellationToken);
            return result;
        }

        private async Task PauseIfArmedAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var fragment = _sqlFragment;
            if (fragment is null || !command.CommandText.Contains(fragment, StringComparison.Ordinal) ||
                Interlocked.Exchange(ref _armed, 0) != 1) return;
            var entered = _entered;
            var continuation = _continue;
            entered.TrySetResult();
            await continuation.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FailRefreshAfterWriteInterceptor : DbCommandInterceptor
    {
        private string? _writeFragment;
        private int _failReads;

        public void Arm(string writeFragment)
        {
            _writeFragment = writeFragment;
            Volatile.Write(ref _failReads, 0);
        }

        public void Disable()
        {
            _writeFragment = null;
            Volatile.Write(ref _failReads, 0);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            ThrowWhenRefreshReads(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            ArmRefreshFailureAfterWrite(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            ArmRefreshFailureAfterWrite(command);
            return ValueTask.FromResult(result);
        }

        private void ArmRefreshFailureAfterWrite(DbCommand command)
        {
            var writeFragment = _writeFragment;
            if (writeFragment is not null && command.CommandText.Contains(writeFragment, StringComparison.Ordinal))
                Volatile.Write(ref _failReads, 1);
        }

        private void ThrowWhenRefreshReads(DbCommand command)
        {
            if (Volatile.Read(ref _failReads) == 1 && command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("simulated refresh read failure");
        }
    }
}
