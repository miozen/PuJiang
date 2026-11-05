using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public sealed record SiblingOrderMember(Guid PersonId, string Name, string? RecordedGeneration, string? RecordedBirthOrder,
    Guid RelationshipId, PersonRelationshipKind Kind);
public sealed record SiblingOrderHistoricalItem(int Position, SiblingOrderMember Member);
public sealed record SiblingOrderSummary(Guid GroupId, Guid ParentId, string ParentName, long EvidenceGeneration);
public sealed record SiblingOrderReview(Guid GroupId, Guid GenealogyId, Guid ChartId, Guid ParentId, string ParentName, string? ParentGeneration,
    long ChartRevision, long CurrentGeneration, long EvidenceGeneration, IReadOnlyList<SiblingOrderHistoricalItem> Historical,
    IReadOnlyList<SiblingOrderMember> Current, IReadOnlyList<string> Problems)
{
    // This conclusion belongs only to the captured revision; consumers must revalidate before applying it.
    public bool CanApplyAtCapturedRevision => Problems.Count == 0;
}

public sealed class SiblingOrderCapture
{
    internal SiblingOrderCapture(Guid session, Guid genealogy, Guid chart, long revision, long generation, Guid parent,
        string parentName, string? parentGeneration, IReadOnlyList<SiblingOrderMember> members, Guid? group)
        => (Session, GenealogyId, ChartId, ChartRevision, Generation, ParentId, ParentName, ParentGeneration, Members, GroupId) =
           (session, genealogy, chart, revision, generation, parent, parentName, parentGeneration, members, group);
    internal Guid Session { get; }
    public Guid GenealogyId { get; }
    public Guid ChartId { get; }
    public long ChartRevision { get; }
    public long Generation { get; }
    public Guid ParentId { get; }
    public string ParentName { get; }
    public string? ParentGeneration { get; }
    public IReadOnlyList<SiblingOrderMember> Members { get; }
    public Guid? GroupId { get; }
}

/// <summary>Dedicated-context, version-bound storage session; no UI or production restore behavior.</summary>
public sealed class SiblingOrderStorageSession : IAsyncDisposable
{
    private const int MaxMembers = 2000;
    private readonly TargetContextCoordinator _coordinator;
    private readonly ZupuDbContext context;
    private readonly ProjectionReadSession _reader;
    private readonly Guid _genealogy;
    private readonly Guid _readerToken;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeSync = new();
    private IDisposable? _registration;
    private Task? _disposeTask;
    private int _closed;
    private bool _committed;
    private readonly Guid _session = Guid.NewGuid();
    private readonly HashSet<SiblingOrderCapture> _issued = [];
    private readonly HashSet<SiblingOrderReview> _reviews = new(ReferenceEqualityComparer.Instance);
    internal Func<int, Task>? AfterItemForTest { get; set; }
    private sealed record GroupEvidence(Guid ParentId, string ParentName, string? ParentGeneration, IReadOnlyList<SiblingOrderMember> Members);

    private SiblingOrderStorageSession(TargetContextCoordinator coordinator, ZupuDbContext context, ProjectionReadSession reader, Guid genealogy)
        => (_coordinator, this.context, _reader, _genealogy, _readerToken) = (coordinator, context, reader, genealogy, reader.Token);

    public static async Task<SiblingOrderStorageSession> OpenAsync(TargetContextCoordinator coordinator, Guid genealogyId, CancellationToken cancellationToken = default)
    {
        using var operation = coordinator.EnterOperation();
        var context = coordinator.CreateContext();
        try
        {
            var reader = await ProjectionReadSession.OpenAsync(coordinator, genealogyId, cancellationToken);
            var session = new SiblingOrderStorageSession(coordinator, context, reader, genealogyId);
            try { session._registration = coordinator.RegisterLongLivedCloser(() => session.DisposeAsync().AsTask()); return session; }
            catch { await reader.DisposeAsync(); throw; }
        }
        catch { await context.DisposeAsync(); throw; }
    }

    private async Task ValidateSessionAsync(Guid genealogy, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _closed) != 0 || _committed || genealogy != _genealogy) throw new InvalidOperationException("排序会话已失效或族谱不匹配，请重新打开。");
        await _reader.ValidateAsync(_readerToken, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _closed, 1); await _gate.WaitAsync();
        try
        {
            _committed = true; _issued.Clear(); _reviews.Clear();
            try { await _reader.DisposeAsync(); } finally { try { await context.DisposeAsync(); } finally { _registration?.Dispose(); } }
        }
        finally { _gate.Release(); }
    }

    private async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _closed) != 0) throw new InvalidOperationException("排序会话已关闭。");
        await _gate.WaitAsync(cancellationToken);
        try { if (Volatile.Read(ref _closed) != 0) throw new InvalidOperationException("排序会话已关闭。"); return await action(); }
        finally { _gate.Release(); }
    }

    public Task<IReadOnlyList<SiblingOrderSummary>> ListAsync(Guid genealogyId, Guid chartId, CancellationToken cancellationToken = default) =>
        RunAsync(() => ListCoreAsync(genealogyId, chartId, cancellationToken), cancellationToken);
    private async Task<IReadOnlyList<SiblingOrderSummary>> ListCoreAsync(Guid genealogyId, Guid chartId, CancellationToken cancellationToken)
    {
        using var operation = _coordinator.EnterOperation();
        await ValidateSessionAsync(genealogyId, cancellationToken);
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        await GuardAsync(cancellationToken);
        if (!await context.LineageCharts.AsNoTracking().AnyAsync(c => c.Id == chartId && c.GenealogyId == genealogyId, cancellationToken))
            throw new InvalidOperationException("图表不属于当前族谱。");
        var groups = await context.LineageSiblingOrders.AsNoTracking().Where(g => g.ChartId == chartId).OrderBy(g => g.Id)
            .Select(g => new SiblingOrderSummary(g.Id, g.ParentId, g.ParentName, g.EvidenceGeneration)).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await ValidateSessionAsync(genealogyId, cancellationToken);
        return groups.AsReadOnly();
    }

    public Task<SiblingOrderCapture> CaptureAsync(Guid genealogyId, Guid chartId, Guid parentId, Guid? existingGroupId = null, CancellationToken cancellationToken = default) =>
        RunAsync(() => CaptureCoreAsync(genealogyId, chartId, parentId, existingGroupId, cancellationToken), cancellationToken);
    private async Task<SiblingOrderCapture> CaptureCoreAsync(Guid genealogyId, Guid chartId, Guid parentId, Guid? existingGroupId, CancellationToken cancellationToken)
    {
        using var operation = _coordinator.EnterOperation();
        await ValidateSessionAsync(genealogyId, cancellationToken);
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await GuardAsync(cancellationToken);
        var revision = await context.LineageCharts.AsNoTracking().Where(c => c.Id == chartId && c.GenealogyId == genealogyId)
            .Select(c => (long?)c.Revision).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("图表不属于当前族谱。");
        if (existingGroupId is Guid groupId && !await context.LineageSiblingOrders.AsNoTracking().AnyAsync(g => g.Id == groupId && g.ChartId == chartId && g.ParentId == parentId, cancellationToken))
            throw new InvalidOperationException("历史排序组与图表或父母不匹配。");
        if (existingGroupId is null && await context.LineageSiblingOrders.AsNoTracking().AnyAsync(g => g.ChartId == chartId && g.ParentId == parentId, cancellationToken))
            throw new InvalidOperationException("该父母的历史排序已存在，请先复核。");
        var evidence = await ReadEvidenceAsync(genealogyId, parentId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await ValidateSessionAsync(genealogyId, cancellationToken);
        var capture = new SiblingOrderCapture(_session, genealogyId, chartId, revision, generation, parentId,
            evidence.ParentName, evidence.ParentGeneration, Array.AsReadOnly(evidence.Members.ToArray()), existingGroupId);
        _issued.Add(capture); return capture;
    }

    public Task<Guid> SaveAsync(SiblingOrderCapture capture, IReadOnlyList<Guid> orderedPeople, bool confirmChangedHistory = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedPeople);
        var order = orderedPeople.ToArray();
        return RunAsync(() => SaveCoreAsync(capture, order, confirmChangedHistory, cancellationToken), cancellationToken);
    }
    private async Task<Guid> SaveCoreAsync(SiblingOrderCapture capture, IReadOnlyList<Guid> orderedPeople, bool confirmChangedHistory, CancellationToken cancellationToken)
    {
        using var operation = _coordinator.EnterOperation();
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(orderedPeople);
        if (capture.Session != _session || !_issued.Contains(capture)) throw new InvalidOperationException("排序凭据不属于当前会话或已使用。");
        await ValidateSessionAsync(capture.GenealogyId, cancellationToken);
        if (orderedPeople.Count != capture.Members.Count || orderedPeople.Distinct().Count() != orderedPeople.Count ||
            !orderedPeople.ToHashSet().SetEquals(capture.Members.Select(m => m.PersonId)))
            throw new InvalidOperationException("必须完整指定且不能重复人物。");
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        var cas = $"UPDATE LineageCharts SET Revision=Revision+1 WHERE Id=@chart AND GenealogyId=@genealogy AND Revision=@revision AND Revision<9223372036854775807 AND EXISTS (SELECT 1 FROM ({UnlinkGenerationV1.GuardSql}) g WHERE g.Value=@generation)";
        var count = await context.Database.ExecuteSqlRawAsync(cas, new object[] { Sql("@chart", capture.ChartId), Sql("@genealogy", capture.GenealogyId),
            new SqliteParameter("@revision", capture.ChartRevision), new SqliteParameter("@generation", capture.Generation) }, cancellationToken);
        if (count != 1) throw new InvalidOperationException("图表或谱系数据已变化，请重新读取并复核。");
        var evidence = await ReadEvidenceAsync(capture.GenealogyId, capture.ParentId, cancellationToken);
        if (!SameEvidence(capture.ParentGeneration, capture.Members, evidence)) throw new InvalidOperationException("同胞组已变化，请重新复核。");
        var existing = await context.LineageSiblingOrders.AsNoTracking().Include(g => g.Items)
            .SingleOrDefaultAsync(g => g.ChartId == capture.ChartId && g.ParentId == capture.ParentId, cancellationToken);
        if ((existing is null) != (capture.GroupId is null) || (existing is not null && existing.Id != capture.GroupId))
            throw new InvalidOperationException("排序组已变化，请重新读取。");
        if (existing is not null && (existing.EvidenceGeneration != capture.Generation || !SameEvidence(existing.ParentGeneration, ToMembers(existing), evidence)))
        {
            var reviewed = _reviews.Any(r => r.GroupId == existing.Id && r.ChartId == capture.ChartId && r.GenealogyId == capture.GenealogyId &&
                r.ChartRevision == capture.ChartRevision && r.CurrentGeneration == capture.Generation &&
                r.EvidenceGeneration == existing.EvidenceGeneration && r.ParentGeneration == existing.ParentGeneration &&
                existing.Items.OrderBy(i => i.Position).Select(i => new SiblingOrderHistoricalItem(i.Position, ToMember(i))).SequenceEqual(r.Historical) &&
                r.Current.Select(m => m.PersonId).ToHashSet().SetEquals(evidence.Members.Select(m => m.PersonId)));
            if (!confirmChangedHistory || !reviewed) throw new InvalidOperationException("历史排序需要在当前会话明确复核后才能替换。");
        }
        var id = existing?.Id ?? Guid.NewGuid();
        if (existing is null)
            await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageSiblingOrders (Id,ChartId,ParentId,ParentName,ParentGeneration,EvidenceGeneration) VALUES (@id,@chart,@parent,@name,@recorded,@generation)",
                new object[] { Sql("@id", id), Sql("@chart", capture.ChartId), Sql("@parent", capture.ParentId), new SqliteParameter("@name", evidence.ParentName),
                    new SqliteParameter("@recorded", (object?)evidence.ParentGeneration ?? DBNull.Value), new SqliteParameter("@generation", capture.Generation) }, cancellationToken);
        else
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE LineageSiblingOrders SET ParentName=@name,ParentGeneration=@recorded,EvidenceGeneration=@generation WHERE Id=@id AND ChartId=@chart",
                new object[] { new SqliteParameter("@name", evidence.ParentName), new SqliteParameter("@recorded", (object?)evidence.ParentGeneration ?? DBNull.Value),
                    new SqliteParameter("@generation", capture.Generation), Sql("@id", id), Sql("@chart", capture.ChartId) }, cancellationToken);
            await context.Database.ExecuteSqlRawAsync("DELETE FROM LineageSiblingOrderItems WHERE GroupId=@id", new object[] { Sql("@id", id) }, cancellationToken);
        }
        var members = evidence.Members.ToDictionary(m => m.PersonId);
        for (var i = 0; i < orderedPeople.Count; i++)
        {
            var m = members[orderedPeople[i]];
            await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageSiblingOrderItems (GroupId,PersonId,Position,RelationshipId,Kind,PersonName,RecordedGeneration,RecordedBirthOrder) VALUES (@group,@person,@position,@relation,@kind,@name,@generation,@birthorder)",
                new object[] { Sql("@group", id), Sql("@person", m.PersonId), new SqliteParameter("@position", i + 1), Sql("@relation", m.RelationshipId),
                    new SqliteParameter("@kind", (int)m.Kind), new SqliteParameter("@name", m.Name), new SqliteParameter("@generation", (object?)m.RecordedGeneration ?? DBNull.Value),
                    new SqliteParameter("@birthorder", (object?)m.RecordedBirthOrder ?? DBNull.Value) }, cancellationToken);
            if (AfterItemForTest is not null) await AfterItemForTest(i + 1);
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Once commit starts, do not report cancellation for a transaction that may have committed.
        await tx.CommitAsync(CancellationToken.None); _committed = true; _issued.Clear(); _reviews.Clear(); return id;
    }

    public Task<SiblingOrderReview> ReviewAsync(Guid genealogyId, Guid chartId, Guid groupId, CancellationToken cancellationToken = default) =>
        RunAsync(() => ReviewCoreAsync(genealogyId, chartId, groupId, cancellationToken), cancellationToken);
    private async Task<SiblingOrderReview> ReviewCoreAsync(Guid genealogyId, Guid chartId, Guid groupId, CancellationToken cancellationToken)
    {
        using var operation = _coordinator.EnterOperation();
        await ValidateSessionAsync(genealogyId, cancellationToken);
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await GuardAsync(cancellationToken);
        var revision = await context.LineageCharts.AsNoTracking().Where(c => c.Id == chartId && c.GenealogyId == genealogyId)
            .Select(c => (long?)c.Revision).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("图表不属于当前族谱。");
        var saved = await context.LineageSiblingOrders.AsNoTracking().Include(g => g.Items)
            .SingleOrDefaultAsync(g => g.Id == groupId && g.ChartId == chartId, cancellationToken) ?? throw new InvalidOperationException("排序组不存在。");
        var historical = saved.Items.OrderBy(i => i.Position).Select(i => new SiblingOrderHistoricalItem(i.Position, ToMember(i))).ToArray();
        var problems = new List<string>(); IReadOnlyList<SiblingOrderMember> current = Array.Empty<SiblingOrderMember>();
        if (saved.EvidenceGeneration != generation) problems.Add("数据版本已变化，须复核。");
        if (!historical.Select(i => i.Position).SequenceEqual(Enumerable.Range(1, historical.Length)) || historical.Length < 2)
            problems.Add("历史顺序不完整或位置无效。");
        try
        {
            var evidence = await ReadEvidenceAsync(genealogyId, saved.ParentId, cancellationToken);
            current = evidence.Members;
            if (saved.ParentGeneration != evidence.ParentGeneration) problems.Add($"父母谱载世代已变化：{saved.ParentGeneration ?? "未记"} → {evidence.ParentGeneration ?? "未记"}。");
            var oldMembers = ToMembers(saved).ToDictionary(m => m.PersonId);
            var newMembers = evidence.Members.ToDictionary(m => m.PersonId);
            foreach (var old in oldMembers.Values.Where(m => !newMembers.ContainsKey(m.PersonId))) problems.Add($"原成员已不在当前组：{old.Name}（{old.PersonId}）。");
            foreach (var added in newMembers.Values.Where(m => !oldMembers.ContainsKey(m.PersonId))) problems.Add($"当前组新增成员：{added.Name}（{added.PersonId}）。");
            foreach (var old in oldMembers.Values.Where(m => newMembers.ContainsKey(m.PersonId)))
            {
                var now = newMembers[old.PersonId];
                if (old.RelationshipId != now.RelationshipId || old.Kind != now.Kind) problems.Add($"{old.Name}（{old.PersonId}）的父母关系或类型已变化。");
                if (old.RecordedGeneration != now.RecordedGeneration) problems.Add($"{old.Name}（{old.PersonId}）的谱载世代已变化。");
                if (old.RecordedBirthOrder != now.RecordedBirthOrder) problems.Add($"{old.Name}（{old.PersonId}）的谱载排行原文已变化。");
            }
        }
        catch (InvalidOperationException ex) { problems.Add(ex.Message); }
        await tx.CommitAsync(cancellationToken);
        await ValidateSessionAsync(genealogyId, cancellationToken);
        var review = new SiblingOrderReview(groupId, genealogyId, chartId, saved.ParentId, saved.ParentName, saved.ParentGeneration, revision, generation, saved.EvidenceGeneration,
            Array.AsReadOnly(historical), Array.AsReadOnly(current.ToArray()), problems.AsReadOnly());
        _reviews.Add(review); return review;
    }

    public async Task ValidateReviewAsync(SiblingOrderReview review, CancellationToken cancellationToken = default) =>
        await RunAsync(async () => { await ValidateReviewCoreAsync(review, cancellationToken); return true; }, cancellationToken);
    private async Task ValidateReviewCoreAsync(SiblingOrderReview review, CancellationToken cancellationToken)
    {
        using var operation = _coordinator.EnterOperation();
        ArgumentNullException.ThrowIfNull(review);
        if (!_reviews.Contains(review) || !review.CanApplyAtCapturedRevision) throw new InvalidOperationException("排序结果未通过复核或不属于当前会话。");
        await ValidateSessionAsync(review.GenealogyId, cancellationToken);
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await GuardAsync(cancellationToken) != review.CurrentGeneration || !await context.LineageCharts.AsNoTracking().AnyAsync(c =>
            c.Id == review.ChartId && c.GenealogyId == review.GenealogyId && c.Revision == review.ChartRevision, cancellationToken))
            throw new InvalidOperationException("排序结果已失效，请重新读取。");
        var group = await context.LineageSiblingOrders.AsNoTracking().Include(g => g.Items).SingleOrDefaultAsync(g => g.Id == review.GroupId && g.ChartId == review.ChartId, cancellationToken);
        if (group is null || group.ParentId != review.ParentId || group.EvidenceGeneration != review.EvidenceGeneration ||
            group.ParentGeneration != review.ParentGeneration || !group.Items.OrderBy(i => i.Position).Select(i => new SiblingOrderHistoricalItem(i.Position, ToMember(i))).SequenceEqual(review.Historical))
            throw new InvalidOperationException("排序记录已变化，请重新读取。");
        var evidence = await ReadEvidenceAsync(review.GenealogyId, review.ParentId, cancellationToken);
        if (!SameEvidence(review.ParentGeneration, review.Current, evidence)) throw new InvalidOperationException("同胞组已变化，请重新读取。");
        await tx.CommitAsync(cancellationToken);
        await ValidateSessionAsync(review.GenealogyId, cancellationToken);
    }

    private async Task<GroupEvidence> ReadEvidenceAsync(Guid genealogyId, Guid parentId, CancellationToken cancellationToken)
    {
        if (parentId == Guid.Empty) throw new InvalidOperationException("父母人物 ID 无效。");
        var parent = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parentId && p.GenealogyId == genealogyId, cancellationToken)
            ?? throw new InvalidOperationException("父母人物不存在或不属于当前族谱。");
        if (!RecordedGenerationParser.TryParse(parent.RecordedGeneration, out var parentGeneration)) throw new InvalidOperationException("父母世代未识别。");
        var links = await context.PersonRelationships.AsNoTracking().Where(r => r.GenealogyId == genealogyId && r.RelatedPersonId == parentId &&
            (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent))
            .OrderBy(r => r.Id).Take(MaxMembers + 1).ToListAsync(cancellationToken);
        if (links.Count < 2 || links.Count > MaxMembers || links.Any(r => r.Id == Guid.Empty || r.PersonId == Guid.Empty || r.PersonId == parentId) || links.Select(r => r.PersonId).Distinct().Count() != links.Count)
            throw new InvalidOperationException("子女组不完整、人数超限或关系有歧义。");
        var ids = links.Select(r => r.PersonId).ToArray();
        var people = await context.People.AsNoTracking().Where(p => p.GenealogyId == genealogyId && ids.Contains(p.Id)).ToListAsync(cancellationToken);
        if (people.Count != links.Count) throw new InvalidOperationException("子女人物缺失或跨谱。");
        var parentCounts = await context.PersonRelationships.AsNoTracking().Where(r => r.GenealogyId == genealogyId && ids.Contains(r.PersonId) &&
            (r.Kind == PersonRelationshipKind.BiologicalParent || r.Kind == PersonRelationshipKind.AdoptiveParent || r.Kind == PersonRelationshipKind.HeirParent))
            .GroupBy(r => r.PersonId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, cancellationToken);
        if (ids.Any(id => !parentCounts.TryGetValue(id, out var count) || count != 1)) throw new InvalidOperationException("子女父母候选不唯一，须先处理。");
        var byId = people.ToDictionary(p => p.Id);
        var members = new List<SiblingOrderMember>();
        foreach (var link in links)
        {
            var p = byId[link.PersonId];
            if (!RecordedGenerationParser.TryParse(p.RecordedGeneration, out var generation) || generation <= parentGeneration)
                throw new InvalidOperationException("子女世代未识别或与父母冲突。");
            members.Add(new(p.Id, p.Name, p.RecordedGeneration, p.RecordedBirthOrder, link.Id, link.Kind));
        }
        return new(parent.Id, parent.Name, parent.RecordedGeneration, members.AsReadOnly());
    }
    private static bool SameEvidence(string? parentGeneration, IReadOnlyList<SiblingOrderMember> saved, GroupEvidence current) =>
        parentGeneration == current.ParentGeneration && saved.Count == current.Members.Count &&
        saved.Select(m => (m.PersonId, m.RelationshipId, m.Kind, m.RecordedGeneration, m.RecordedBirthOrder)).OrderBy(x => x.PersonId)
            .SequenceEqual(current.Members.Select(m => (m.PersonId, m.RelationshipId, m.Kind, m.RecordedGeneration, m.RecordedBirthOrder)).OrderBy(x => x.PersonId));
    private static SiblingOrderMember ToMember(LineageSiblingOrderItem i) => new(i.PersonId, i.PersonName, i.RecordedGeneration, i.RecordedBirthOrder, i.RelationshipId, i.Kind);
    private static IReadOnlyList<SiblingOrderMember> ToMembers(LineageSiblingOrder group) => group.Items.Select(ToMember).ToArray();
    private Task<long> GuardAsync(CancellationToken cancellationToken) => context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
    private static SqliteParameter Sql(string name, Guid value) => new(name, value.ToString().ToUpperInvariant());
}
