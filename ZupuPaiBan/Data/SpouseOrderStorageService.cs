using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public sealed record SpouseOrderEvidence(Guid UnionId, Guid PartnerOneId, Guid? PartnerTwoId, FamilyUnionKind Kind);

public sealed class SpouseOrderCapture
{
    internal SpouseOrderCapture(Guid genealogyId, Guid chartId, Guid ownerId, long generation, long revision,
        Guid? groupId, long? historyGeneration, IReadOnlyList<SpouseOrderEvidence> evidence,
        IReadOnlyList<SpouseOrderEvidence> historical, IReadOnlyList<Guid> savedOrder, bool needsReview)
        => (GenealogyId, ChartId, OwnerId, Generation, ChartRevision, GroupId, HistoryGeneration, Evidence, Historical, SavedOrder, NeedsReview) =
            (genealogyId, chartId, ownerId, generation, revision, groupId, historyGeneration, evidence, historical, savedOrder, needsReview);
    public Guid GenealogyId { get; }
    public Guid ChartId { get; }
    public Guid OwnerId { get; }
    public long Generation { get; }
    public long ChartRevision { get; }
    public Guid? GroupId { get; }
    public long? HistoryGeneration { get; }
    public IReadOnlyList<SpouseOrderEvidence> Evidence { get; }
    public IReadOnlyList<SpouseOrderEvidence> Historical { get; }
    public IReadOnlyList<Guid> SavedOrder { get; }
    public bool NeedsReview { get; }
}

/// <summary>Version-guarded per-chart spouse order; the caller owns the context and operation lifetime.</summary>
public sealed class SpouseOrderStorageService(ZupuDbContext context)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<SpouseOrderCapture> _issued = new(ReferenceEqualityComparer.Instance);
    internal Func<int, Task>? AfterItemForTest { get; set; }

    public async Task<SpouseOrderCapture> CaptureAsync(Guid genealogyId, Guid chartId, Guid ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
            var generation = await GuardAsync(cancellationToken);
            var revision = await context.LineageCharts.AsNoTracking().Where(c => c.Id == chartId && c.GenealogyId == genealogyId)
                .Select(c => (long?)c.Revision).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("图表不属于当前族谱。");
            var evidence = await ReadEvidenceAsync(genealogyId, ownerId, cancellationToken);
            var existing = await context.LineageSpouseOrders.AsNoTracking().Include(g => g.Items)
                .SingleOrDefaultAsync(g => g.ChartId == chartId && g.OwnerId == ownerId, cancellationToken);
            var items = existing?.Items.OrderBy(i => i.Position).ToArray() ?? [];
            if (existing is not null && !items.Select(i => i.Position).SequenceEqual(Enumerable.Range(1, items.Length)))
                throw new InvalidOperationException("历史配偶顺序不完整，请先核实。");
            var historical = items.Select(ToEvidence).ToArray();
            var needsReview = existing is not null && (existing.EvidenceGeneration != generation || !SameEvidence(evidence, historical));
            await tx.CommitAsync(cancellationToken);
            var capture = new SpouseOrderCapture(genealogyId, chartId, ownerId, generation, revision, existing?.Id, existing?.EvidenceGeneration,
                Array.AsReadOnly(evidence.ToArray()), Array.AsReadOnly(historical), Array.AsReadOnly(items.Select(i => i.UnionId).ToArray()), needsReview);
            _issued.Add(capture); return capture;
        }
        finally { _gate.Release(); }
    }

    public async Task<Guid> SaveAsync(SpouseOrderCapture capture, IReadOnlyList<Guid> orderedUnions, CancellationToken cancellationToken = default, bool confirmChangedHistory = false)
    {
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(orderedUnions);
        var order = orderedUnions.ToArray();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_issued.Contains(capture)) throw new InvalidOperationException("排序凭据不属于当前会话或已使用。");
            if (capture.NeedsReview && !confirmChangedHistory) throw new InvalidOperationException("历史配偶排序需要明确复核。");
            if (order.Length != capture.Evidence.Count || order.Distinct().Count() != order.Length ||
                !order.ToHashSet().SetEquals(capture.Evidence.Select(e => e.UnionId)))
                throw new InvalidOperationException("必须完整指定且不能重复婚配记录。");
            await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
            var cas = $"UPDATE LineageCharts SET Revision=Revision+1 WHERE Id=@chart AND GenealogyId=@genealogy AND Revision=@revision AND Revision<9223372036854775807 AND EXISTS (SELECT 1 FROM ({UnlinkGenerationV1.GuardSql}) g WHERE g.Value=@generation)";
            var changed = await context.Database.ExecuteSqlRawAsync(cas, new object[] { Sql("@chart", capture.ChartId), Sql("@genealogy", capture.GenealogyId),
                new SqliteParameter("@revision", capture.ChartRevision), new SqliteParameter("@generation", capture.Generation) }, cancellationToken);
            if (changed != 1) throw new InvalidOperationException("图表或谱系数据已变化，请重新读取。");
            var evidence = await ReadEvidenceAsync(capture.GenealogyId, capture.OwnerId, cancellationToken);
            if (!SameEvidence(capture.Evidence, evidence)) throw new InvalidOperationException("婚配记录已变化，请重新读取。");
            var existing = await context.LineageSpouseOrders.AsNoTracking().Include(g => g.Items)
                .SingleOrDefaultAsync(g => g.ChartId == capture.ChartId && g.OwnerId == capture.OwnerId, cancellationToken);
            if ((existing is null) != (capture.GroupId is null) || (existing is not null && (existing.Id != capture.GroupId ||
                existing.EvidenceGeneration != capture.HistoryGeneration || !existing.Items.OrderBy(i => i.Position).Select(i => i.UnionId).SequenceEqual(capture.SavedOrder) ||
                !SameEvidence(capture.Historical, existing.Items.Select(ToEvidence)))))
                throw new InvalidOperationException("配偶排序已变化，请重新读取。");
            var id = existing?.Id ?? Guid.NewGuid();
            if (existing is null)
                await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageSpouseOrders (Id,ChartId,OwnerId,EvidenceGeneration) VALUES (@id,@chart,@owner,@generation)",
                    new object[] { Sql("@id", id), Sql("@chart", capture.ChartId), Sql("@owner", capture.OwnerId), new SqliteParameter("@generation", capture.Generation) }, cancellationToken);
            else
            {
                await context.Database.ExecuteSqlRawAsync("UPDATE LineageSpouseOrders SET EvidenceGeneration=@generation WHERE Id=@id AND ChartId=@chart",
                    new object[] { new SqliteParameter("@generation", capture.Generation), Sql("@id", id), Sql("@chart", capture.ChartId) }, cancellationToken);
                await context.Database.ExecuteSqlRawAsync("DELETE FROM LineageSpouseOrderItems WHERE GroupId=@id", new object[] { Sql("@id", id) }, cancellationToken);
            }
            var byId = evidence.ToDictionary(e => e.UnionId);
            for (var i = 0; i < order.Length; i++)
            {
                var item = byId[order[i]];
                await context.Database.ExecuteSqlRawAsync("INSERT INTO LineageSpouseOrderItems (GroupId,UnionId,PartnerOneId,PartnerTwoId,Kind,Position) VALUES (@group,@union,@one,@two,@kind,@position)",
                    new object[] { Sql("@group", id), Sql("@union", item.UnionId), Sql("@one", item.PartnerOneId),
                        new SqliteParameter("@two", item.PartnerTwoId is Guid two ? two.ToString().ToUpperInvariant() : DBNull.Value),
                        new SqliteParameter("@kind", (int)item.Kind), new SqliteParameter("@position", i + 1) }, cancellationToken);
                if (AfterItemForTest is not null) await AfterItemForTest(i + 1);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await tx.CommitAsync(CancellationToken.None); _issued.Clear(); return id;
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<SpouseOrderEvidence>> ReadEvidenceAsync(Guid genealogyId, Guid ownerId, CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty || !await context.People.AsNoTracking().AnyAsync(p => p.Id == ownerId && p.GenealogyId == genealogyId, cancellationToken))
            throw new InvalidOperationException("人物不存在或不属于当前族谱。");
        var rows = await context.FamilyUnions.AsNoTracking().Where(u => u.GenealogyId == genealogyId && (u.PartnerOneId == ownerId || u.PartnerTwoId == ownerId))
            .Select(u => new SpouseOrderEvidence(u.Id, u.PartnerOneId, u.PartnerTwoId, u.Kind)).Take(2001).ToListAsync(cancellationToken);
        if (rows.Count > 2000 || rows.Any(r => r.UnionId == Guid.Empty)) throw new InvalidOperationException("配偶记录不完整或超出上限。");
        return rows.AsReadOnly();
    }
    private static bool SameEvidence(IEnumerable<SpouseOrderEvidence> left, IEnumerable<SpouseOrderEvidence> right) =>
        left.OrderBy(e => e.UnionId).SequenceEqual(right.OrderBy(e => e.UnionId));
    private static SpouseOrderEvidence ToEvidence(LineageSpouseOrderItem item) => new(item.UnionId, item.PartnerOneId, item.PartnerTwoId, item.Kind);
    private Task<long> GuardAsync(CancellationToken cancellationToken) => context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
    private static SqliteParameter Sql(string name, Guid value) => new(name, value.ToString().ToUpperInvariant());
}
