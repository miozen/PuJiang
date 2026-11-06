using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class WorkbenchChildParentDefaultTests
{
    [Theory]
    [InlineData(PersonSex.Male, PersonSex.Female, 0)]
    [InlineData(PersonSex.Male, PersonSex.Female, 1)]
    [InlineData(PersonSex.Male, PersonSex.Female, 2)]
    [InlineData(PersonSex.Female, PersonSex.Male, 0)]
    [InlineData(PersonSex.Female, PersonSex.Male, 1)]
    [InlineData(PersonSex.Female, PersonSex.Male, 2)]
    [Trait("Category", "Integration")]
    public async Task Default_parent_uses_only_ordered_marriages_and_survives_reload(PersonSex sex, PersonSex spouseSex, int count)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "虚构谱" };
        var parent = new Person { GenealogyId = g.Id, Name = "所选父母", Sex = sex, RecordedGeneration = "1" };
        var first = new Person { GenealogyId = g.Id, Name = "配偶甲", Sex = spouseSex };
        var second = new Person { GenealogyId = g.Id, Name = "配偶乙", Sex = spouseSex };
        var partner = new Person { GenealogyId = g.Id, Name = "非婚配伴侣", Sex = spouseSex };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(parent, first, second, partner);
            seed.FamilyUnions.Add(new FamilyUnion { Id = Guid.Parse("01000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = partner.Id, Kind = FamilyUnionKind.Partnership });
            if (count > 1) seed.FamilyUnions.Add(new FamilyUnion { Id = Guid.Parse("20000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = second.Id, Kind = FamilyUnionKind.Marriage });
            if (count > 0) seed.FamilyUnions.Add(new FamilyUnion { Id = Guid.Parse("10000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = first.Id, Kind = FamilyUnionKind.Marriage });
            await seed.SaveChangesAsync();
        }
        Guid childId;
        await using (var context = db.CreateContext())
        {
            var command = new WorkbenchPersonProfileCommand(context);
            var snapshot = await command.BeginOrdinaryChildAsync(g.Id, parent.Id);
            Assert.Equal(count == 0 ? null : first.Id, snapshot.SecondParentId);
            Assert.Equal(PersonSex.Male, snapshot.Values.Sex); Assert.Equal("2", snapshot.Values.RecordedGeneration);
            var draft = new WorkbenchPersonDraft(snapshot); childId = snapshot.PersonId; draft.Values = draft.Values with { Name = "新子女" };
            await draft.SaveAsync(command);
        }
        await using var verify = db.CreateContext();
        var child = await verify.People.SingleAsync(p => p.Id == childId); Assert.Equal(g.Id, child.GenealogyId);
        var relations = await verify.PersonRelationships.Where(r => r.PersonId == childId).ToListAsync();
        Assert.Equal(count == 0 ? 1 : 2, relations.Count);
        Assert.Contains(relations, r => r.RelatedPersonId == parent.Id && r.Kind == PersonRelationshipKind.BiologicalParent);
        if (count > 0) Assert.Contains(relations, r => r.RelatedPersonId == first.Id && r.Kind == PersonRelationshipKind.BiologicalParent);
        Assert.DoesNotContain(relations, r => r.RelatedPersonId == second.Id || r.RelatedPersonId == partner.Id);
        var reloaded = await new WorkbenchPersonProfileCommand(verify).ReadAsync(g.Id, childId);
        Assert.Equal(count == 0 ? 1 : 2, reloaded.Parents!.Count); Assert.Equal(childId, reloaded.PersonId);
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(1, 2)] [InlineData(2, 1)]
    [Trait("Category", "Integration")]
    public async Task Changed_marriage_candidates_reject_stale_draft_without_partial_commit(int before, int after)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var parent = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var a = new Person { GenealogyId = g.Id, Name = "甲", Sex = PersonSex.Female }; var b = new Person { GenealogyId = g.Id, Name = "乙", Sex = PersonSex.Female };
        var first = new FamilyUnion { Id = Guid.Parse("10000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = a.Id, Kind = FamilyUnionKind.Marriage };
        var second = new FamilyUnion { Id = Guid.Parse("20000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = b.Id, Kind = FamilyUnionKind.Marriage };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(parent, a, b); if (before > 0) seed.FamilyUnions.Add(first); if (before > 1) seed.FamilyUnions.Add(second); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildAsync(g.Id, parent.Id)); draft.Values = draft.Values with { Name = "子女" };
        await using (var changed = db.CreateContext())
        {
            if (after > before) changed.FamilyUnions.Add(before == 0 ? first : second);
            else await changed.FamilyUnions.Where(u => u.Id == second.Id).ExecuteDeleteAsync();
            await changed.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using var verify = db.CreateContext(); Assert.False(await verify.People.AnyAsync(p => p.Id == draft.Original.PersonId)); Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Unidentified_spouse_does_not_silently_create_default_parent()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var parent = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male, RecordedGeneration = "1" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(parent); seed.FamilyUnions.Add(new FamilyUnion { GenealogyId = g.Id, PartnerOneId = parent.Id, Kind = FamilyUnionKind.Marriage }); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => command.BeginOrdinaryChildAsync(g.Id, parent.Id)); Assert.Contains("未详", error.Message);
        Assert.Single(await context.People.ToListAsync()); Assert.Empty(await context.PersonRelationships.ToListAsync());
    }
}
