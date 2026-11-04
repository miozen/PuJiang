using ZupuPaiBan.Data;
using static ZupuPaiBan.Data.ProjectionReadSession;

namespace ZupuPaiBan.Services;

/// <summary>Builds a staged single ancestry path without mutating the displayed graph.</summary>
public static class AncestryPathReader
{
    public enum Boundary { Connected, MissingParent, ChoiceRequired, ChartNeedsReview, ChoiceConflict, Cycle, LimitReached }
    public sealed record Step(PersonRow Person, ParentCandidate Parent, IReadOnlyList<ParentCandidate> Candidates);
    public sealed record Result(Boundary Status, Guid AtPerson, IReadOnlyList<Step> Steps,
        IReadOnlyList<ParentCandidate> Candidates, ChartChoiceState? ChartChoice);

    public static async Task<Result> ReadAsync(ProjectionReadSession session, Guid token, Guid target,
        IReadOnlySet<Guid> anchors, Guid? chartId, IReadOnlyDictionary<Guid, Guid> selections,
        int maxDepth = 100, int maxCandidates = 500, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(anchors); ArgumentNullException.ThrowIfNull(selections);
        if (target == Guid.Empty || maxDepth < 1 || maxCandidates < 1) throw new ArgumentOutOfRangeException(nameof(target));
        var visited = new HashSet<Guid>(); var steps = new List<Step>(); var current = target; var total = 0;
        Result Stop(Boundary status, IReadOnlyList<ParentCandidate>? candidates = null, ChartChoiceState? choice = null)
            => new(status, current, steps.AsReadOnly(), candidates ?? Array.Empty<ParentCandidate>(), choice);
        while (true)
        {
            if (anchors.Contains(current)) { await session.ValidateAsync(token, cancellationToken); return Stop(Boundary.Connected); }
            if (!visited.Add(current)) return Stop(Boundary.Cycle);
            if (steps.Count >= maxDepth || total >= maxCandidates) return Stop(Boundary.LimitReached);
            var person = await session.ReadPersonAsync(token, current, cancellationToken);
            var parents = new List<ParentCandidate>(); Guid? cursor = null;
            do
            {
                var page = await session.ReadParentCandidatesAsync(token, current, cursor, Math.Min(200, maxCandidates - total), cancellationToken);
                parents.AddRange(page.Items); total += page.Items.Count; cursor = page.NextCursor;
                if (cursor is not null && total >= maxCandidates) return Stop(Boundary.LimitReached, parents.AsReadOnly());
            } while (cursor is not null);
            ChartChoiceState? choice = chartId is Guid chart ? await session.ReadChartChoiceAsync(token, chart, current, cancellationToken) : null;
            if (choice is { NeedsReview: true }) return Stop(Boundary.ChartNeedsReview, parents.AsReadOnly(), choice);
            if (parents.Count == 0) return Stop(Boundary.MissingParent, parents.AsReadOnly());
            var chartSelection = choice?.ApplicableRelationshipId;
            selections.TryGetValue(current, out var explicitSelection);
            if (chartSelection is Guid chosen && explicitSelection != Guid.Empty && explicitSelection != chosen)
                return Stop(Boundary.ChoiceConflict, parents.AsReadOnly(), choice);
            var selectedId = chartSelection ?? (explicitSelection != Guid.Empty ? explicitSelection : parents.Count == 1 ? parents[0].RelationshipId : Guid.Empty);
            if (selectedId == Guid.Empty) return Stop(Boundary.ChoiceRequired, parents.AsReadOnly(), choice);
            var parent = parents.SingleOrDefault(p => p.RelationshipId == selectedId);
            if (parent is null) return Stop(Boundary.ChoiceConflict, parents.AsReadOnly(), choice);
            steps.Add(new Step(person, parent, parents.AsReadOnly())); current = parent.ParentId;
        }
    }
}
