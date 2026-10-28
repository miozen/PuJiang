using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace PuJiang.Tests;

public class LegacyImportTransactionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Replacing_members_rolls_back_if_writing_the_new_import_fails()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using (var initial = database.CreateContext())
        {
            initial.Members.Add(new FamilyMember { Id = 1, Name = "原数据" });
            await initial.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(new FailMemberInsertInterceptor())
            .Options;
        var service = new DataService(new TargetContextCoordinator(options));

        await Assert.ThrowsAsync<DbUpdateException>(() => service.ReplaceMembersAsync(
        [
            new FamilyMember { Id = 1, Name = "新根" },
            new FamilyMember { Id = 2, Name = "新子", FatherId = 1 }
        ]));

        await using var verify = database.CreateContext();
        var members = await verify.Members.OrderBy(member => member.Id).ToListAsync();
        Assert.Equal("原数据", Assert.Single(members).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Replacing_members_commits_the_complete_valid_import()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
        var service = new DataService(new TargetContextCoordinator(options));
        await service.AddMemberAsync(new FamilyMember { Id = 1, Name = "旧成员" });

        await service.ReplaceMembersAsync(
        [
            new FamilyMember { Id = 10, Name = "新根" },
            new FamilyMember { Id = 11, Name = "新子", FatherId = 10 }
        ]);

        await using var verify = database.CreateContext();
        var members = await verify.Members.OrderBy(member => member.Id).ToListAsync();
        Assert.Equal(["新根", "新子"], members.Select(member => member.Name));
        Assert.Equal(10, members[1].FatherId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Appending_members_rejects_invalid_references_without_partial_writes()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
        var service = new DataService(new TargetContextCoordinator(options));
        await service.AddMemberAsync(new FamilyMember { Id = 1, Name = "既有父亲" });

        await Assert.ThrowsAsync<InvalidDataException>(() => service.AppendMembersAsync(
        [
            new FamilyMember { Id = 2, Name = "有效子女", FatherId = 1 },
            new FamilyMember { Id = 3, Name = "无效成员", FatherId = 999 }
        ]));

        Assert.Equal("既有父亲", Assert.Single(await service.GetAllMembersAsync()).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Appending_members_rolls_back_if_writing_the_import_fails()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using (var initial = database.CreateContext())
        {
            initial.Members.Add(new FamilyMember { Id = 1, Name = "既有父亲" });
            await initial.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(new FailMemberInsertInterceptor())
            .Options;
        var service = new DataService(new TargetContextCoordinator(options));

        await Assert.ThrowsAsync<DbUpdateException>(() => service.AppendMembersAsync(
        [new FamilyMember { Id = 2, Name = "新成员", FatherId = 1 }]));

        await using var verify = database.CreateContext();
        Assert.Equal("既有父亲", Assert.Single(await verify.Members.ToListAsync()).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Collaboration_import_rejects_a_missing_package_parent_without_writing_members()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False").Options;
        var service = new DataService(new TargetContextCoordinator(options));
        var target = await service.AddMemberAsync(new FamilyMember { Id = 1, Name = "目标父亲" });
        var packagePath = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "invalid-collaboration.json");
        await File.WriteAllTextAsync(packagePath, System.Text.Json.JsonSerializer.Serialize(new CollaborationPackage
        {
            Members = [new CollaborationMember { TempId = 1, Name = "无效成员", FatherTempId = 99, Generation = 1 }]
        }));

        await Assert.ThrowsAsync<InvalidDataException>(() => new CollaborationService().ImportSubtreeJson(packagePath, target.Id, service));

        Assert.Equal("目标父亲", Assert.Single(await service.GetAllMembersAsync()).Name);
    }

    private sealed class FailMemberInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"Members\"", StringComparison.Ordinal))
                throw new IOException("模拟导入写入失败。");
            return ValueTask.FromResult(result);
        }
    }
}
