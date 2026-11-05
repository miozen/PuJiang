using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.Services;

namespace PuJiang.Tests;

public class WorkbenchDraftSessionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Switch_and_close_choices_preserve_or_commit_scoped_draft()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var a = new Genealogy { Name = "甲" }; var b = new Genealogy { Name = "乙" };
        var first = new Person { GenealogyId = a.Id, Name = "甲人" }; var second = new Person { GenealogyId = b.Id, Name = "乙人" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.AddRange(a, b); seed.People.AddRange(first, second); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(db); var session = WorkbenchDraftSession.Create(coordinator);
        Assert.Equal(DraftTransitionResult.Completed, await session.SelectAsync(a.Id, first.Id, DraftLeaveChoice.Cancel));
        session.Draft!.Values = session.Draft.Values with { Name = "草稿" };
        Assert.Equal(DraftTransitionResult.Cancelled, await session.SelectAsync(b.Id, second.Id, DraftLeaveChoice.Cancel));
        Assert.Equal(first.Id, session.Draft!.Original.PersonId); Assert.Equal("草稿", session.Draft.Values.Name);
        Assert.Equal(DraftTransitionResult.Cancelled, await session.LeaveAsync(DraftLeaveChoice.Cancel));
        session.Draft.Values = session.Draft.Values with { Name = " " };
        Assert.Equal(DraftTransitionResult.SaveFailed, await session.SelectAsync(b.Id, second.Id, DraftLeaveChoice.Save));
        Assert.Equal(first.Id, session.Draft.Original.PersonId); Assert.True(session.Draft.IsDirty);
        session.Draft.Values = session.Draft.Values with { Name = "草稿" };
        Assert.Equal(DraftTransitionResult.Completed, await session.SelectAsync(b.Id, second.Id, DraftLeaveChoice.Save));
        Assert.Equal(second.Id, session.Draft!.Original.PersonId);
        session.Draft.Values = session.Draft.Values with { Name = "舍弃" };
        Assert.Equal(DraftTransitionResult.Completed, await session.LeaveAsync(DraftLeaveChoice.Discard));
        Assert.Null(session.Draft);
        await using var verify = db.CreateContext();
        Assert.Equal("草稿", (await verify.People.SingleAsync(p => p.Id == first.Id)).Name);
        Assert.Equal("乙人", (await verify.People.SingleAsync(p => p.Id == second.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Committed_refresh_failure_does_not_retry_and_drain_waits_for_operation()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var genealogy = new Genealogy { Name = "谱" }; var person = new Person { GenealogyId = genealogy.Id, Name = "原名" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(genealogy); seed.People.Add(person); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(db); var failRefresh = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new WorkbenchDraftSession(coordinator.EnterOperation,
            async (g, p) =>
            {
                if (failRefresh) { entered.SetResult(); await release.Task; throw new InvalidOperationException("注入刷新失败"); }
                await using var context = coordinator.CreateContext(); return await new WorkbenchPersonProfileCommand(context).ReadAsync(g, p);
            },
            async draft => { await using var context = coordinator.CreateContext(); await draft.SaveAsync(new WorkbenchPersonProfileCommand(context)); });
        await session.SelectAsync(genealogy.Id, person.Id, DraftLeaveChoice.Cancel);
        session.Draft!.Values = session.Draft.Values with { Name = "已保存" }; failRefresh = true;
        var saving = session.SaveAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var drain = coordinator.DrainAsync(); Assert.False(drain.IsCompleted);
        release.SetResult();
        Assert.Equal(DraftTransitionResult.SavedRefreshFailed, await saving); Assert.Null(session.Draft);
        Assert.Equal("注入刷新失败", session.Error); await drain;
        await using var verify = db.CreateContext(); Assert.Equal("已保存", (await verify.People.SingleAsync()).Name);
    }

    private static TargetContextCoordinator Coordinator(TestDatabase db) => new(new DbContextOptionsBuilder<ZupuDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()).Options);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Switching_after_commit_reports_target_read_failure_as_already_saved()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var genealogy = new Genealogy { Name = "谱" }; var a = new Person { GenealogyId = genealogy.Id, Name = "甲" }; var b = new Person { GenealogyId = genealogy.Id, Name = "乙" };
        await using (var seed = db.CreateContext()) { seed.Genealogies.Add(genealogy); seed.People.AddRange(a, b); await seed.SaveChangesAsync(); }
        var coordinator = Coordinator(db);
        var session = new WorkbenchDraftSession(coordinator.EnterOperation,
            async (g, p) => { if (p == b.Id) throw new InvalidOperationException("目标读取失败"); await using var context = coordinator.CreateContext(); return await new WorkbenchPersonProfileCommand(context).ReadAsync(g, p); },
            async draft => { await using var context = coordinator.CreateContext(); await draft.SaveAsync(new WorkbenchPersonProfileCommand(context)); });
        await session.SelectAsync(genealogy.Id, a.Id, DraftLeaveChoice.Cancel);
        session.Draft!.Values = session.Draft.Values with { Name = "已保存" };
        Assert.Equal(DraftTransitionResult.SavedThenReadFailed, await session.SelectAsync(genealogy.Id, b.Id, DraftLeaveChoice.Save));
        Assert.Null(session.Draft);
        await using var verify = db.CreateContext(); Assert.Equal("已保存", (await verify.People.SingleAsync(p => p.Id == a.Id)).Name);
    }
}
