using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class WorkbenchPersonProfileCommandTests
{
    [Theory]
    [InlineData(PersonSex.Male, PersonSex.Female)]
    [InlineData(PersonSex.Female, PersonSex.Male)]
    [InlineData(PersonSex.Unknown, PersonSex.Unknown)]
    [Trait("Category", "Integration")]
    public async Task New_spouse_default_sex_uses_known_context_and_remains_editable(PersonSex currentSex, PersonSex expected)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var other = new Genealogy { Name = "另一谱" };
        var current = new Person { GenealogyId = g.Id, Name = "人物", Sex = currentSex, RecordedGeneration = "1" };
        var foreign = new Person { GenealogyId = other.Id, Name = "异谱" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.AddRange(g, other); seed.People.AddRange(current, foreign); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.BeginNewAsync(g.Id, partnerId: foreign.Id, unionKind: FamilyUnionKind.Marriage));
        var draft = new WorkbenchPersonDraft(await command.BeginNewAsync(g.Id, partnerId: current.Id, unionKind: FamilyUnionKind.Marriage));
        Assert.Equal(expected, draft.Values.Sex); Assert.Null(draft.Values.RecordedGeneration);
        draft.Values = draft.Values with { Name = "配偶", Sex = PersonSex.Unknown };
        await draft.SaveAsync(command);
        await using var verify = db.CreateContext();
        Assert.Equal(PersonSex.Unknown, (await verify.People.SingleAsync(p => p.Id == draft.Original.PersonId)).Sex);
        var union = await verify.FamilyUnions.SingleAsync();
        Assert.Contains(current.Id, new Guid?[] { union.PartnerOneId, union.PartnerTwoId });
        Assert.Contains(draft.Original.PersonId, new Guid?[] { union.PartnerOneId, union.PartnerTwoId });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Adding_spouse_keeps_existing_marriages_and_child_parent_records_unchanged()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var current = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male };
        var spouse = new Person { GenealogyId = g.Id, Name = "原配偶", Sex = PersonSex.Female };
        var child = new Person { GenealogyId = g.Id, Name = "子", Sex = PersonSex.Male };
        var oldUnion = new FamilyUnion { GenealogyId = g.Id, PartnerOneId = current.Id, PartnerTwoId = spouse.Id, Kind = FamilyUnionKind.Marriage };
        var father = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = current.Id, Kind = PersonRelationshipKind.BiologicalParent };
        var mother = new PersonRelationship { GenealogyId = g.Id, PersonId = child.Id, RelatedPersonId = spouse.Id, Kind = PersonRelationshipKind.BiologicalParent };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(current, spouse, child); seed.FamilyUnions.Add(oldUnion); seed.PersonRelationships.AddRange(father, mother); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginNewAsync(g.Id, partnerId: current.Id, unionKind: FamilyUnionKind.Marriage));
        draft.Values = draft.Values with { Name = "新配偶" }; await draft.SaveAsync(command);
        await using var verify = db.CreateContext();
        Assert.Equal(2, await verify.FamilyUnions.CountAsync()); Assert.True(await verify.FamilyUnions.AnyAsync(u => u.Id == oldUnion.Id));
        var parents = await verify.PersonRelationships.Where(r => r.PersonId == child.Id).ToListAsync();
        Assert.Equal(2, parents.Count); Assert.Contains(parents, r => r.Id == father.Id && r.RelatedPersonId == current.Id);
        Assert.Contains(parents, r => r.Id == mother.Id && r.RelatedPersonId == spouse.Id);
    }
    [Theory]
    [InlineData(PersonSex.Male, PersonSex.Female)]
    [InlineData(PersonSex.Female, PersonSex.Male)]
    [Trait("Category", "Integration")]
    public async Task Ordinary_child_uses_stable_marriage_order_and_commits_both_parents_atomically(PersonSex parentSex, PersonSex spouseSex)
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" };
        var parent = new Person { GenealogyId = g.Id, Name = "父母", Sex = parentSex, RecordedGeneration = "2" };
        var first = new Person { GenealogyId = g.Id, Name = "配偶一", Sex = spouseSex };
        var second = new Person { GenealogyId = g.Id, Name = "配偶二", Sex = spouseSex };
        var ignored = new Person { GenealogyId = g.Id, Name = "伴侣", Sex = spouseSex };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.Add(g); seed.People.AddRange(parent, first, second, ignored);
            seed.FamilyUnions.AddRange(
                new FamilyUnion { Id = Guid.Parse("20000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = second.Id, Kind = FamilyUnionKind.Marriage },
                new FamilyUnion { Id = Guid.Parse("10000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = first.Id, Kind = FamilyUnionKind.Marriage },
                new FamilyUnion { Id = Guid.Parse("01000000-0000-0000-0000-000000000000"), GenealogyId = g.Id, PartnerOneId = parent.Id, PartnerTwoId = ignored.Id, Kind = FamilyUnionKind.Partnership });
            await seed.SaveChangesAsync();
        }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildAsync(g.Id, parent.Id));
        Assert.Equal(first.Id, draft.Original.SecondParentId); Assert.Equal(PersonSex.Male, draft.Values.Sex); Assert.Equal("3", draft.Values.RecordedGeneration);
        draft.Values = draft.Values with { Name = "新子女" };
        await context.Database.ExecuteSqlRawAsync($"CREATE TRIGGER RejectSecondParent BEFORE INSERT ON PersonRelationships WHEN NEW.RelatedPersonId = '{first.Id.ToString().ToUpperInvariant()}' BEGIN SELECT RAISE(ABORT, 'injected failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using (var verify = db.CreateContext()) { Assert.Equal(4, await verify.People.CountAsync()); Assert.Empty(await verify.PersonRelationships.ToListAsync()); }
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectSecondParent"); await draft.SaveAsync(command);
        await using var final = db.CreateContext();
        var parents = await final.PersonRelationships.Where(r => r.PersonId == draft.Original.PersonId).ToListAsync();
        Assert.Equal(2, parents.Count); Assert.All(parents, r => Assert.Equal(PersonRelationshipKind.BiologicalParent, r.Kind));
        Assert.Contains(parents, r => r.RelatedPersonId == parent.Id); Assert.Contains(parents, r => r.RelatedPersonId == first.Id);
        Assert.DoesNotContain(parents, r => r.RelatedPersonId == second.Id || r.RelatedPersonId == ignored.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Ordinary_child_handles_no_spouse_and_rejects_ambiguous_or_stale_context()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var p = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var spouse = new Person { GenealogyId = g.Id, Name = "配偶", Sex = PersonSex.Unknown };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(p, spouse); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildAsync(g.Id, p.Id)); Assert.Null(draft.Original.SecondParentId);
        draft.Values = draft.Values with { Name = "子" };
        await using (var changed = db.CreateContext()) { changed.FamilyUnions.Add(new FamilyUnion { GenealogyId = g.Id, PartnerOneId = p.Id, PartnerTwoId = spouse.Id, Kind = FamilyUnionKind.Marriage }); await changed.SaveChangesAsync(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.BeginOrdinaryChildAsync(g.Id, p.Id));
        await using var verify = db.CreateContext(); Assert.Equal(2, await verify.People.CountAsync()); Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Ordinary_child_keeps_recorded_parents_when_marriages_later_change()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var p = new Person { GenealogyId = g.Id, Name = "父", Sex = PersonSex.Male, RecordedGeneration = "1" };
        var a = new Person { GenealogyId = g.Id, Name = "母", Sex = PersonSex.Female };
        var b = new Person { GenealogyId = g.Id, Name = "继配", Sex = PersonSex.Female };
        var union = new FamilyUnion { GenealogyId = g.Id, PartnerOneId = p.Id, PartnerTwoId = a.Id, Kind = FamilyUnionKind.Marriage };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.AddRange(p, a, b); seed.FamilyUnions.Add(union); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginOrdinaryChildAsync(g.Id, p.Id)); Assert.Equal(a.Id, draft.Original.SecondParentId);
        draft.Values = draft.Values with { Name = "子" }; await draft.SaveAsync(command);
        await using (var changed = db.CreateContext())
        {
            await changed.FamilyUnions.Where(u => u.Id == union.Id).ExecuteDeleteAsync();
            changed.FamilyUnions.Add(new FamilyUnion { GenealogyId = g.Id, PartnerOneId = p.Id, PartnerTwoId = b.Id, Kind = FamilyUnionKind.Marriage });
            await changed.SaveChangesAsync();
        }
        await using var verify = db.CreateContext();
        var parents = await verify.PersonRelationships.Where(r => r.PersonId == draft.Original.PersonId).Select(r => r.RelatedPersonId).ToListAsync();
        Assert.Contains(p.Id, parents); Assert.Contains(a.Id, parents); Assert.DoesNotContain(b.Id, parents);
    }
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Draft_is_scoped_validated_and_saved_once()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var a = new Genealogy { Name = "甲谱" }; var b = new Genealogy { Name = "乙谱" };
        var person = new Person { GenealogyId = a.Id, Name = "原名" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.AddRange(a, b); seed.People.Add(person); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ReadAsync(b.Id, person.Id));
        var draft = new WorkbenchPersonDraft(await command.ReadAsync(a.Id, person.Id));
        draft.Values = draft.Values with { Name = "  " };
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command));
        Assert.True(draft.IsDirty); Assert.False(draft.IsSaved);
        draft.Values = draft.Values with { Name = " 新名 ", Biography = "虚构资料" };
        await draft.SaveAsync(command); Assert.True(draft.IsSaved); Assert.False(draft.IsDirty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command));
        await using var verify = db.CreateContext(); var saved = await verify.People.SingleAsync();
        Assert.Equal("新名", saved.Name); Assert.Equal("虚构资料", saved.Biography);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Aba_change_and_database_failure_preserve_draft_and_never_partially_write()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var genealogy = new Genealogy { Name = "虚构谱" }; var person = new Person { GenealogyId = genealogy.Id, Name = "原名" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(genealogy); seed.People.Add(person); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var stale = new WorkbenchPersonDraft(await command.ReadAsync(genealogy.Id, person.Id));
        stale.Values = stale.Values with { Name = "草稿", Biography = "草稿资料" };
        await using (var other = db.CreateContext())
        {
            await other.People.Where(p => p.Id == person.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "临时"));
            await other.People.Where(p => p.Id == person.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "原名"));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.SaveAsync(command));
        Assert.True(stale.IsDirty); Assert.False(stale.IsSaved);
        var failing = new WorkbenchPersonDraft(await command.ReadAsync(genealogy.Id, person.Id));
        failing.Values = stale.Values;
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectProfile BEFORE UPDATE ON People BEGIN SELECT RAISE(ABORT, 'injected failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => failing.SaveAsync(command));
        Assert.True(failing.IsDirty); Assert.False(failing.IsSaved);
        await using var verify = db.CreateContext(); var saved = await verify.People.SingleAsync();
        Assert.Equal("原名", saved.Name); Assert.Null(saved.Biography);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task New_person_is_a_scoped_draft_until_saved_and_stale_insert_is_rejected()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var genealogy = new Genealogy { Name = "虚构谱" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(genealogy); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginNewAsync(genealogy.Id));
        Assert.True(draft.IsDirty); Assert.Equal(0, await context.People.CountAsync());
        draft.Values = draft.Values with { Name = "新人物", RecordedGeneration = "2" };
        await draft.SaveAsync(command);
        await using (var verify = db.CreateContext())
        {
            var person = await verify.People.SingleAsync(); Assert.Equal(genealogy.Id, person.GenealogyId);
            Assert.Equal("2", person.RecordedGeneration); Assert.Empty(await verify.PersonRelationships.ToListAsync());
        }
        var stale = new WorkbenchPersonDraft(await command.BeginNewAsync(genealogy.Id));
        stale.Values = stale.Values with { Name = "过期草稿", RecordedGeneration = "3" };
        await using (var other = db.CreateContext()) await other.People.ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "外部变更"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.SaveAsync(command));
        Assert.True(stale.IsDirty); Assert.False(stale.IsSaved);
        await using var final = db.CreateContext(); Assert.Equal(1, await final.People.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Editing_without_generation_change_preserves_unrecognized_source()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync(); var g = new Genealogy { Name = "谱" };
        var p = new Person { GenealogyId = g.Id, Name = "旧名", RecordedGeneration = "待考" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(p); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.ReadAsync(g.Id, p.Id));
        draft.Values = draft.Values with { Name = "新名" }; await draft.SaveAsync(command);
        await using var verify = db.CreateContext(); Assert.Equal("待考", (await verify.People.SingleAsync()).RecordedGeneration);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Child_prefills_next_generation_and_failure_rolls_back_person_and_relation()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var parent = new Person { GenealogyId = g.Id, Name = "父", RecordedGeneration = "第二世" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(parent); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginNewAsync(g.Id, parent.Id, PersonRelationshipKind.BiologicalParent));
        Assert.Equal("3", draft.Values.RecordedGeneration); draft.Values = draft.Values with { Name = "子" };
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectChild BEFORE INSERT ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'injected failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using (var verify = db.CreateContext()) { Assert.Equal(1, await verify.People.CountAsync()); Assert.Empty(await verify.PersonRelationships.ToListAsync()); }
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectChild");
        await draft.SaveAsync(command);
        await using var final = db.CreateContext(); Assert.Equal(2, await final.People.CountAsync()); Assert.Single(await final.PersonRelationships.ToListAsync());
    }
    [Fact]
    [Trait("Category", "Integration")]
    public async Task New_spouse_and_union_commit_together_and_failure_leaves_no_person()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var person = new Person { GenealogyId = g.Id, Name = "人物", RecordedGeneration = "1" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(g); seed.People.Add(person); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var draft = new WorkbenchPersonDraft(await command.BeginNewAsync(g.Id, partnerId: person.Id, unionKind: FamilyUnionKind.Marriage));
        draft.Values = draft.Values with { Name = "配偶" }; Assert.Null(draft.Values.RecordedGeneration);
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectUnion BEFORE INSERT ON FamilyUnions BEGIN SELECT RAISE(ABORT, 'injected failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using (var verify = db.CreateContext()) { Assert.Equal(1, await verify.People.CountAsync()); Assert.Empty(await verify.FamilyUnions.ToListAsync()); }
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectUnion"); await draft.SaveAsync(command);
        await using var final = db.CreateContext(); Assert.Equal(2, await final.People.CountAsync());
        var union = await final.FamilyUnions.SingleAsync();
        Assert.Contains(person.Id, new Guid?[] { union.PartnerOneId, union.PartnerTwoId });
        Assert.Contains(draft.Original.PersonId, new Guid?[] { union.PartnerOneId, union.PartnerTwoId });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task New_sibling_and_selected_parent_are_atomic_and_stale_choices_fail()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "谱" }; var other = new Genealogy { Name = "另一谱" };
        var person = new Person { GenealogyId = g.Id, Name = "人物", RecordedGeneration = "第二世" };
        var parent = new Person { GenealogyId = g.Id, Name = "父", RecordedGeneration = "一世" };
        var foreign = new Person { GenealogyId = other.Id, Name = "异谱", RecordedGeneration = "一世" };
        await using (var seed = db.CreateContext())
        {
            seed.Genealogies.AddRange(g, other); seed.People.AddRange(person, parent, foreign);
            seed.PersonRelationships.Add(new PersonRelationship { GenealogyId = g.Id, PersonId = person.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.AdoptiveParent });
            await seed.SaveChangesAsync();
        }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.BeginNewAsync(g.Id, siblingId: foreign.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.BeginNewAsync(g.Id, person.Id, PersonRelationshipKind.BiologicalParent, siblingId: person.Id));
        var draft = new WorkbenchPersonDraft(await command.BeginNewAsync(g.Id, parent.Id, PersonRelationshipKind.AdoptiveParent, siblingId: person.Id));
        Assert.Equal("2", draft.Values.RecordedGeneration); draft.Values = draft.Values with { Name = "兄妹" };
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectSibling BEFORE INSERT ON PersonRelationships WHEN NEW.Kind=6 BEGIN SELECT RAISE(ABORT, 'injected failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => draft.SaveAsync(command)); Assert.True(draft.IsDirty);
        await using (var verify = db.CreateContext()) { Assert.Equal(3, await verify.People.CountAsync()); Assert.Single(await verify.PersonRelationships.ToListAsync()); }
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectSibling");
        var forged = new WorkbenchPersonDraft(draft.Original with { ParentId = person.Id }); forged.Values = draft.Values;
        await Assert.ThrowsAsync<InvalidOperationException>(() => forged.SaveAsync(command));
        Assert.True(forged.IsDirty); await draft.SaveAsync(command);
        await using var final = db.CreateContext();
        var relations = await final.PersonRelationships.Where(r => r.PersonId == draft.Original.PersonId).ToListAsync();
        Assert.Contains(relations, r => r.Kind == PersonRelationshipKind.AdoptiveParent && r.RelatedPersonId == parent.Id);
        Assert.Contains(relations, r => r.Kind == PersonRelationshipKind.Sibling && r.RelatedPersonId == person.Id);
    }
}
