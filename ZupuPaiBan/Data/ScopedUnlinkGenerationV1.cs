namespace ZupuPaiBan.Data;

// Migration SQL is versioned and must remain immutable: existing databases store these exact definitions.
internal static class ScopedUnlinkGenerationV1
{
    internal const string ActiveTable = "ScopedUnlinkGenerationV1";
    internal const string TombstoneTable = "ScopedUnlinkGenerationTombstoneV1";
    internal const string ActiveTableSql = "CREATE TABLE ScopedUnlinkGenerationV1 (GenealogyId TEXT PRIMARY KEY, Value INTEGER NOT NULL CHECK (typeof(Value) = 'integer' AND Value >= 0))";
    internal const string TombstoneTableSql = "CREATE TABLE ScopedUnlinkGenerationTombstoneV1 (GenealogyId TEXT PRIMARY KEY, Value INTEGER NOT NULL CHECK (typeof(Value) = 'integer' AND Value >= 0))";
    internal static readonly string[] Tables = ["People", "PersonRelationships", "FamilyUnions"];
    internal static readonly string[] Operations = ["INSERT", "UPDATE", "DELETE"];

    private const string Error = "invalid scoped unlink generation";
    private const string ActiveIsValid = "typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807";
    private const string TombstoneIsValid = "typeof(Value) = 'integer' AND Value >= 0 AND Value <= 9223372036854775807";

    internal const string GenealogyInsertTriggerName = "ScopedUnlinkGenerationV1GenealogiesINSERT";
    internal const string GenealogyDeleteTriggerName = "ScopedUnlinkGenerationV1GenealogiesDELETE";
    internal static readonly string GenealogyInsertTriggerSql =
        "CREATE TRIGGER ScopedUnlinkGenerationV1GenealogiesINSERT AFTER INSERT ON Genealogies BEGIN " +
        "SELECT CASE WHEN EXISTS (SELECT 1 FROM ScopedUnlinkGenerationV1 WHERE GenealogyId = NEW.Id) OR EXISTS (SELECT 1 FROM ScopedUnlinkGenerationTombstoneV1 WHERE GenealogyId = NEW.Id AND NOT (typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807)) THEN RAISE(ABORT, 'invalid scoped unlink generation') END; " +
        "INSERT INTO ScopedUnlinkGenerationV1 (GenealogyId, Value) VALUES (NEW.Id, COALESCE((SELECT Value + 1 FROM ScopedUnlinkGenerationTombstoneV1 WHERE GenealogyId = NEW.Id), 0)); " +
        "DELETE FROM ScopedUnlinkGenerationTombstoneV1 WHERE GenealogyId = NEW.Id; END";
    internal static readonly string GenealogyDeleteTriggerSql =
        "CREATE TRIGGER ScopedUnlinkGenerationV1GenealogiesDELETE AFTER DELETE ON Genealogies BEGIN " +
        "SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM ScopedUnlinkGenerationV1 WHERE GenealogyId = OLD.Id AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807) OR EXISTS (SELECT 1 FROM ScopedUnlinkGenerationTombstoneV1 WHERE GenealogyId = OLD.Id) THEN RAISE(ABORT, 'invalid scoped unlink generation') END; " +
        "INSERT INTO ScopedUnlinkGenerationTombstoneV1 (GenealogyId, Value) SELECT OLD.Id, Value + 1 FROM ScopedUnlinkGenerationV1 WHERE GenealogyId = OLD.Id; " +
        "DELETE FROM ScopedUnlinkGenerationV1 WHERE GenealogyId = OLD.Id; END";

    internal static string TriggerName(string table, string operation) => $"ScopedUnlinkGenerationV1{table}{operation}";

    internal static string TriggerSql(string table, string operation)
    {
        var name = TriggerName(table, operation);
        return operation switch
        {
            "INSERT" => $"CREATE TRIGGER {name} AFTER INSERT ON {table} BEGIN {RequireActive("NEW.GenealogyId")} UPDATE {ActiveTable} SET Value = Value + 1 WHERE GenealogyId = NEW.GenealogyId; END",
            "DELETE" => $"CREATE TRIGGER {name} AFTER DELETE ON {table} BEGIN {RequireActive("OLD.GenealogyId")} UPDATE {ActiveTable} SET Value = Value + 1 WHERE GenealogyId = OLD.GenealogyId; END",
            "UPDATE" => $"CREATE TRIGGER {name} AFTER UPDATE ON {table} BEGIN {RequireActive("OLD.GenealogyId")} {RequireDifferentNewActive()} UPDATE {ActiveTable} SET Value = Value + 1 WHERE GenealogyId = OLD.GenealogyId; UPDATE {ActiveTable} SET Value = Value + 1 WHERE GenealogyId = NEW.GenealogyId AND NEW.GenealogyId <> OLD.GenealogyId; END",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    internal static IEnumerable<(string Name, string Sql)> ExpectedTriggers()
    {
        yield return (GenealogyInsertTriggerName, GenealogyInsertTriggerSql);
        yield return (GenealogyDeleteTriggerName, GenealogyDeleteTriggerSql);
        foreach (var table in Tables)
        foreach (var operation in Operations)
            yield return (TriggerName(table, operation), TriggerSql(table, operation));
    }

    internal static IEnumerable<string> ReinstallTableTriggerSql(string table)
    {
        if (!Tables.Contains(table, StringComparer.Ordinal)) throw new ArgumentOutOfRangeException(nameof(table));
        foreach (var operation in Operations)
        {
            yield return $"DROP TRIGGER IF EXISTS UnlinkGeneration{table}{operation}";
            yield return $"DROP TRIGGER IF EXISTS {TriggerName(table, operation)}";
        }
        foreach (var operation in Operations)
            yield return UnlinkGenerationV1.TriggerSql(table, operation);
        foreach (var operation in Operations)
            yield return TriggerSql(table, operation);
    }

    internal static string GuardSql(string genealogyParameter = "@genealogy") =>
        $"SELECT Value AS Value FROM {ActiveTable} WHERE GenealogyId = {genealogyParameter} AND {ActiveIsValid} AND {DatabaseIsValidSql()}";

    // A one-row query used by backup preflight and startup verification.
    internal static string DatabaseGuardSql => $"SELECT 1 AS Value WHERE {DatabaseIsValidSql()}";

    // SQLite RAISE is only valid inside a trigger. This query deliberately violates the active table CHECK when invalid.
    internal static string InstallValidationSql =>
        $"INSERT INTO {ActiveTable} (GenealogyId, Value) SELECT '__ScopedUnlinkGenerationV1InstallValidation__', -1 WHERE NOT ({DatabaseIsValidSql()})";

    private static string RequireActive(string id) =>
        $"SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM {ActiveTable} WHERE GenealogyId = {id} AND {ActiveIsValid}) THEN RAISE(ABORT, '{Error}') END;";

    private static string RequireDifferentNewActive() =>
        $"SELECT CASE WHEN NEW.GenealogyId <> OLD.GenealogyId AND NOT EXISTS (SELECT 1 FROM {ActiveTable} WHERE GenealogyId = NEW.GenealogyId AND {ActiveIsValid}) THEN RAISE(ABORT, '{Error}') END;";

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string DatabaseIsValidSql()
    {
        var sql = $"EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = {Literal(ActiveTable)} AND sql = {Literal(ActiveTableSql)})";
        sql += $" AND EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = {Literal(TombstoneTable)} AND sql = {Literal(TombstoneTableSql)})";
        foreach (var (name, definition) in ExpectedTriggers())
            sql += $" AND EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'trigger' AND name = {Literal(name)} AND sql = {Literal(definition)})";
        sql += $" AND NOT EXISTS (SELECT 1 FROM Genealogies g WHERE NOT EXISTS (SELECT 1 FROM {ActiveTable} a WHERE a.GenealogyId = g.Id AND {ActiveIsValid}))";
        sql += $" AND NOT EXISTS (SELECT 1 FROM {ActiveTable} a WHERE NOT EXISTS (SELECT 1 FROM Genealogies g WHERE g.Id = a.GenealogyId))";
        sql += $" AND NOT EXISTS (SELECT 1 FROM {ActiveTable} a INNER JOIN {TombstoneTable} t ON t.GenealogyId = a.GenealogyId)";
        sql += $" AND NOT EXISTS (SELECT 1 FROM {TombstoneTable} WHERE NOT ({TombstoneIsValid}))";
        return sql;
    }
}
