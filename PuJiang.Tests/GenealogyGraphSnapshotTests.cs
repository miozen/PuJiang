using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class GenealogyGraphSnapshotTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Snapshot_is_scoped_and_preserves_multiple_relationships_and_recorded_data()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Guid genealogyId, foreignId, childId, biologicalId, adoptiveId;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱", Surname = "甲" });
            var foreign = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
            genealogyId = genealogy.Id;
            foreignId = foreign.Id;
            var branch = new FamilyBranch { GenealogyId = genealogy.Id, Name = "虚构房支", SortOrder = 2 };
            context.FamilyBranches.Add(branch);
            await context.SaveChangesAsync();
            var child = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "同名", BranchId = branch.Id, RecordedGeneration = "谱载三世", DerivedGeneration = 4, RecordedBirthOrder = "长", DerivedBirthOrder = 2 });
            var biological = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "同名" });
            var adoptive = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "养亲" });
            await service.AddPersonAsync(new Person { GenealogyId = foreign.Id, Name = "外谱人物" });
            childId = child.Id;
            biologicalId = biological.Id;
            adoptiveId = adoptive.Id;
            await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = biological.Id, Kind = PersonRelationshipKind.BiologicalParent, SourceCitation = "虚构谱页", IsPreferredForLineage = true });
            await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = adoptive.Id, Kind = PersonRelationshipKind.AdoptiveParent, Note = "虚构备注" });
            await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = biological.Id, Kind = PersonRelationshipKind.LostAncestry });
            context.FamilyUnions.Add(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = biological.Id, PartnerTwoId = adoptive.Id, Note = "虚构婚配" });
            await context.SaveChangesAsync();
        }

        await using var reader = database.CreateContext();
        var serviceReader = new GenealogyDataService(reader);
        var snapshot = await serviceReader.GetGraphSnapshotAsync(genealogyId);
        var repeat = await serviceReader.GetGraphSnapshotAsync(genealogyId);
        Assert.Empty(reader.ChangeTracker.Entries());
        Assert.Equal(genealogyId, snapshot.Genealogy.Id);
        Assert.Equal(3, snapshot.People.Count);
        Assert.Equal(2, snapshot.People.Count(p => p.Name == "同名"));
        Assert.Equal("谱载三世", snapshot.People.Single(p => p.Id == childId).RecordedGeneration);
        Assert.Equal(4, snapshot.People.Single(p => p.Id == childId).DerivedGeneration);
        Assert.Equal("长", snapshot.People.Single(p => p.Id == childId).RecordedBirthOrder);
        Assert.Equal(2, snapshot.People.Single(p => p.Id == childId).DerivedBirthOrder);
        Assert.Equal(3, snapshot.Relationships.Count);
        Assert.Contains(snapshot.Relationships, r => r.PersonId == childId && r.RelatedPersonId == biologicalId && r.SourceCitation == "虚构谱页" && r.IsPreferredForLineage);
        Assert.Contains(snapshot.Relationships, r => r.PersonId == childId && r.RelatedPersonId == adoptiveId && r.Kind == PersonRelationshipKind.AdoptiveParent && r.Note == "虚构备注");
        Assert.Contains(snapshot.Relationships, r => r.Kind == PersonRelationshipKind.LostAncestry && r.RelatedPersonId == null);
        Assert.Equal("虚构婚配", Assert.Single(snapshot.Unions).Note);
        Assert.Equal("虚构房支", Assert.Single(snapshot.Branches).Name);
        Assert.Equal(snapshot.People, repeat.People);
        Assert.Equal(snapshot.Relationships, repeat.Relationships);
        Assert.Single((await serviceReader.GetGraphSnapshotAsync(foreignId)).People);
        Assert.Empty((await serviceReader.GetGraphSnapshotAsync(foreignId)).Relationships);
        Assert.Throws<NotSupportedException>(() => ((ICollection<GenealogyPersonData>)snapshot.People).Clear());
        await Assert.ThrowsAsync<InvalidOperationException>(() => serviceReader.GetGraphSnapshotAsync(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Snapshot_remains_unchanged_when_database_is_edited_later()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "快照谱" });
        var person = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "原名" });
        var snapshot = await service.GetGraphSnapshotAsync(genealogy.Id);
        await service.RenamePersonAsync(genealogy.Id, person.Id, "新名");
        Assert.Equal("原名", Assert.Single(snapshot.People).Name);
        Assert.Equal("新名", Assert.Single((await service.GetGraphSnapshotAsync(genealogy.Id)).People).Name);
    }
}
