using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class GenealogyExchangeTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Versioned_document_preflight_and_target_mapping_preserve_every_target_collection()
    {
        await using var database = new TestDatabase();
        var genealogyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var spouseId = Guid.NewGuid();
        var snapshot = new GenealogyGraphSnapshot(
            new(genealogyId, "虚构族谱", "虚构姓氏", "说明", DateTimeOffset.Parse("2026-10-28T00:00:00Z")),
            [
                new(personId, "虚构人物", PersonSex.Male, "1900", null, "生平", "photos/member.jpg", true, "一世", 1, "长", 1, branchId),
                new(spouseId, "虚构配偶", PersonSex.Female, null, null, null, null, false, null, null, null, null, null)
            ],
            [new(Guid.NewGuid(), personId, spouseId, PersonRelationshipKind.Spouse, true, "谱载", "卷一")],
            [new(Guid.NewGuid(), personId, spouseId, FamilyUnionKind.Marriage, "1920", null, "婚配")],
            [new(branchId, null, "长房", 1)]);
        var root = Path.GetDirectoryName(database.DatabasePath)!;
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "genealogy-exchange.json");

        await GenealogyExchangeService.WriteAsync(snapshot, path);
        var (document, report) = await GenealogyExchangeService.PreflightAsync(path);
        var mapped = GenealogyExchangeService.MapToTarget(document);

        Assert.Equal(1, report.FormatVersion);
        Assert.Equal(2, report.People);
        Assert.Equal(1, report.PhotoPathReferences);
        Assert.Equal(genealogyId, mapped.Genealogy.Id);
        Assert.Equal(2, mapped.People.Count);
        Assert.Equal(branchId, mapped.People.Single(person => person.Id == personId).BranchId);
        Assert.Single(mapped.Relationships);
        Assert.Single(mapped.Unions);
        Assert.Single(mapped.Branches);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Preflight_rejects_cross_document_relationship_references()
    {
        var personId = Guid.NewGuid();
        var document = new GenealogyExchangeDocument(1,
            new(Guid.NewGuid(), "虚构族谱", null, null, DateTimeOffset.UtcNow),
            [new(personId, "虚构人物", PersonSex.Unknown, null, null, null, null, false, null, null, null, null, null)],
            [new(Guid.NewGuid(), personId, Guid.NewGuid(), PersonRelationshipKind.BiologicalParent, false, null, null)],
            [], []);

        Assert.Throws<InvalidDataException>(() => GenealogyExchangeService.Preflight(document));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Preflight_rejects_branch_cycles_before_mapping()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var document = new GenealogyExchangeDocument(1,
            new(Guid.NewGuid(), "虚构族谱", null, null, DateTimeOffset.UtcNow), [], [], [],
            [new(first, second, "一房", 1), new(second, first, "二房", 2)]);

        Assert.Throws<InvalidDataException>(() => GenealogyExchangeService.MapToTarget(document));
    }
}
