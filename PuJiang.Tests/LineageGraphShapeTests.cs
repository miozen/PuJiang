using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

[Collection("ProcessWorkerExclusive")]
public class LineageGraphShapeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("deep")]
    [InlineData("wide")]
    [InlineData("balanced")]
    [Trait("Category", "Performance")]
    [Trait("Category", "UI")]
    public async Task Measure_isolated_thousand_person_graph_shapes(string shape)
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using (var context = database.CreateContext())
        {
            context.Genealogies.Add(new Genealogy { Id = LineagePerformanceData.GenealogyId, Name = "虚构测量谱" });
            context.People.AddRange(Enumerable.Range(0, 1000).Select(i => new Person {
                Id = LineagePerformanceData.PersonId(i), GenealogyId = LineagePerformanceData.GenealogyId, Name = $"虚构成员{i:D4}" }));
            await context.SaveChangesAsync();
            var count = shape == "balanced" ? 990 : 1000;
            context.PersonRelationships.AddRange(Enumerable.Range(1, count - 1).Select(i => new PersonRelationship {
                Id = LineagePerformanceData.RelationshipId(i), GenealogyId = LineagePerformanceData.GenealogyId,
                PersonId = LineagePerformanceData.PersonId(i), RelatedPersonId = LineagePerformanceData.PersonId(
                    shape == "deep" ? i - 1 : shape == "wide" ? 0 : (i - 1) / 3), Kind = PersonRelationshipKind.BiologicalParent }));
            if (shape == "balanced")
            {
                context.PersonRelationships.AddRange(Enumerable.Range(20, 30).Select(n => n * 10).Select(i => new PersonRelationship {
                    Id = LineagePerformanceData.RelationshipId(1000 + i), GenealogyId = LineagePerformanceData.GenealogyId,
                    PersonId = LineagePerformanceData.PersonId(i), RelatedPersonId = LineagePerformanceData.PersonId((i - 1) / 3 - 1),
                    Kind = PersonRelationshipKind.AdoptiveParent }));
                context.PersonRelationships.AddRange(new[] { 0, 200, 500, 990 }.Select(i => new PersonRelationship {
                    Id = LineagePerformanceData.RelationshipId(2000 + i), GenealogyId = LineagePerformanceData.GenealogyId,
                    PersonId = LineagePerformanceData.PersonId(i), Kind = PersonRelationshipKind.LostAncestry }));
            }
            await context.SaveChangesAsync();
        }
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(RestoreWorkerProgram).Assembly.Location);
        start.ArgumentList.Add("--lineage-shape-perf");
        start.ArgumentList.Add(Path.GetDirectoryName(database.DatabasePath)!);
        start.ArgumentList.Add(shape);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动隔离测量子进程。");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); output.WriteLine("TIMEOUT: 180s"); }
        var text = await stdout; var error = await stderr;
        output.WriteLine($"SHAPE {shape}\n{text}\n{error}\nEXIT {process.ExitCode}");
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("RESULT:", text);
        await using var verify = database.CreateContext();
        Assert.Equal(1000, await verify.People.CountAsync());
        Assert.Equal(shape == "balanced" ? 1023 : 999, await verify.PersonRelationships.CountAsync());
    }
}
