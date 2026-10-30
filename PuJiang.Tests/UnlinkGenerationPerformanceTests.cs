using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using ZupuPaiBan.Data;

namespace PuJiang.Tests;

public class UnlinkGenerationPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Performance")]
    public async Task Isolated_gedcom_import_timings_with_and_without_people_insert_trigger()
    {
        const int count = 250;
        var content = new StringBuilder("0 HEAD\n1 GEDC\n2 VERS 5.5.1\n");
        for (var i = 1; i <= count; i++)
            content.Append($"0 @I{i}@ INDI\n1 NAME 虚构{i} /氏/\n");
        content.Append("0 TRLR\n");
        var timings = new Dictionary<bool, List<double>> { [false] = [], [true] = [] };
        // Warm both paths once before recording; alternate subsequent order to reduce first-run bias.
        for (var iteration = -1; iteration < 3; iteration++)
        foreach (var enabled in iteration % 2 == 0 ? new[] { false, true } : new[] { true, false })
        {
            await using var database = new TestDatabase();
            await database.MigrateAsync();
            var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "fictional.ged");
            await File.WriteAllTextAsync(path, content.ToString());
            await using var context = database.CreateContext();
            // Only in this disposable benchmark DB: isolate the People INSERT trigger's cost.
            if (!enabled) await context.Database.ExecuteSqlRawAsync("DROP TRIGGER UnlinkGenerationPeopleINSERT");
            var importer = new ExternalGenealogyExchangeImportService(context);
            var stopwatch = Stopwatch.StartNew();
            var result = await importer.ImportGedcomAsync(path, "样本谱", acceptLosses: true);
            stopwatch.Stop();
            Assert.True(result.Imported);
            Assert.Equal(count, await context.People.CountAsync());
            var generation = await context.Database.SqlQueryRaw<long>("SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1").SingleAsync();
            Assert.Equal(enabled ? count : 0, generation);
            if (iteration >= 0) timings[enabled].Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        foreach (var enabled in new[] { false, true })
        {
            var samples = timings[enabled];
            output.WriteLine($"People INSERT trigger {(enabled ? "on" : "off")}, {count} people: " +
                $"{string.Join(", ", samples.Select(value => value.ToString("F1")))} ms; median {samples.Order().ElementAt(1):F1} ms");
        }
    }
}
