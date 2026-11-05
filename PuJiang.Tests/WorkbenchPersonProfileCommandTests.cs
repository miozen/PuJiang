using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;

namespace PuJiang.Tests;

public class WorkbenchPersonProfileCommandTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Draft_is_scoped_validated_and_saved_once()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var a = new Genealogy { Name = "甲谱" }; var b = new Genealogy { Name = "乙谱" };
        var person = new Person { GenealogyId = a.Id, Name = "原名" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.AddRange(a, b); seed.People.Add(person); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ReadAsync(b.Id, person.Id));
        var draft = new WorkbenchPersonDraft(await command.ReadAsync(a.Id, person.Id));
        draft.Values = draft.Values with { Name = "  " };
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command));
        Assert.True(draft.IsDirty); Assert.False(draft.IsSaved);
        draft.Values = draft.Values with { Name = " 新名 ", Biography = "虚构资料" };
        await draft.SaveAsync(command); Assert.True(draft.IsSaved); Assert.False(draft.IsDirty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.SaveAsync(command));
        await using var verify = db.CreateContext(); var saved = await verify.People.SingleAsync();
        Assert.Equal("新名", saved.Name); Assert.Equal("虚构资料", saved.Biography);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Aba_change_and_database_failure_preserve_draft_and_never_partially_write()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var genealogy = new Genealogy { Name = "虚构谱" }; var person = new Person { GenealogyId = genealogy.Id, Name = "原名" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(genealogy); seed.People.Add(person); await seed.SaveChangesAsync(); }
        await using var context = db.CreateContext(); var command = new WorkbenchPersonProfileCommand(context);
        var stale = new WorkbenchPersonDraft(await command.ReadAsync(genealogy.Id, person.Id));
        stale.Values = stale.Values with { Name = "草稿", Biography = "草稿资料" };
        await using (var other = db.CreateContext())
        {
            await other.People.Where(p => p.Id == person.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "临时"));
            await other.People.Where(p => p.Id == person.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "原名"));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.SaveAsync(command));
        Assert.True(stale.IsDirty); Assert.False(stale.IsSaved);
        var failing = new WorkbenchPersonDraft(await command.ReadAsync(genealogy.Id, person.Id));
        failing.Values = stale.Values;
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectProfile BEFORE UPDATE ON People BEGIN SELECT RAISE(ABORT, 'injected failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => failing.SaveAsync(command));
        Assert.True(failing.IsDirty); Assert.False(failing.IsSaved);
        await using var verify = db.CreateContext(); var saved = await verify.People.SingleAsync();
        Assert.Equal("原名", saved.Name); Assert.Null(saved.Biography);
    }
}
