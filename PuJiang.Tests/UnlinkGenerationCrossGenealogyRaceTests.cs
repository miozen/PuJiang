using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

public class UnlinkGenerationCrossGenealogyRaceTests
{
    [Theory]
    [InlineData(TargetLinkRemovalKind.Relationship, true)]
    [InlineData(TargetLinkRemovalKind.Relationship, false)]
    [InlineData(TargetLinkRemovalKind.FamilyUnion, true)]
    [InlineData(TargetLinkRemovalKind.FamilyUnion, false)]
    [Trait("Category", "Integration")]
    public async Task Inflight_unlink_and_unrelated_genealogy_write_preserve_transaction_scope(TargetLinkRemovalKind kind, bool commit)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var pause = new PauseDelete();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").AddInterceptors(pause).Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "甲谱" };
        await vm.CreateAsync();
        var original = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙" }) { vm.NewPersonName = name; await vm.AddPersonAsync(); }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, second.Id);
        Person unrelated;
        await using (var setup = database.CreateContext())
        {
            var service = new GenealogyDataService(setup);
            var other = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
            unrelated = await service.AddPersonAsync(new Person { GenealogyId = other.Id, Name = "外谱" });
        }
        var candidate = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(item => item.Kind == kind);
        pause.Enabled = true;
        var unlink = vm.RemoveTargetLinkAsync(candidate);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var external = database.CreateContext();
            await using var transaction = await external.Database.BeginTransactionAsync();
            (await external.People.SingleAsync(person => person.Id == unrelated.Id)).Name = "外谱已修改";
            await external.SaveChangesAsync();
            if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
        }
        finally { pause.Release.TrySetResult(); }

        if (commit) await Assert.ThrowsAsync<InvalidOperationException>(() => unlink);
        else await unlink;
        await using (var verify = database.CreateContext())
        {
            var generation = await verify.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
            Assert.Equal(candidate.UnlinkGeneration + 1, generation); // Either the other edit or this unlink committed.
            Assert.Equal(commit ? "外谱已修改" : "外谱", (await verify.People.SingleAsync(person => person.Id == unrelated.Id)).Name);
            Assert.Equal(commit || kind != TargetLinkRemovalKind.Relationship ? 1 : 0,
                await verify.PersonRelationships.CountAsync(item => item.GenealogyId == original.Id));
            Assert.Equal(commit || kind != TargetLinkRemovalKind.FamilyUnion ? 1 : 0,
                await verify.FamilyUnions.CountAsync(item => item.GenealogyId == original.Id));
            Assert.Equal(3, await verify.People.CountAsync());
        }
        if (commit)
        {
            var fresh = (await vm.GetTargetLinkRemovalCandidatesAsync()).Single(item => item.Kind == kind);
            await vm.RemoveTargetLinkAsync(fresh);
        }
    }

    private sealed class PauseDelete : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("DELETE FROM", StringComparison.Ordinal))
            {
                Enabled = false;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
