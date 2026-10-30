using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class UnlinkGraphPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Performance")]
    public async Task Compare_bulk_graph_insert_with_and_without_relationship_and_union_triggers()
    {
        const int personCount = 1000;
        var samples = new Dictionary<bool, List<double>> { [false] = [], [true] = [] };
        for (var iteration = -1; iteration < 3; iteration++)
        foreach (var enabled in iteration % 2 == 0 ? new[] { false, true } : new[] { true, false })
        {
            await using var database = new TestDatabase();
            await database.MigrateAsync();
            await using var context = database.CreateContext();
            var genealogy = await new GenealogyDataService(context).CreateGenealogyAsync(new Genealogy { Name = "虚构基准谱" });
            var people = Enumerable.Range(0, personCount)
                .Select(index => new Person { GenealogyId = genealogy.Id, Name = $"虚构{index}" }).ToArray();
            context.People.AddRange(people);
            await context.SaveChangesAsync();
            if (!enabled)
            {
                // Only the disposable comparison database loses these two insert triggers.
                await context.Database.ExecuteSqlRawAsync("DROP TRIGGER UnlinkGenerationPersonRelationshipsINSERT");
                await context.Database.ExecuteSqlRawAsync("DROP TRIGGER UnlinkGenerationFamilyUnionsINSERT");
            }
            var before = await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
            var relationships = Enumerable.Range(0, personCount - 1)
                .Select(index => new PersonRelationship { GenealogyId = genealogy.Id, PersonId = people[index].Id,
                    RelatedPersonId = people[index + 1].Id, Kind = PersonRelationshipKind.Spouse }).ToArray();
            var unions = Enumerable.Range(0, personCount / 2)
                .Select(index => new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = people[index * 2].Id,
                    PartnerTwoId = people[index * 2 + 1].Id, Kind = FamilyUnionKind.Marriage }).ToArray();
            context.PersonRelationships.AddRange(relationships);
            context.FamilyUnions.AddRange(unions);
            var stopwatch = Stopwatch.StartNew();
            await context.SaveChangesAsync();
            stopwatch.Stop();
            Assert.Equal(personCount - 1, await context.PersonRelationships.CountAsync());
            Assert.Equal(personCount / 2, await context.FamilyUnions.CountAsync());
            var after = await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
            Assert.Equal(before + (enabled ? relationships.Length + unions.Length : 0), after);
            if (iteration >= 0) samples[enabled].Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        foreach (var enabled in new[] { false, true })
        {
            var values = samples[enabled];
            output.WriteLine($"Graph INSERT triggers {(enabled ? "on" : "off")}, 1000 people, 999 relationships, 500 unions: " +
                $"{string.Join(", ", values.Select(value => value.ToString("F1")))} ms; median {values.Order().ElementAt(1):F1} ms");
        }
    }
}
