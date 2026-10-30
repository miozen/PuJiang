using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

public class UnlinkAbaAuditTests
{
    [Theory]
    [InlineData(TargetLinkRemovalKind.Relationship, false)]
    [InlineData(TargetLinkRemovalKind.Relationship, true)]
    [InlineData(TargetLinkRemovalKind.FamilyUnion, false)]
    [InlineData(TargetLinkRemovalKind.FamilyUnion, true)]
    [Trait("Category", "Integration")]
    public async Task Current_unlink_rejects_candidate_after_committed_aba(TargetLinkRemovalKind kind, bool rename)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "虚构谱" };
        await vm.CreateAsync();
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

        // Two separately committed writes model an external writer changing A -> B -> A while confirmation is open.
        for (var step = 0; step < 2; step++)
        {
            await using var external = database.CreateContext();
            if (rename)
                (await external.People.SingleAsync(person => person.Id == second.Id)).Name = step == 0 ? "乙新" : "乙";
            else if (kind == TargetLinkRemovalKind.Relationship)
                (await external.PersonRelationships.SingleAsync(item => item.Id == candidate.Id)).Note = step == 0 ? "新备注" : null;
            else
                (await external.FamilyUnions.SingleAsync(item => item.Id == candidate.Id)).Note = step == 0 ? "新备注" : null;
            await external.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(candidate));
        await using var verify = database.CreateContext();
        Assert.Equal(1, await verify.PersonRelationships.CountAsync());
        Assert.Equal(1, await verify.FamilyUnions.CountAsync());
        Assert.Equal(2, await verify.People.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Isolated_trigger_generation_detects_aba_and_rollback_without_writer_cooperation()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var seed = database.CreateContext();
        var service = new GenealogyDataService(seed);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "原型谱" });
        var person = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });

        // Test-only prototype: coarse global generation, never installed in the application or its migrations.
        await seed.Database.ExecuteSqlRawAsync("CREATE TABLE AbaAuditGeneration (Id INTEGER PRIMARY KEY CHECK (Id = 1), Value INTEGER NOT NULL); INSERT INTO AbaAuditGeneration VALUES (1, 0);");
        await seed.Database.ExecuteSqlRawAsync("CREATE TRIGGER AbaAuditPeopleUpdate AFTER UPDATE ON People BEGIN UPDATE AbaAuditGeneration SET Value = Value + 1 WHERE Id = 1; END;");
        long ReadGeneration() => seed.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM AbaAuditGeneration WHERE Id = 1").Single();
        var initial = ReadGeneration();
        await using (var external = database.CreateContext())
        {
            var item = await external.People.SingleAsync(item => item.Id == person.Id);
            item.Name = "乙";
            await external.SaveChangesAsync();
            item.Name = "甲";
            await external.SaveChangesAsync();
        }
        Assert.Equal(initial + 2, ReadGeneration());
        var captured = ReadGeneration();
        await using (var external = database.CreateContext())
        {
            await using var transaction = await external.Database.BeginTransactionAsync();
            var item = await external.People.SingleAsync(item => item.Id == person.Id);
            item.Name = "回滚";
            await external.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        Assert.Equal(captured, ReadGeneration());
        Assert.Equal("甲", (await seed.People.AsNoTracking().SingleAsync(item => item.Id == person.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Test_only_global_generation_rejects_stale_delete_after_raw_external_aba_and_reinsert()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var seed = database.CreateContext();
        var service = new GenealogyDataService(seed);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "原型谱" });
        var first = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        var second = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "乙" });
        var unrelated = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "丙" });
        var relationship = await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id,
            PersonId = first.Id, RelatedPersonId = second.Id, Kind = PersonRelationshipKind.Spouse });
        var union = await service.AddFamilyUnionAsync(new FamilyUnion { GenealogyId = genealogy.Id,
            PartnerOneId = first.Id, PartnerTwoId = second.Id });
        await seed.Database.ExecuteSqlRawAsync("CREATE TABLE AbaAuditGeneration (Id INTEGER PRIMARY KEY CHECK (Id = 1), Value INTEGER NOT NULL); INSERT INTO AbaAuditGeneration VALUES (1, 0);");
        foreach (var table in new[] { "People", "PersonRelationships", "FamilyUnions" })
            foreach (var operation in new[] { "INSERT", "UPDATE", "DELETE" })
                await seed.Database.ExecuteSqlRawAsync($"CREATE TRIGGER AbaAudit{table}{operation} AFTER {operation} ON {table} BEGIN UPDATE AbaAuditGeneration SET Value = Value + 1 WHERE Id = 1; END;");
        async Task<long> GenerationAsync() => await seed.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM AbaAuditGeneration WHERE Id = 1").SingleAsync();

        var oldGeneration = await GenerationAsync();
        await using (var external = database.CreateContext())
        {
            await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE PersonRelationships SET Note = {"B"} WHERE Id = {relationship.Id}");
            await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE PersonRelationships SET Note = NULL WHERE Id = {relationship.Id}");
        }
        Assert.Equal(oldGeneration + 2, await GenerationAsync());
        var deleted = await seed.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM PersonRelationships WHERE Id = {relationship.Id} AND EXISTS (SELECT 1 FROM AbaAuditGeneration WHERE Id = 1 AND Value = {oldGeneration})");
        Assert.Equal(0, deleted);

        oldGeneration = await GenerationAsync();
        await using (var external = database.CreateContext())
        {
            await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"B"} WHERE Id = {second.Id}");
            await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"乙"} WHERE Id = {second.Id}");
        }
        deleted = await seed.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM FamilyUnions WHERE Id = {union.Id} AND EXISTS (SELECT 1 FROM AbaAuditGeneration WHERE Id = 1 AND Value = {oldGeneration})");
        Assert.Equal(0, deleted);

        oldGeneration = await GenerationAsync();
        await using (var external = database.CreateContext())
        {
            var original = await external.PersonRelationships.AsNoTracking().SingleAsync(item => item.Id == relationship.Id);
            await external.PersonRelationships.Where(item => item.Id == relationship.Id).ExecuteDeleteAsync();
            external.PersonRelationships.Add(original);
            await external.SaveChangesAsync();
        }
        Assert.Equal(oldGeneration + 2, await GenerationAsync());
        deleted = await seed.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM PersonRelationships WHERE Id = {relationship.Id} AND EXISTS (SELECT 1 FROM AbaAuditGeneration WHERE Id = 1 AND Value = {oldGeneration})");
        Assert.Equal(0, deleted);

        oldGeneration = await GenerationAsync();
        await using (var external = database.CreateContext())
            await external.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET Name = {"丁"} WHERE Id = {unrelated.Id}");
        deleted = await seed.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM FamilyUnions WHERE Id = {union.Id} AND EXISTS (SELECT 1 FROM AbaAuditGeneration WHERE Id = 1 AND Value = {oldGeneration})");
        Assert.Equal(0, deleted); // Deliberately conservative: an unrelated edit also invalidates a candidate.
        var freshGeneration = await GenerationAsync();
        deleted = await seed.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM FamilyUnions WHERE Id = {union.Id} AND EXISTS (SELECT 1 FROM AbaAuditGeneration WHERE Id = 1 AND Value = {freshGeneration})");
        Assert.Equal(1, deleted);
        Assert.Single(await seed.PersonRelationships.AsNoTracking().ToListAsync());
        Assert.Equal(3, await seed.People.CountAsync());
    }
}
