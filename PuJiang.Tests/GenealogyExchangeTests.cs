using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    [Fact]
    [Trait("Category", "Unit")]
    public void Preflight_rejects_reversed_duplicate_unions_before_mapping()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var document = new GenealogyExchangeDocument(1,
            new(Guid.NewGuid(), "虚构族谱", null, null, DateTimeOffset.UtcNow),
            [
                new(first, "甲", PersonSex.Unknown, null, null, null, null, false, null, null, null, null, null),
                new(second, "乙", PersonSex.Unknown, null, null, null, null, false, null, null, null, null, null)
            ], [],
            [
                new(Guid.NewGuid(), first, second, FamilyUnionKind.Marriage, null, null, null),
                new(Guid.NewGuid(), second, first, FamilyUnionKind.Marriage, null, null, null)
            ], []);

        Assert.Throws<InvalidDataException>(() => GenealogyExchangeService.Preflight(document));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Persistent_import_creates_a_new_genealogy_and_complete_graph_after_preflight()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var document = CreateValidDocument();

        var result = await new GenealogyExchangeImportService(context).ImportAsync(document);

        Assert.True(result.Imported);
        Assert.True(result.Report.CanImport);
        Assert.Equal(document.Genealogy.Id, result.GenealogyId);
        Assert.NotNull(result.ImportRecordId);
        Assert.Contains("预检通过", result.Report.ToDisplayText());
        await using var verify = database.CreateContext();
        Assert.Equal(document.Genealogy.Id, (await verify.Genealogies.SingleAsync()).Id);
        Assert.Equal(document.People.Count, await verify.People.CountAsync());
        Assert.Equal(document.Relationships.Count, await verify.PersonRelationships.CountAsync());
        Assert.Equal(document.Unions.Count, await verify.FamilyUnions.CountAsync());
        Assert.Equal(document.Branches.Count, await verify.FamilyBranches.CountAsync());
        var provenance = Assert.Single(await new GenealogyDataService(verify).GetImportRecordsAsync(document.Genealogy.Id));
        Assert.Equal(result.ImportRecordId, provenance.Id);
        Assert.Equal(document.Genealogy.Id, provenance.GenealogyId);
        Assert.Equal("PuJiang.InternalJson", provenance.SourceKind);
        Assert.Equal(document.FormatVersion, provenance.SourceFormatVersion);
        Assert.Equal(document.Genealogy.Id, provenance.SourceGenealogyId);
        Assert.Equal(GenealogyExchangeService.ComputeDocumentFingerprint(document), provenance.SourceDocumentFingerprint);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Persistent_import_reports_existing_source_genealogy_and_leaves_database_unchanged()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var document = CreateValidDocument();
        await using (var setup = database.CreateContext())
        {
            setup.Genealogies.Add(new Genealogy { Id = document.Genealogy.Id, Name = "已存在的来源族谱" });
            await setup.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var importer = new GenealogyExchangeImportService(context);
        var preflight = await importer.PreflightAsync(document);
        var result = await importer.ImportAsync(document);

        Assert.False(preflight.Report.CanImport);
        Assert.Contains(preflight.Report.Issues, issue => issue.Code == "ExistingGenealogyId");
        Assert.Contains("ExistingGenealogyId", preflight.Report.ToDisplayText());
        Assert.False(result.Imported);
        await using var verify = database.CreateContext();
        Assert.Single(await verify.Genealogies.ToListAsync());
        Assert.Empty(await verify.People.ToListAsync());
        Assert.Empty(await verify.GenealogyImportRecords.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Persistent_import_allows_a_same_named_distinct_source_genealogy_without_identity_matching()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var document = CreateValidDocument();
        await using var context = database.CreateContext();
        context.Genealogies.Add(new Genealogy { Name = document.Genealogy.Name });
        await context.SaveChangesAsync();

        var result = await new GenealogyExchangeImportService(context).ImportAsync(document);

        Assert.True(result.Imported);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.Genealogies.CountAsync(item => item.Name == document.Genealogy.Name));
        Assert.Equal(document.People.Count, await verify.People.CountAsync());
        Assert.Single(await verify.GenealogyImportRecords.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Explicit_copy_import_remaps_every_graph_id_and_retains_original_provenance()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var document = CreateValidDocument();
        await using var context = database.CreateContext();
        var importer = new GenealogyExchangeImportService(context);
        var original = await importer.ImportAsync(document);
        var copy = await importer.ImportCopyAsync(document, "用户指定的副本名称");

        Assert.True(original.Imported);
        Assert.True(copy.Imported);
        Assert.True(copy.Report.IsCopyImport);
        Assert.Contains("全新内部 ID", copy.Report.ToDisplayText());
        await using var verify = database.CreateContext();
        var genealogies = await verify.Genealogies.OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(2, genealogies.Count);
        var copiedGenealogy = genealogies.Single(item => item.Id != document.Genealogy.Id);
        Assert.Equal(copy.GenealogyId, copiedGenealogy.Id);
        Assert.Equal("用户指定的副本名称", copiedGenealogy.Name);
        var originalPeople = await verify.People.Where(item => item.GenealogyId == document.Genealogy.Id).ToListAsync();
        var copiedPeople = await verify.People.Where(item => item.GenealogyId == copiedGenealogy.Id).ToListAsync();
        var copiedIds = copiedPeople.Select(item => item.Id).ToHashSet();
        Assert.Empty(originalPeople.Select(item => item.Id).Intersect(copiedIds));
        Assert.All(copiedPeople, item => Assert.NotEqual(document.Genealogy.Id, item.GenealogyId));
        var copiedBranches = await verify.FamilyBranches.Where(item => item.GenealogyId == copiedGenealogy.Id).ToListAsync();
        var copiedBranchIds = copiedBranches.Select(item => item.Id).ToHashSet();
        Assert.All(copiedBranches, item =>
        {
            Assert.DoesNotContain(item.Id, document.Branches.Select(source => source.Id));
            Assert.True(item.ParentBranchId is null || copiedBranchIds.Contains(item.ParentBranchId.Value));
        });
        Assert.All(await verify.PersonRelationships.Where(item => item.GenealogyId == copiedGenealogy.Id).ToListAsync(), item =>
        {
            Assert.Contains(item.PersonId, copiedIds);
            Assert.True(item.RelatedPersonId is null || copiedIds.Contains(item.RelatedPersonId.Value));
        });
        Assert.All(await verify.FamilyUnions.Where(item => item.GenealogyId == copiedGenealogy.Id).ToListAsync(), item =>
        {
            Assert.Contains(item.PartnerOneId, copiedIds);
            Assert.True(item.PartnerTwoId is null || copiedIds.Contains(item.PartnerTwoId.Value));
        });
        var record = Assert.Single(await new GenealogyDataService(verify).GetImportRecordsAsync(copiedGenealogy.Id));
        Assert.Equal("Copy", record.ImportMode);
        Assert.Equal(document.Genealogy.Id, record.SourceGenealogyId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Persistent_import_returns_readable_invalid_document_report_without_writing()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var personId = Guid.NewGuid();
        var invalid = new GenealogyExchangeDocument(1,
            new(Guid.NewGuid(), "无效引用谱", null, null, DateTimeOffset.UtcNow),
            [new(personId, "甲", PersonSex.Unknown, null, null, null, null, false, null, null, null, null, null)],
            [new(Guid.NewGuid(), personId, Guid.NewGuid(), PersonRelationshipKind.BiologicalParent, false, null, null)], [], []);

        await using var context = database.CreateContext();
        var result = await new GenealogyExchangeImportService(context).ImportAsync(invalid);

        Assert.False(result.Imported);
        Assert.Contains(result.Report.Issues, issue => issue.Code == "InvalidDocument");
        Assert.Contains("预检拒绝：未写入任何数据", result.Report.ToDisplayText());
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.Genealogies.ToListAsync());
        Assert.Empty(await verify.GenealogyImportRecords.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Persistent_import_database_failure_rolls_back_the_new_genealogy_and_graph()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(new FailFamilyUnionInsertInterceptor())
            .Options;
        await using var context = new ZupuDbContext(options);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            new GenealogyExchangeImportService(context).ImportAsync(CreateValidDocument()));
        Assert.IsType<IOException>(error.InnerException);

        await using var verify = database.CreateContext();
        Assert.Empty(await verify.Genealogies.ToListAsync());
        Assert.Empty(await verify.FamilyBranches.ToListAsync());
        Assert.Empty(await verify.People.ToListAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        Assert.Empty(await verify.FamilyUnions.ToListAsync());
        Assert.Empty(await verify.GenealogyImportRecords.ToListAsync());
    }

    private static GenealogyExchangeDocument CreateValidDocument()
    {
        var genealogyId = Guid.NewGuid();
        var rootBranchId = Guid.NewGuid();
        var childBranchId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        return new(1, new(genealogyId, "导入谱-" + genealogyId.ToString("N"), "陈", "隔离导入", DateTimeOffset.UtcNow),
            [
                new(parentId, "祖", PersonSex.Male, null, null, null, null, true, "一世", 1, null, null, rootBranchId),
                new(childId, "孙", PersonSex.Female, null, null, null, null, false, "二世", 2, null, null, childBranchId)
            ],
            [new(Guid.NewGuid(), childId, parentId, PersonRelationshipKind.BiologicalParent, true, "谱载", "卷一")],
            [new(Guid.NewGuid(), parentId, childId, FamilyUnionKind.Unknown, null, null, "测试婚配")],
            [new(rootBranchId, null, "长房", 1), new(childBranchId, rootBranchId, "长房次支", 2)]);
    }

    private sealed class FailFamilyUnionInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"FamilyUnions\"", StringComparison.Ordinal))
                throw new IOException("模拟婚配写入失败。");
            return ValueTask.FromResult(result);
        }
    }
}
