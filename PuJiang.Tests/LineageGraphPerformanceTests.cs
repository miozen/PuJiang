using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class LineageGraphPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "UI")]
    public async Task Thousand_person_target_lineage_graph_baseline()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using (var context = database.CreateContext())
        {
            var genealogy = new Genealogy { Id = LineagePerformanceData.GenealogyId, Name = "虚构性能谱" };
            context.Genealogies.Add(genealogy);
            context.People.AddRange(Enumerable.Range(0, 1000).Select(i => new Person
                { Id = LineagePerformanceData.PersonId(i), GenealogyId = genealogy.Id, Name = $"虚构成员{i:D4}" }));
            await context.SaveChangesAsync();
            var relationships = new List<PersonRelationship>();
            for (var i = 1; i < 990; i++) relationships.Add(new PersonRelationship
            {
                Id = LineagePerformanceData.RelationshipId(i), GenealogyId = genealogy.Id,
                PersonId = LineagePerformanceData.PersonId(i), RelatedPersonId = LineagePerformanceData.PersonId((i - 1) / 3),
                Kind = PersonRelationshipKind.BiologicalParent
            });
            for (var i = 200; i < 500; i += 10) relationships.Add(new PersonRelationship
            {
                Id = LineagePerformanceData.RelationshipId(1000 + i), GenealogyId = genealogy.Id,
                PersonId = LineagePerformanceData.PersonId(i), RelatedPersonId = LineagePerformanceData.PersonId((i - 1) / 3 - 1),
                Kind = PersonRelationshipKind.AdoptiveParent
            });
            foreach (var i in new[] { 0, 200, 500, 990 }) relationships.Add(new PersonRelationship
            {
                Id = LineagePerformanceData.RelationshipId(2000 + i), GenealogyId = genealogy.Id,
                PersonId = LineagePerformanceData.PersonId(i), Kind = PersonRelationshipKind.LostAncestry
            });
            context.PersonRelationships.AddRange(relationships);
            await context.SaveChangesAsync();
            Assert.Equal(1000, await context.People.CountAsync());
            Assert.Equal(1023, await context.PersonRelationships.CountAsync());
        }

        var results = new List<LineagePerformanceResult>();
        for (var run = 0; run < 3; run++)
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(RestoreWorkerProgram).Assembly.Location);
            start.ArgumentList.Add("--lineage-graph-perf");
            start.ArgumentList.Add(Path.GetDirectoryName(database.DatabasePath)!);
            start.ArgumentList.Add(run == 0 ? "full" : "cold");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动性能测量子进程。");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            var text = await stdout;
            var error = await stderr;
            Assert.True(process.ExitCode == 0, $"测量子进程失败: {text} {error}");
            var line = text.Split('\n').Select(item => item.Trim()).LastOrDefault(item => item.StartsWith("RESULT:", StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"测量结果缺失: {text} {error}");
            var result = JsonSerializer.Deserialize<LineagePerformanceResult>(line["RESULT:".Length..]) ?? throw new InvalidOperationException("测量结果为空。");
            results.Add(result);
            output.WriteLine($"RUN_{run + 1}: {JsonSerializer.Serialize(result)}");
        }
        Assert.Equal(20, results[0].WarmOpenMs.Count);
        Assert.Equal(20, results[0].SearchNameMs.Count);
        Assert.Equal(20, results[0].SearchIdMs.Count);
        Assert.Equal(20, results[0].ExpandMs.Count);
        await using var verify = database.CreateContext();
        Assert.Equal(1000, await verify.People.CountAsync());
        Assert.Equal(1023, await verify.PersonRelationships.CountAsync());
    }
}
