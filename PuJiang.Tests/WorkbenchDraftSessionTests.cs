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
    public async Task Chart_spouse_draft_respects_cancel_and_saves_order_with_person()
    {
        await using var db = new TestDatabase(); await db.MigrateAsync();
        var g = new Genealogy { Name = "虚构谱" }; var owner = new Person { GenealogyId = g.Id, Name = "人物", Sex = PersonSex.Male, RecordedGeneration = "1" };
        Guid chart;
        await using (var setup = db.CreateContext())
        {
            setup.Genealogies.Add(g); setup.People.Add(owner); await setup.SaveChangesAsync();
            var graph = await new GenealogyDataService(setup).GetGraphSnapshotAsync(g.Id);
            chart = (await new LineageChartService(setup).SaveAsync(g.Id, null, "图", null, graph, new Dictionary<Guid, Guid>())).Id;
        }
        var session = WorkbenchDraftSession.Create(Coordinator(db));
        await session.SelectAsync(g.Id, owner.Id, DraftLeaveChoice.Cancel);
        session.Draft!.Values = session.Draft.Values with { Name = "未保存" };
        Assert.Equal(DraftTransitionResult.Cancelled, await session.BeginSpouseForChartAsync(g.Id, owner.Id, chart, DraftLeaveChoice.Cancel));
        Assert.Equal("未保存", session.Draft!.Values.Name);
        Assert.Equal(DraftTransitionResult.Completed, await session.BeginSpouseForChartAsync(g.Id, owner.Id, chart, DraftLeaveChoice.Discard));
        var spouseId = session.Draft!.Original.PersonId; session.Draft.Values = session.Draft.Values with { Name = "配偶" };
        Assert.Equal(DraftTransitionResult.Completed, await session.SaveAsync());
        await using var verify = db.CreateContext();
        Assert.Equal("人物", (await verify.People.SingleAsync(p => p.Id == owner.Id)).Name);
        var union = await verify.FamilyUnions.SingleAsync(u => u.PartnerOneId == spouseId || u.PartnerTwoId == spouseId);
        var order = await new SpouseOrderStorageService(verify).CaptureAsync(g.Id, chart, owner.Id);
        Assert.False(order.NeedsReview); Assert.Equal(new[] { union.Id }, order.SavedOrder);
    }

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
