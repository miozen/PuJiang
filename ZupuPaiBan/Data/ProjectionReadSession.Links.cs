using Microsoft.Data.Sqlite;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public sealed partial class ProjectionReadSession
{
    public sealed record ParentCandidate(Guid RelationshipId, Guid ChildId, Guid ParentId, string ParentName,
        PersonRelationshipKind Kind, bool Preferred, string? Note, string? Source);
    public sealed record ChildCandidate(Guid RelationshipId, Guid ChildId, string ChildName, string? RecordedGeneration,
        string? RecordedBirthOrder, PersonRelationshipKind Kind);
    public sealed record UnionRow(Guid Id, Guid PartnerOneId, Guid? PartnerTwoId, FamilyUnionKind Kind,
        string? Start, string? End, string? Note);
    public sealed record OtherRelationshipRow(Guid Id, Guid PersonId, Guid? RelatedPersonId,
        PersonRelationshipKind Kind, string? Note, string? Source);
    public sealed record SiblingRow(Guid RelationshipId, Guid PersonId, string Name);
    public sealed record LinkPage<T>(IReadOnlyList<T> Items, Guid? NextCursor);
    public sealed record ChartRow(Guid Id, string Name, long Revision, long Generation);
    public sealed record ChartChoiceState(Guid ChartId, Guid PersonId, Guid ParentId, Guid RelationshipId,
        PersonRelationshipKind Kind, string PersonName, string ParentName, bool NeedsReview, string? Problem)
    {
        public Guid? ApplicableRelationshipId => NeedsReview ? null : RelationshipId;
    }

    public async Task ValidateAsync(Guid token, CancellationToken cancellationToken = default)
    {
        await ReadGuardedAsync(token, (_, _) => Task.FromResult(true), cancellationToken);
    }

    public Task<PersonRow> ReadPersonAsync(Guid token, Guid personId, CancellationToken cancellationToken = default)
    {
        if (personId == Guid.Empty) throw new ArgumentException("人物 ID 无效。", nameof(personId));
        return ReadGuardedAsync(token, async (tx, cancellation) =>
        {
            await using var command = _connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = "SELECT Id, Name, RecordedGeneration, RecordedBirthOrder FROM People WHERE GenealogyId=$genealogy AND Id=$person";
            command.Parameters.AddWithValue("$genealogy", SqlGuid(_genealogyId)); command.Parameters.AddWithValue("$person", SqlGuid(personId));
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            if (!await reader.ReadAsync(cancellation)) throw new InvalidOperationException("人物不存在于当前族谱。");
            return new PersonRow(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
        }, cancellationToken);
    }

    public Task<LinkPage<PersonRow>> SearchPeopleAsync(Guid token, string query, Guid? after, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200) throw new ArgumentException("搜索内容须为 1 至 200 字。", nameof(query));
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        var text = query.Trim();
        return ReadGuardedAsync(token, async (tx, cancellation) =>
        {
            await using var command = _connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = "SELECT Id, Name, RecordedGeneration, RecordedBirthOrder FROM People WHERE GenealogyId=$genealogy AND (instr(Name,$text)>0 OR Id=$id) AND ($after IS NULL OR Id>$after) ORDER BY Id LIMIT $limit";
            command.Parameters.AddWithValue("$genealogy", SqlGuid(_genealogyId)); command.Parameters.AddWithValue("$text", text);
            command.Parameters.AddWithValue("$id", Guid.TryParse(text, out var id) ? SqlGuid(id) : DBNull.Value);
            command.Parameters.AddWithValue("$after", after is Guid cursor ? SqlGuid(cursor) : DBNull.Value); command.Parameters.AddWithValue("$limit", limit + 1);
            var rows = new List<PersonRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation)) rows.Add(new PersonRow(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
            var more = rows.Count > limit; if (more) rows.RemoveAt(rows.Count - 1);
            return new LinkPage<PersonRow>(rows.AsReadOnly(), more ? rows[^1].Id : null);
        }, cancellationToken);
    }

    public Task<LinkPage<ParentCandidate>> ReadParentCandidatesAsync(Guid token, Guid personId, Guid? after, int limit, CancellationToken cancellationToken = default) =>
        ReadLinksAsync(token, personId, after, limit, false, reader => new ParentCandidate(
            Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.GetString(3),
            (PersonRelationshipKind)reader.GetInt32(4), reader.GetInt32(5) != 0, reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7)), cancellationToken);

    public Task<LinkPage<UnionRow>> ReadUnionsAsync(Guid token, Guid personId, Guid? after, int limit, CancellationToken cancellationToken = default) =>
        ReadLinksAsync(token, personId, after, limit, true, reader => new UnionRow(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)), (FamilyUnionKind)reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6)), cancellationToken);

    public Task<LinkPage<ChildCandidate>> ReadChildCandidatesAsync(Guid token, Guid parentId, Guid? after, int limit, CancellationToken cancellationToken = default)
    {
        if (parentId == Guid.Empty) throw new ArgumentException("人物 ID 无效。", nameof(parentId));
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        return ReadGuardedAsync(token, async (tx, cancellation) =>
        {
            await RequirePersonAsync(tx, parentId, cancellation);
            await using var command = _connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = "SELECT r.Id, r.PersonId, p.Name, p.RecordedGeneration, p.RecordedBirthOrder, r.Kind FROM PersonRelationships r LEFT JOIN People p ON p.Id=r.PersonId AND p.GenealogyId=r.GenealogyId WHERE r.GenealogyId=$genealogy AND r.RelatedPersonId=$person AND r.Kind IN (1,2,3) AND ($after IS NULL OR r.Id>$after) ORDER BY r.Id LIMIT $limit";
            command.Parameters.AddWithValue("$genealogy", SqlGuid(_genealogyId)); command.Parameters.AddWithValue("$person", SqlGuid(parentId));
            command.Parameters.AddWithValue("$after", after is Guid id ? SqlGuid(id) : DBNull.Value); command.Parameters.AddWithValue("$limit", limit + 1);
            var rows = new List<ChildCandidate>();
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation)) rows.Add(new ChildCandidate(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), (PersonRelationshipKind)reader.GetInt32(5)));
            var more = rows.Count > limit; if (more) rows.RemoveAt(rows.Count - 1);
            return new LinkPage<ChildCandidate>(rows.AsReadOnly(), more ? rows[^1].RelationshipId : null);
        }, cancellationToken);
    }

    public Task<LinkPage<SiblingRow>> ReadSiblingsAsync(Guid token, Guid personId, Guid? after, int limit, CancellationToken cancellationToken = default)
    {
        if (personId == Guid.Empty || limit is < 1 or > 200) throw new ArgumentException("人物或分页参数无效。");
        return ReadGuardedAsync(token, async (tx, cancellation) =>
        {
            await RequirePersonAsync(tx, personId, cancellation);
            await using var command = _connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = "SELECT r.Id, p.Id, p.Name FROM PersonRelationships r JOIN People p ON p.GenealogyId=r.GenealogyId AND p.Id=CASE WHEN r.PersonId=$person THEN r.RelatedPersonId ELSE r.PersonId END WHERE r.GenealogyId=$genealogy AND r.Kind=6 AND (r.PersonId=$person OR r.RelatedPersonId=$person) AND ($after IS NULL OR r.Id>$after) ORDER BY r.Id LIMIT $limit";
            command.Parameters.AddWithValue("$genealogy", SqlGuid(_genealogyId)); command.Parameters.AddWithValue("$person", SqlGuid(personId));
            command.Parameters.AddWithValue("$after", after is Guid cursor ? SqlGuid(cursor) : DBNull.Value); command.Parameters.AddWithValue("$limit", limit + 1);
            var rows = new List<SiblingRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation)) rows.Add(new SiblingRow(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2)));
            var more = rows.Count > limit; if (more) rows.RemoveAt(rows.Count - 1);
            return new LinkPage<SiblingRow>(rows.AsReadOnly(), more ? rows[^1].RelationshipId : null);
        }, cancellationToken);
    }

    public Task<LinkPage<ChartRow>> ReadChartsAsync(Guid token, Guid? after, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        return ReadGuardedAsync(token, async (tx, cancellation) =>
        {
            await using var command = _connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = "SELECT Id, Name, Revision, Generation FROM LineageCharts WHERE GenealogyId=$genealogy AND ($after IS NULL OR Id>$after) ORDER BY Id LIMIT $limit";
            command.Parameters.AddWithValue("$genealogy", SqlGuid(_genealogyId));
            command.Parameters.AddWithValue("$after", after is Guid id ? SqlGuid(id) : DBNull.Value); command.Parameters.AddWithValue("$limit", limit + 1);
            var rows = new List<ChartRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation)) rows.Add(new ChartRow(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
            var more = rows.Count > limit; if (more) rows.RemoveAt(rows.Count - 1);
            return new LinkPage<ChartRow>(rows.AsReadOnly(), more ? rows[^1].Id : null);
        }, cancellationToken);
    }

    public Task<LinkPage<OtherRelationshipRow>> ReadOtherRelationshipsAsync(Guid token, Guid personId, Guid? after, int limit, CancellationToken cancellationToken = default)
    {
        if (personId == Guid.Empty) throw new ArgumentException("人物 ID 无效。", nameof(personId));
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        return ReadGuardedAsync(token, async (tx, cancellation) =>
        {
            await RequirePersonAsync(tx, personId, cancellation);
            await using var command = _connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = "SELECT Id, PersonId, RelatedPersonId, Kind, Note, SourceCitation FROM PersonRelationships WHERE GenealogyId=$genealogy AND ((Kind=5 AND PersonId=$person) OR (Kind IN (4,6) AND (PersonId=$person OR RelatedPersonId=$person))) AND ($after IS NULL OR Id>$after) ORDER BY Id LIMIT $limit";
            command.Parameters.AddWithValue("$genealogy", SqlGuid(_genealogyId)); command.Parameters.AddWithValue("$person", SqlGuid(personId));
            command.Parameters.AddWithValue("$after", after is Guid id ? SqlGuid(id) : DBNull.Value); command.Parameters.AddWithValue("$limit", limit + 1);
            var rows = new List<OtherRelationshipRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation)) rows.Add(new OtherRelationshipRow(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)), (PersonRelationshipKind)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
            var more = rows.Count > limit; if (more) rows.RemoveAt(rows.Count - 1);
            return new LinkPage<OtherRelationshipRow>(rows.AsReadOnly(), more ? rows[^1].Id : null);
        }, cancellationToken);
    }

    private async Task<LinkPage<T>> ReadLinksAsync<T>(Guid token, Guid personId, Guid? after, int limit, bool unions,
        Func<SqliteDataReader, T> map, CancellationToken cancellationToken)
    {
        if (personId == Guid.Empty) throw new ArgumentException("人物 ID 无效。", nameof(personId));
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        return await ReadGuardedAsync(token, async (tx, cancellation) =>
        {
            await RequirePersonAsync(tx, personId, cancellation);
            await using var command = _connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = unions
                ? "SELECT Id, PartnerOneId, PartnerTwoId, Kind, StartDateText, EndDateText, Note FROM FamilyUnions WHERE GenealogyId=$genealogy AND (PartnerOneId=$person OR PartnerTwoId=$person) AND ($after IS NULL OR Id>$after) ORDER BY Id LIMIT $limit"
                : "SELECT r.Id, r.PersonId, r.RelatedPersonId, p.Name, r.Kind, r.IsPreferredForLineage, r.Note, r.SourceCitation FROM PersonRelationships r LEFT JOIN People p ON p.Id=r.RelatedPersonId AND p.GenealogyId=r.GenealogyId WHERE r.GenealogyId=$genealogy AND r.PersonId=$person AND r.Kind IN (1,2,3) AND ($after IS NULL OR r.Id>$after) ORDER BY r.Id LIMIT $limit";
            command.Parameters.AddWithValue("$genealogy", SqlGuid(_genealogyId)); command.Parameters.AddWithValue("$person", SqlGuid(personId));
            command.Parameters.AddWithValue("$after", after is Guid id ? SqlGuid(id) : DBNull.Value); command.Parameters.AddWithValue("$limit", limit + 1);
            var rows = new List<(Guid Id, T Item)>();
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation)) rows.Add((Guid.Parse(reader.GetString(0)), map(reader)));
            var more = rows.Count > limit; if (more) rows.RemoveAt(rows.Count - 1);
            return new LinkPage<T>(rows.Select(row => row.Item).ToList().AsReadOnly(), more ? rows[^1].Id : null);
        }, cancellationToken);
    }

    public Task<ChartChoiceState?> ReadChartChoiceAsync(Guid token, Guid chartId, Guid personId, CancellationToken cancellationToken = default)
    {
        if (chartId == Guid.Empty || personId == Guid.Empty) throw new ArgumentException("图表和人物 ID 不能为空。");
        return ReadGuardedAsync<ChartChoiceState?>(token, async (tx, cancellation) =>
        {
            long chartGeneration;
            await using (var chart = _connection.CreateCommand())
            {
                chart.Transaction = tx; chart.CommandText = "SELECT Generation FROM LineageCharts WHERE Id=$chart AND GenealogyId=$genealogy";
                chart.Parameters.AddWithValue("$chart", SqlGuid(chartId)); chart.Parameters.AddWithValue("$genealogy", SqlGuid(_genealogyId));
                var value = await chart.ExecuteScalarAsync(cancellation);
                if (value is null) throw new InvalidOperationException("图表不存在于当前族谱。");
                chartGeneration = Convert.ToInt64(value);
            }
            long currentGeneration;
            await using (var generation = _connection.CreateCommand())
            {
                generation.Transaction = tx; generation.CommandText = UnlinkGenerationV1.GuardSql;
                var value = await generation.ExecuteScalarAsync(cancellation);
                if (value is null) throw new InvalidOperationException("数据库版本守卫失效。");
                currentGeneration = Convert.ToInt64(value);
            }
            await using var choice = _connection.CreateCommand(); choice.Transaction = tx;
            choice.CommandText = "SELECT c.ParentId, c.RelationshipId, c.Kind, c.PersonName, c.ParentName, " +
                "EXISTS(SELECT 1 FROM PersonRelationships r JOIN People parent ON parent.Id=r.RelatedPersonId AND parent.GenealogyId=r.GenealogyId " +
                "JOIN People child ON child.Id=r.PersonId AND child.GenealogyId=r.GenealogyId " +
                "WHERE r.GenealogyId=$genealogy AND r.Id=c.RelationshipId AND r.PersonId=c.PersonId AND r.RelatedPersonId=c.ParentId AND r.Kind=c.Kind AND r.Kind IN (1,2,3)) " +
                "FROM LineageChartChoices c WHERE c.ChartId=$chart AND c.PersonId=$person";
            choice.Parameters.AddWithValue("$chart", SqlGuid(chartId)); choice.Parameters.AddWithValue("$person", SqlGuid(personId));
            choice.Parameters.AddWithValue("$genealogy", SqlGuid(_genealogyId));
            await using var reader = await choice.ExecuteReaderAsync(cancellation);
            if (!await reader.ReadAsync(cancellation)) return null;
            var valid = reader.GetInt32(5) != 0;
            var changed = chartGeneration != currentGeneration;
            return new ChartChoiceState(chartId, personId, Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                (PersonRelationshipKind)reader.GetInt32(2), reader.GetString(3), reader.GetString(4), changed || !valid,
                !valid ? "人物或关系已变化" : changed ? "数据版本已变化，须复核" : null);
        }, cancellationToken);
    }

    private async Task RequirePersonAsync(SqliteTransaction tx, Guid personId, CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT COUNT(*) FROM People WHERE GenealogyId=$genealogy AND Id=$person";
        command.Parameters.AddWithValue("$genealogy", SqlGuid(_genealogyId)); command.Parameters.AddWithValue("$person", SqlGuid(personId));
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("人物不存在于当前族谱。");
    }
    private static string SqlGuid(Guid value) => value.ToString().ToUpperInvariant();

    private async Task<T> ReadGuardedAsync<T>(Guid token, Func<SqliteTransaction, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        EnsureValid(token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
        try { await _gate.WaitAsync(linked.Token); } catch { _invalid = true; throw; }
        try
        {
            EnsureValid(token);
            await using var tx = _connection.BeginTransaction(deferred: true);
            if (await ReadVersionAsync(_connection, tx, linked.Token) != _version) throw new InvalidOperationException("数据库已变化，请重新打开。");
            if (AfterVersionChecked is not null) await AfterVersionChecked(linked.Token);
            var result = await read(tx, linked.Token);
            await tx.CommitAsync(linked.Token);
            if (await ReadVersionAsync(_connection, null, linked.Token) != _version) throw new InvalidOperationException("数据库已变化，请重新打开。");
            linked.Token.ThrowIfCancellationRequested(); EnsureValid(token); return result;
        }
        catch { _invalid = true; throw; }
        finally { _gate.Release(); }
    }
}
