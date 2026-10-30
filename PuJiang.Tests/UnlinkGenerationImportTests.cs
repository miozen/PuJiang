using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

public class UnlinkGenerationImportTests
{
    private const int ImportedPeople = 50;
    private static string CreateGedcom()
    {
        var result = new StringBuilder("0 HEAD\n1 GEDC\n2 VERS 5.5.1\n");
        for (var i = 1; i <= ImportedPeople; i++)
            result.Append($"0 @I{i}@ INDI\n1 NAME 虚构{i} /氏/\n");
        return result.Append("0 @F1@ FAM\n1 HUSB @I1@\n1 WIFE @I2@\n0 TRLR\n").ToString();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Cross_genealogy_import_commits_generation_or_rolls_it_back_atomically(bool fail)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "原谱" };
        await vm.CreateAsync();
        var originalGenealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙" }) { vm.NewPersonName = name; await vm.AddPersonAsync(); }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        var candidate = Assert.Single(await vm.GetTargetLinkRemovalCandidatesAsync());
        var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "fictional.ged");
        await File.WriteAllTextAsync(path, CreateGedcom());
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False");
        if (fail) options.AddInterceptors(new FailUnionInsert());
        await using (var importContext = new ZupuDbContext(options.Options))
        {
            var importer = new ExternalGenealogyExchangeImportService(importContext);
            if (fail)
                await Assert.ThrowsAsync<DbUpdateException>(() => importer.ImportGedcomAsync(path, "导入谱", acceptLosses: true));
            else
                Assert.True((await importer.ImportGedcomAsync(path, "导入谱", acceptLosses: true)).Imported);
        }
        await using (var verify = database.CreateContext())
        {
            var generation = await verify.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
            Assert.Equal(candidate.UnlinkGeneration + (fail ? 0 : ImportedPeople + 1), generation);
            Assert.Equal(fail ? 1 : 2, await verify.Genealogies.CountAsync());
            Assert.Equal(fail ? 2 : ImportedPeople + 2, await verify.People.CountAsync());
            Assert.Equal(fail ? 0 : 1, await verify.FamilyUnions.CountAsync());
            Assert.Equal(fail ? 0 : 1, await verify.GenealogyImportRecords.CountAsync());
            Assert.Single(await verify.PersonRelationships.Where(r => r.GenealogyId == originalGenealogy.Id).ToListAsync());
        }
        if (!fail)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(candidate));
            candidate = Assert.Single(await vm.GetTargetLinkRemovalCandidatesAsync());
        }
        await vm.RemoveTargetLinkAsync(candidate);
        await using var final = database.CreateContext();
        Assert.Empty(await final.PersonRelationships.Where(r => r.GenealogyId == originalGenealogy.Id).ToListAsync());
        Assert.Equal(fail ? 0 : 1, await final.FamilyUnions.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "Integration")]
    public async Task Unrelated_existing_genealogy_edit_invalidates_only_when_committed(bool commit)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "原谱" };
        await vm.CreateAsync();
        foreach (var name in new[] { "甲", "乙" }) { vm.NewPersonName = name; await vm.AddPersonAsync(); }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, second.Id);
        Person unrelated;
        await using (var setup = database.CreateContext())
        {
            var service = new GenealogyDataService(setup);
            var other = await service.CreateGenealogyAsync(new Genealogy { Name = "另一谱" });
            unrelated = await service.AddPersonAsync(new Person { GenealogyId = other.Id, Name = "外谱人物" });
        }
        var candidate = Assert.Single(await vm.GetTargetLinkRemovalCandidatesAsync());
        await using (var external = database.CreateContext())
        {
            await using var transaction = await external.Database.BeginTransactionAsync();
            (await external.People.SingleAsync(item => item.Id == unrelated.Id)).Name = "已修改";
            await external.SaveChangesAsync();
            if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
        }
        await using (var verify = database.CreateContext())
        {
            var generation = await verify.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
            Assert.Equal(candidate.UnlinkGeneration + (commit ? 1 : 0), generation);
            Assert.Equal(commit ? "已修改" : "外谱人物", (await verify.People.SingleAsync(item => item.Id == unrelated.Id)).Name);
            Assert.Single(await verify.FamilyUnions.ToListAsync());
        }
        if (commit)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(candidate));
            candidate = Assert.Single(await vm.GetTargetLinkRemovalCandidatesAsync());
        }
        await vm.RemoveTargetLinkAsync(candidate);
        await using var final = database.CreateContext();
        Assert.Empty(await final.FamilyUnions.ToListAsync());
        Assert.Equal(3, await final.People.CountAsync());
    }

    private sealed class FailUnionInsert : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"FamilyUnions\"", StringComparison.Ordinal))
                throw new IOException("隔离测试：模拟婚配写入失败。");
            return ValueTask.FromResult(result);
        }
    }
}
