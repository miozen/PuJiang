using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class GenealogyGraphTransactionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Deletion_before_stale_relationship_commit_leaves_no_dangling_relationship()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var (genealogyId, childId, parentId) = await CreatePersonPairAsync(database, "关系删除竞争谱");
        await using var stale = database.CreateContext();
        var candidate = new PersonRelationship { GenealogyId = genealogyId, PersonId = childId, RelatedPersonId = parentId, Kind = PersonRelationshipKind.BiologicalParent };
        await new PersonRelationshipValidator(stale).ValidateForCreateAsync(candidate);
        await using (var deleting = database.CreateContext())
            await new GenealogyDataService(deleting).DeleteUnrelatedPersonAsync(genealogyId, childId);
        stale.PersonRelationships.Add(candidate);

        await Assert.ThrowsAsync<DbUpdateException>(() => stale.SaveChangesAsync());
        await using var verify = database.CreateContext();
        Assert.Null(await verify.People.FindAsync(childId));
        Assert.Empty(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogyId).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Deletion_before_stale_union_commit_leaves_no_dangling_union()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var (genealogyId, oneId, twoId) = await CreatePersonPairAsync(database, "婚配删除竞争谱");
        await using var stale = database.CreateContext();
        var candidate = new FamilyUnion { GenealogyId = genealogyId, PartnerOneId = oneId, PartnerTwoId = twoId, Kind = FamilyUnionKind.Marriage };
        await new FamilyUnionValidator(stale).ValidateForCreateAsync(candidate);
        await using (var deleting = database.CreateContext())
            await new GenealogyDataService(deleting).DeleteUnrelatedPersonAsync(genealogyId, oneId);
        stale.FamilyUnions.Add(candidate);

        await Assert.ThrowsAsync<DbUpdateException>(() => stale.SaveChangesAsync());
        await using var verify = database.CreateContext();
        Assert.Null(await verify.People.FindAsync(oneId));
        Assert.Empty(await verify.FamilyUnions.Where(item => item.GenealogyId == genealogyId).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Relationship_commit_before_deletion_keeps_the_person_and_relationship()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var (genealogyId, childId, parentId) = await CreatePersonPairAsync(database, "关系写入竞争谱");
        await using (var creating = database.CreateContext())
            await new GenealogyDataService(creating).AddExistingBiologicalChildAsync(genealogyId, parentId, childId);
        await using var deleting = database.CreateContext();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GenealogyDataService(deleting).DeleteUnrelatedPersonAsync(genealogyId, childId));

        Assert.Contains("关联关系", error.Message);
        await using var verify = database.CreateContext();
        Assert.NotNull(await verify.People.FindAsync(childId));
        Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogyId).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Union_commit_before_deletion_keeps_the_person_and_union()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var (genealogyId, oneId, twoId) = await CreatePersonPairAsync(database, "婚配写入竞争谱");
        await using (var creating = database.CreateContext())
            await new GenealogyDataService(creating).AddFamilyUnionAsync(new FamilyUnion
            { GenealogyId = genealogyId, PartnerOneId = oneId, PartnerTwoId = twoId, Kind = FamilyUnionKind.Marriage });
        await using var deleting = database.CreateContext();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GenealogyDataService(deleting).DeleteUnrelatedPersonAsync(genealogyId, oneId));

        Assert.Contains("关联关系", error.Message);
        await using var verify = database.CreateContext();
        Assert.NotNull(await verify.People.FindAsync(oneId));
        Assert.Single(await verify.FamilyUnions.Where(item => item.GenealogyId == genealogyId).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Independent_contexts_with_stale_validation_cannot_commit_duplicate_relationships()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Guid genealogyId, childId, parentId;
        await using (var setup = database.CreateContext())
        {
            var service = new GenealogyDataService(setup);
            var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "关系并发谱" });
            genealogyId = genealogy.Id;
            childId = (await service.AddPersonAsync(new Person { GenealogyId = genealogyId, Name = "子" })).Id;
            parentId = (await service.AddPersonAsync(new Person { GenealogyId = genealogyId, Name = "父" })).Id;
        }
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCandidate = new PersonRelationship { GenealogyId = genealogyId, PersonId = childId, RelatedPersonId = parentId, Kind = PersonRelationshipKind.BiologicalParent };
        var secondCandidate = new PersonRelationship { GenealogyId = genealogyId, PersonId = childId, RelatedPersonId = parentId, Kind = PersonRelationshipKind.BiologicalParent };

        await new PersonRelationshipValidator(first).ValidateForCreateAsync(firstCandidate);
        await new PersonRelationshipValidator(second).ValidateForCreateAsync(secondCandidate);
        first.PersonRelationships.Add(firstCandidate);
        await first.SaveChangesAsync();
        second.PersonRelationships.Add(secondCandidate);

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        await using var verify = database.CreateContext();
        Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogyId).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Independent_contexts_with_stale_validation_cannot_commit_reversed_duplicate_unions()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        Guid genealogyId, oneId, twoId;
        await using (var setup = database.CreateContext())
        {
            var service = new GenealogyDataService(setup);
            var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "婚配并发谱" });
            genealogyId = genealogy.Id;
            oneId = (await service.AddPersonAsync(new Person { GenealogyId = genealogyId, Name = "甲" })).Id;
            twoId = (await service.AddPersonAsync(new Person { GenealogyId = genealogyId, Name = "乙" })).Id;
        }
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCandidate = new FamilyUnion { GenealogyId = genealogyId, PartnerOneId = oneId, PartnerTwoId = twoId, Kind = FamilyUnionKind.Marriage };
        var secondCandidate = new FamilyUnion { GenealogyId = genealogyId, PartnerOneId = twoId, PartnerTwoId = oneId, Kind = FamilyUnionKind.Marriage };

        await new FamilyUnionValidator(first).ValidateForCreateAsync(firstCandidate);
        await new FamilyUnionValidator(second).ValidateForCreateAsync(secondCandidate);
        Assert.Equal(firstCandidate.PartnerOneId, secondCandidate.PartnerOneId);
        Assert.Equal(firstCandidate.PartnerTwoId, secondCandidate.PartnerTwoId);
        first.FamilyUnions.Add(firstCandidate);
        await first.SaveChangesAsync();
        second.FamilyUnions.Add(secondCandidate);

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        await using var verify = database.CreateContext();
        var union = Assert.Single(await verify.FamilyUnions.Where(item => item.GenealogyId == genealogyId).ToListAsync());
        Assert.True(union.PartnerTwoId is Guid secondId && union.PartnerOneId.CompareTo(secondId) < 0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Graph_batch_commits_same_genealogy_branches_people_relationships_and_unions()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "完整图数据谱" });
        var root = new FamilyBranch { GenealogyId = genealogy.Id, Name = "长房" };
        var childBranch = new FamilyBranch { GenealogyId = genealogy.Id, ParentBranchId = root.Id, Name = "次房" };
        var parent = new Person { GenealogyId = genealogy.Id, Name = "父", BranchId = root.Id };
        var child = new Person { GenealogyId = genealogy.Id, Name = "子", BranchId = childBranch.Id };

        await service.AddGraphRecordsAsync(genealogy.Id, [root, childBranch], [parent, child],
        [
            new PersonRelationship
            {
                GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id,
                Kind = PersonRelationshipKind.Spouse
            }
        ],
        [new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = parent.Id, PartnerTwoId = child.Id, Kind = FamilyUnionKind.Unknown }]);

        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.FamilyBranches.CountAsync(branch => branch.GenealogyId == genealogy.Id));
        Assert.Equal(2, await verify.People.CountAsync(person => person.GenealogyId == genealogy.Id));
        Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        Assert.Single(await verify.FamilyUnions.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cross_genealogy_union_rejects_the_whole_staged_graph()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var target = await service.CreateGenealogyAsync(new Genealogy { Name = "目标图数据谱" });
        var foreign = await service.CreateGenealogyAsync(new Genealogy { Name = "外部图数据谱" });
        var foreignPerson = await service.AddPersonAsync(new Person { GenealogyId = foreign.Id, Name = "外部人物" });
        var branch = new FamilyBranch { GenealogyId = target.Id, Name = "暂存房支" };
        var staged = new Person { GenealogyId = target.Id, Name = "暂存人物", BranchId = branch.Id };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddGraphRecordsAsync(target.Id, [branch], [staged], [],
            [new FamilyUnion { GenealogyId = target.Id, PartnerOneId = staged.Id, PartnerTwoId = foreignPerson.Id }]));

        Assert.Contains("跨族谱", error.Message);
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.FamilyBranches.Where(item => item.GenealogyId == target.Id).ToListAsync());
        Assert.Empty(await verify.People.Where(item => item.GenealogyId == target.Id).ToListAsync());
        Assert.Empty(await verify.FamilyUnions.Where(item => item.GenealogyId == target.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Reversed_duplicate_unions_are_rejected_and_roll_back_the_whole_staged_graph()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "无向婚配去重谱" });
        var one = new Person { GenealogyId = genealogy.Id, Name = "甲" };
        var two = new Person { GenealogyId = genealogy.Id, Name = "乙" };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddGraphRecordsAsync(genealogy.Id, [], [one, two], [],
        [
            new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = one.Id, PartnerTwoId = two.Id, Kind = FamilyUnionKind.Marriage },
            new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = two.Id, PartnerTwoId = one.Id, Kind = FamilyUnionKind.Marriage }
        ]));

        Assert.Contains("已经存在", error.Message);
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.People.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        Assert.Empty(await verify.FamilyUnions.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Branch_cycle_is_rejected_before_a_graph_batch_writes_anything()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "循环房支谱" });
        var first = new FamilyBranch { GenealogyId = genealogy.Id, Name = "一房" };
        var second = new FamilyBranch { GenealogyId = genealogy.Id, ParentBranchId = first.Id, Name = "二房" };
        first.ParentBranchId = second.Id;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddGraphRecordsAsync(genealogy.Id, [first, second], [], [], []));

        Assert.Contains("循环", error.Message);
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.FamilyBranches.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Database_failure_while_adding_union_rolls_back_the_complete_graph_batch()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using (var initial = database.CreateContext())
        {
            initial.Genealogies.Add(new Genealogy { Name = "写入失败图数据谱" });
            await initial.SaveChangesAsync();
        }

        await using var lookup = database.CreateContext();
        var genealogy = await lookup.Genealogies.SingleAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(new FailFamilyUnionInsertInterceptor())
            .Options;
        await using var failing = new ZupuDbContext(options);
        var service = new GenealogyDataService(failing);
        var branch = new FamilyBranch { GenealogyId = genealogy.Id, Name = "暂存房支" };
        var one = new Person { GenealogyId = genealogy.Id, Name = "甲", BranchId = branch.Id };
        var two = new Person { GenealogyId = genealogy.Id, Name = "乙", BranchId = branch.Id };

        await Assert.ThrowsAnyAsync<Exception>(() => service.AddGraphRecordsAsync(genealogy.Id, [branch], [one, two],
        [new PersonRelationship { GenealogyId = genealogy.Id, PersonId = two.Id, RelatedPersonId = one.Id, Kind = PersonRelationshipKind.Spouse }],
        [new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = one.Id, PartnerTwoId = two.Id }]));

        await using var verify = database.CreateContext();
        Assert.Empty(await verify.FamilyBranches.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        Assert.Empty(await verify.People.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        Assert.Empty(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        Assert.Empty(await verify.FamilyUnions.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
    }

    private sealed class FailFamilyUnionInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"FamilyUnions\"", StringComparison.Ordinal))
                throw new IOException("模拟婚配写入失败。");
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<(Guid GenealogyId, Guid FirstPersonId, Guid SecondPersonId)> CreatePersonPairAsync(
        TestDatabase database, string genealogyName)
    {
        await using var setup = database.CreateContext();
        var service = new GenealogyDataService(setup);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = genealogyName });
        var first = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        var second = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "乙" });
        return (genealogy.Id, first.Id, second.Id);
    }
}
