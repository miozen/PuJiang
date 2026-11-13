using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class WorkbenchChildParentCorrectionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Combined_relationship_draft_rolls_back_and_rejects_stale_then_saves_after_reload()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" };
        var father = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male };
        var a = new Person { GenealogyId = g.Id, Name = "甲", Sex = PersonSex.Female };
        var b = new Person { GenealogyId = g.Id, Name = "乙", Sex = PersonSex.Female };
        var child = new Person { GenealogyId = g.Id, Name = "子" };
        var unionA = new FamilyUnion { GenealogyId = g.Id, PartnerOneId = father.Id, PartnerTwoId = a.Id, Kind = FamilyUnionKind.Marriage, Note = "旧备注" };
        var unionB = new FamilyUnion { GenealogyId = g.Id, PartnerOneId = father.Id, PartnerTwoId = b.Id, Kind = FamilyUnionKind.Marriage };
        var fatherLink = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = father.Id, Kind = PersonRelationshipKind.BiologicalParent };
        var motherLink = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = a.Id, Kind = PersonRelationshipKind.BiologicalParent };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(father, a, b, child); seed.FamilyUnions.AddRange(unionA, unionB); seed.PersonRelationships.AddRange(fatherLink, motherLink); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        void Change(WorkbenchPersonDraft draft)
        {
            draft.Values = draft.Values with { Name = "父改名" };
            draft.SetUnionValues(unionA.Id, new(FamilyUnionKind.Marriage, "开始", null, "新备注"));
            draft.SetChildOtherParent(child.Id, b.Id);
        }
        async Task AssertOriginal()
        {
            await using var check = db.CreateContext();
            Assert.Equal("父", (await check.People.SingleAsync(p => p.Id == father.Id)).Name);
            Assert.Equal("旧备注", (await check.FamilyUnions.SingleAsync(u => u.Id == unionA.Id)).Note);
            Assert.Equal(a.Id, (await check.PersonRelationships.SingleAsync(r => r.Id == motherLink.Id)).RelatedPersonId);
            Assert.Equal(father.Id, (await check.PersonRelationships.SingleAsync(r => r.Id == fatherLink.Id)).RelatedPersonId);
        }
        var draft = new WorkbenchPersonDraft(await command.ReadAsync(g.Id, father.Id)); Change(draft);
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectCombinedCorrection BEFORE UPDATE ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty); await AssertOriginal();
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectCombinedCorrection");
        await using (var external = db.CreateContext()) await external.People.Where(p => p.Id == b.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "乙外部变更"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty); await AssertOriginal();
        var fresh = new WorkbenchPersonDraft(await command.ReadAsync(g.Id, father.Id)); Change(fresh); await fresh.SaveAsync(command);
        await using var final = db.CreateContext();
        Assert.Equal("父改名", (await final.People.SingleAsync(p => p.Id == father.Id)).Name);
        Assert.Equal("新备注", (await final.FamilyUnions.SingleAsync(u => u.Id == unionA.Id)).Note);
        Assert.Equal(b.Id, (await final.PersonRelationships.SingleAsync(r => r.Id == motherLink.Id)).RelatedPersonId);
        Assert.Equal(father.Id, (await final.PersonRelationships.SingleAsync(r => r.Id == fatherLink.Id)).RelatedPersonId);
        var reloaded = await new WorkbenchPersonProfileCommand(final).ReadAsync(g.Id, father.Id);
        Assert.Equal(b.Id, Assert.Single(reloaded.Children!).OtherParentId);
    }

    [Theory]
    [InlineData(PersonSex.Male, PersonSex.Female)] [InlineData(PersonSex.Female, PersonSex.Male)]
    [Trait("Category", "Integration")]
    public async Task Correct_clear_and_restore_preserves_child_descendants_and_special_relationships(PersonSex parentSex, PersonSex otherSex)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var parent = new Person { GenealogyId = g.Id, Name = "所选父母", Sex = parentSex, RecordedGeneration = "1" };
        var a = new Person { GenealogyId = g.Id, Name = "甲", Sex = otherSex }; var b = new Person { GenealogyId = g.Id, Name = "乙", Sex = otherSex };
        var adoptive = new Person { GenealogyId = g.Id, Name = "养父母", Sex = otherSex }; var child = new Person { GenealogyId = g.Id, Name = "子女", RecordedGeneration = "2" };
        var grandchild = new Person { GenealogyId = g.Id, Name = "后代", RecordedGeneration = "3" };
        var parentLink = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent };
        var otherLink = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = a.Id, Kind = PersonRelationshipKind.BiologicalParent };
        var special = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = adoptive.Id, Kind = PersonRelationshipKind.AdoptiveParent, SourceCitation = "资料" };
        var descendant = new PersonRelationship { GenealogyId = g.Id, PersonId = grandchild.Id, RelatedPersonId = child.Id, Kind = PersonRelationshipKind.BiologicalParent };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(parent, a, b, adoptive, child, grandchild);
            seed.FamilyUnions.AddRange(new FamilyUnion { GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = a.Id, Kind = FamilyUnionKind.Marriage }, new FamilyUnion { GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = b.Id, Kind = FamilyUnionKind.Marriage });
            seed.PersonRelationships.AddRange(parentLink, otherLink, special, descendant); await seed.SaveChangesAsync();
        }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.ReadAsync(g.Id, parent.Id)); Assert.Equal(a.Id, Assert.Single(draft.Original.Children!).OtherParentId);
        draft.SetChildOtherParent(child.Id, b.Id); draft.Values = draft.Values with { Name = "父母改名" };
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectParentCorrection BEFORE UPDATE ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using (var rollback = db.CreateContext()) { Assert.Equal(a.Id, (await rollback.PersonRelationships.SingleAsync(r => r.Id == otherLink.Id)).RelatedPersonId); Assert.Equal("所选父母", (await rollback.People.SingleAsync(p => p.Id == parent.Id)).Name); }
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectParentCorrection"); await draft.SaveAsync(command);
        await using (var verify = db.CreateContext())
        {
            Assert.Equal(b.Id, (await verify.PersonRelationships.SingleAsync(r => r.Id == otherLink.Id)).RelatedPersonId);
            Assert.Equal(child.Id, (await verify.People.SingleAsync(p => p.Id == child.Id)).Id);
            Assert.Equal(child.Id, (await verify.PersonRelationships.SingleAsync(r => r.Id == descendant.Id)).RelatedPersonId);
            Assert.Equal("资料", (await verify.PersonRelationships.SingleAsync(r => r.Id == special.Id)).SourceCitation);
        }
        var clear = new WorkbenchPersonDraft(await command.ReadAsync(g.Id, parent.Id)); clear.SetChildOtherParent(child.Id, null); await clear.SaveAsync(command);
        await using (var verify = db.CreateContext()) { Assert.False(await verify.PersonRelationships.AnyAsync(r => r.Id == otherLink.Id)); Assert.True(await verify.PersonRelationships.AnyAsync(r => r.Id == parentLink.Id)); Assert.True(await verify.PersonRelationships.AnyAsync(r => r.Id == descendant.Id)); Assert.True(await verify.PersonRelationships.AnyAsync(r => r.Id == special.Id)); }
        var restore = new WorkbenchPersonDraft(await command.ReadAsync(g.Id, parent.Id)); restore.SetChildOtherParent(child.Id, a.Id); await restore.SaveAsync(command);
        await using var final = db.CreateContext(); Assert.Contains(await final.PersonRelationships.ToListAsync(), r => r.PersonId == child.Id && r.RelatedPersonId == a.Id && r.Kind == PersonRelationshipKind.BiologicalParent);
    }

    [Theory]
    [InlineData("note")] [InlineData("source")] [InlineData("preferred")] [InlineData("unknown")] [InlineData("multiple")]
    [Trait("Category", "Integration")]
    public async Task Ambiguous_or_sourced_relation_is_blocked_without_data_loss(string reason)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var father = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male }; var mother = new Person { GenealogyId = g.Id, Name = "母", Sex = reason == "unknown" ? PersonSex.Unknown : PersonSex.Female };
        var extra = new Person { GenealogyId = g.Id, Name = "另一个母亲", Sex = PersonSex.Female }; var child = new Person { GenealogyId = g.Id, Name = "子" };
        var link = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = mother.Id, Kind = PersonRelationshipKind.BiologicalParent, Note = reason == "note" ? "原备注" : null, SourceCitation = reason == "source" ? "原来源" : null, IsPreferredForLineage = reason == "preferred" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(father, mother, extra, child); seed.PersonRelationships.AddRange(new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = father.Id, Kind = PersonRelationshipKind.BiologicalParent }, link); if (reason == "multiple") seed.PersonRelationships.Add(new() { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = extra.Id, Kind = PersonRelationshipKind.BiologicalParent }); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var draft = new WorkbenchPersonDraft(await new WorkbenchPersonProfileCommand(context).ReadAsync(g.Id, father.Id));
        Assert.NotNull(Assert.Single(draft.Original.Children!).BlockReason); Assert.Throws<InvalidOperationException>(() => draft.SetChildOtherParent(child.Id, null)); Assert.False(draft.IsDirty);
        await using var verify = db.CreateContext(); Assert.True(await verify.PersonRelationships.AnyAsync(r => r.Id == link.Id));
    }

    [Theory]
    [InlineData("foreign")] [InlineData("not_married")] [InlineData("cycle")] [InlineData("stale")]
    [Trait("Category", "Integration")]
    public async Task Invalid_target_or_stale_snapshot_fails_without_partial_write(string mode)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" }; var other = new Genealogy { Name = "别谱" };
        var father = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male }; var mother = new Person { GenealogyId = g.Id, Name = "母", Sex = PersonSex.Female };
        var candidate = new Person { GenealogyId = mode == "foreign" ? other.Id : g.Id, Name = "候选", Sex = PersonSex.Female };
        var child = new Person { GenealogyId = g.Id, Name = "子" };
        var old = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = mother.Id, Kind = PersonRelationshipKind.BiologicalParent };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.AddRange(g, other); seed.People.AddRange(father, mother, candidate, child);
            seed.PersonRelationships.AddRange(new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = father.Id, Kind = PersonRelationshipKind.BiologicalParent }, old);
            if (mode == "cycle") { seed.FamilyUnions.Add(new FamilyUnion { GenealogyId = g.Id, PartnerOneId = father.Id, PartnerTwoId = candidate.Id, Kind = FamilyUnionKind.Marriage }); seed.PersonRelationships.Add(new PersonRelationship { GenealogyId = g.Id, PersonId = candidate.Id, RelatedPersonId = child.Id, Kind = PersonRelationshipKind.BiologicalParent }); }
            if (mode == "stale") seed.FamilyUnions.Add(new FamilyUnion { GenealogyId = g.Id, PartnerOneId = father.Id, PartnerTwoId = candidate.Id, Kind = FamilyUnionKind.Marriage });
            await seed.SaveChangesAsync();
        }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context); var draft = new WorkbenchPersonDraft(await command.ReadAsync(g.Id, father.Id));
        draft.SetChildOtherParent(child.Id, candidate.Id); draft.Values = draft.Values with { Name = "不能提交" };
        if (mode == "stale") await using (var changed = db.CreateContext()) { await changed.People.Where(p => p.Id == mother.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "外部变更")); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using var verify = db.CreateContext(); Assert.Equal("父", (await verify.People.SingleAsync(p => p.Id == father.Id)).Name); Assert.Equal(mother.Id, (await verify.PersonRelationships.SingleAsync(r => r.Id == old.Id)).RelatedPersonId);
    }
}
