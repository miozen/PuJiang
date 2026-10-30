namespace ZupuPaiBan.Data;

// Migration SQL is versioned and must remain immutable: existing databases store these exact trigger definitions.
internal static class UnlinkGenerationV1
{
    internal const string TableSql = "CREATE TABLE UnlinkGeneration (Id INTEGER PRIMARY KEY CHECK (Id = 1), Value INTEGER NOT NULL CHECK (typeof(Value) = 'integer' AND Value >= 0))";
    internal static readonly string[] Tables = ["People", "PersonRelationships", "FamilyUnions"];
    internal static readonly string[] Operations = ["INSERT", "UPDATE", "DELETE"];

    internal static string TriggerSql(string table, string operation) =>
        $"CREATE TRIGGER UnlinkGeneration{table}{operation} AFTER {operation} ON {table} BEGIN SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM UnlinkGeneration WHERE Id = 1 AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807) THEN RAISE(ABORT, 'invalid unlink generation') END; UPDATE UnlinkGeneration SET Value = Value + 1 WHERE Id = 1; END";

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    // This SELECT is also embedded as a subquery inside the DELETE, atomically checking generation and schema.
    internal static readonly string GuardSql = BuildGuardSql();
    private static string BuildGuardSql()
    {
        var sql = "SELECT Value AS Value FROM UnlinkGeneration WHERE Id = 1 AND typeof(Value) = 'integer' AND Value >= 0 AND Value < 9223372036854775807";
        sql += $" AND EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'UnlinkGeneration' AND sql = {Literal(TableSql)})";
        foreach (var table in Tables)
        foreach (var operation in Operations)
            sql += $" AND EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'trigger' AND name = {Literal($"UnlinkGeneration{table}{operation}")} AND sql = {Literal(TriggerSql(table, operation))})";
        return sql;
    }
}
