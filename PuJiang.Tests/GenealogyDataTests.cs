using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class GenealogyDataTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Design_time_factory_creates_sqlite_context_without_opening_a_database()
    {
        using var context = new ZupuDbContextFactory().CreateDbContext([]);

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Migration_creates_target_schema_in_an_isolated_database()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();

        await using var context = database.CreateContext();
        var migrations = await context.Database.GetAppliedMigrationsAsync();

        Assert.Contains("20261025000100_InitialPuJiang", migrations);
        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task People_queries_are_scoped_to_the_requested_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);

        var first = await service.CreateGenealogyAsync(new Genealogy { Name = "陈氏宗谱", Surname = "陈" });
        var second = await service.CreateGenealogyAsync(new Genealogy { Name = "林氏宗谱", Surname = "林" });
        await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "陈甲" });
        await service.AddPersonAsync(new Person { GenealogyId = second.Id, Name = "林乙" });

        var people = await service.GetPeopleAsync(first.Id);

        var person = Assert.Single(people);
        Assert.Equal("陈甲", person.Name);
        Assert.All(people, p => Assert.Equal(first.Id, p.GenealogyId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Relationship_service_rejects_cross_genealogy_relationships()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);

        var first = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var second = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var child = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "甲子" });
        var foreignParent = await service.AddPersonAsync(new Person { GenealogyId = second.Id, Name = "乙父" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = first.Id,
            PersonId = child.Id,
            RelatedPersonId = foreignParent.Id,
            Kind = PersonRelationshipKind.BiologicalParent
        }));

        Assert.Contains("跨族谱", exception.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Relationship_service_rejects_parent_cycles()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);

        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "循环校验谱" });
        var child = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "子" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "父" });
        await service.AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = genealogy.Id,
            PersonId = child.Id,
            RelatedPersonId = parent.Id,
            Kind = PersonRelationshipKind.BiologicalParent
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = genealogy.Id,
            PersonId = parent.Id,
            RelatedPersonId = child.Id,
            Kind = PersonRelationshipKind.BiologicalParent
        }));

        Assert.Contains("循环", exception.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Relationship_service_allows_lost_ancestry_without_a_related_person()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "失考谱" });
        var person = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "虚构失考人物" });

        var relationship = await service.AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = genealogy.Id,
            PersonId = person.Id,
            Kind = PersonRelationshipKind.LostAncestry
        });

        Assert.Null(relationship.RelatedPersonId);
        Assert.Equal(PersonRelationshipKind.LostAncestry, relationship.Kind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Relationship_service_rejects_duplicate_relationships_without_writing_a_second_record()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "重复关系谱" });
        var child = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "虚构子" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "虚构父" });
        var relationship = new PersonRelationship
        {
            GenealogyId = genealogy.Id,
            PersonId = child.Id,
            RelatedPersonId = parent.Id,
            Kind = PersonRelationshipKind.BiologicalParent
        };
        await service.AddRelationshipAsync(relationship);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = genealogy.Id,
            PersonId = child.Id,
            RelatedPersonId = parent.Id,
            Kind = PersonRelationshipKind.BiologicalParent
        }));

        Assert.Contains("已经存在", exception.Message);
        Assert.Single(await context.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Batch_write_commits_people_and_relationships_in_one_transaction()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "事务提交谱" });
        var parent = new Person { GenealogyId = genealogy.Id, Name = "虚构父" };
        var child = new Person { GenealogyId = genealogy.Id, Name = "虚构子" };

        await service.AddPeopleAndRelationshipsAsync(genealogy.Id, [parent, child],
        [
            new PersonRelationship
            {
                GenealogyId = genealogy.Id,
                PersonId = child.Id,
                RelatedPersonId = parent.Id,
                Kind = PersonRelationshipKind.BiologicalParent
            }
        ]);

        await using var verification = database.CreateContext();
        Assert.Equal(2, await verification.People.CountAsync(person => person.GenealogyId == genealogy.Id));
        Assert.Single(await verification.PersonRelationships.Where(relationship => relationship.GenealogyId == genealogy.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Batch_write_rolls_back_staged_people_when_relationship_validation_fails()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var target = await service.CreateGenealogyAsync(new Genealogy { Name = "事务回滚谱" });
        var foreign = await service.CreateGenealogyAsync(new Genealogy { Name = "外部谱" });
        var foreignPerson = await service.AddPersonAsync(new Person { GenealogyId = foreign.Id, Name = "虚构外部人物" });
        var staged = new Person { GenealogyId = target.Id, Name = "虚构暂存人物" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPeopleAndRelationshipsAsync(target.Id, [staged],
        [
            new PersonRelationship
            {
                GenealogyId = target.Id,
                PersonId = staged.Id,
                RelatedPersonId = foreignPerson.Id,
                Kind = PersonRelationshipKind.BiologicalParent
            }
        ]));

        Assert.Contains("跨族谱", exception.Message);
        await using var verification = database.CreateContext();
        Assert.Empty(await verification.People.Where(person => person.GenealogyId == target.Id).ToListAsync());
        Assert.Empty(await verification.PersonRelationships.Where(relationship => relationship.GenealogyId == target.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Batch_cycle_is_rejected_and_context_can_be_reused_without_resurrecting_rolled_back_entities()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "批次循环谱" });
        var a = new Person { GenealogyId = genealogy.Id, Name = "虚构甲" };
        var b = new Person { GenealogyId = genealogy.Id, Name = "虚构乙" };
        var c = new Person { GenealogyId = genealogy.Id, Name = "虚构丙" };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPeopleAndRelationshipsAsync(genealogy.Id, [a, b, c],
        [
            new() { GenealogyId = genealogy.Id, PersonId = a.Id, RelatedPersonId = b.Id, Kind = PersonRelationshipKind.BiologicalParent },
            new() { GenealogyId = genealogy.Id, PersonId = b.Id, RelatedPersonId = c.Id, Kind = PersonRelationshipKind.AdoptiveParent },
            new() { GenealogyId = genealogy.Id, PersonId = c.Id, RelatedPersonId = a.Id, Kind = PersonRelationshipKind.HeirParent }
        ]));
        Assert.Contains("循环", error.Message);
        await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "正常人物" });
        await using var verification = database.CreateContext();
        Assert.Equal("正常人物", (await verification.People.SingleAsync()).Name);
        Assert.Empty(await verification.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Duplicate_lost_ancestry_is_rejected_in_single_and_batch_writes()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "失考去重谱" });
        var existing = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "已有" });
        PersonRelationship Lost(Guid id) => new() { GenealogyId = genealogy.Id, PersonId = id, Kind = PersonRelationshipKind.LostAncestry };
        await service.AddRelationshipAsync(Lost(existing.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRelationshipAsync(Lost(existing.Id)));
        var staged = new Person { GenealogyId = genealogy.Id, Name = "暂存" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPeopleAndRelationshipsAsync(genealogy.Id, [staged], [Lost(staged.Id), Lost(staged.Id)]));
        await using var verification = database.CreateContext();
        Assert.Single(await verification.People.ToListAsync());
        Assert.Single(await verification.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Batch_rejects_cycle_spanning_existing_and_new_relationships()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "混合循环谱" });
        var a = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        var b = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "乙" });
        await service.AddRelationshipAsync(new() { GenealogyId = genealogy.Id, PersonId = a.Id, RelatedPersonId = b.Id, Kind = PersonRelationshipKind.BiologicalParent });
        var c = new Person { GenealogyId = genealogy.Id, Name = "丙" };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPeopleAndRelationshipsAsync(genealogy.Id, [c],
        [
            new() { GenealogyId = genealogy.Id, PersonId = b.Id, RelatedPersonId = c.Id, Kind = PersonRelationshipKind.BiologicalParent },
            new() { GenealogyId = genealogy.Id, PersonId = c.Id, RelatedPersonId = a.Id, Kind = PersonRelationshipKind.BiologicalParent }
        ]));
        Assert.Contains("循环", error.Message);
        await using var verification = database.CreateContext();
        Assert.Equal(2, await verification.People.CountAsync());
        Assert.Single(await verification.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Batch_rejects_duplicate_parent_relationship_and_rolls_back_all_staged_data()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "批次重复谱" });
        var child = new Person { GenealogyId = genealogy.Id, Name = "子" };
        var parent = new Person { GenealogyId = genealogy.Id, Name = "父" };
        PersonRelationship Parent() => new() { GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.BiologicalParent };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPeopleAndRelationshipsAsync(genealogy.Id, [child, parent], [Parent(), Parent()]));
        Assert.Contains("已经存在", error.Message);
        await using var verification = database.CreateContext();
        Assert.Empty(await verification.People.ToListAsync());
        Assert.Empty(await verification.PersonRelationships.ToListAsync());
    }
}
