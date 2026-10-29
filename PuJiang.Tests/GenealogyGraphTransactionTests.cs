using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class GenealogyGraphTransactionTests
{
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
}
