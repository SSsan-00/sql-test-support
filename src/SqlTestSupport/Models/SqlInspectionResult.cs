namespace SqlTestSupport
{
    // Mock 分岐に使う AST 由来の SQL metadata。
    public sealed record SqlInspectionResult(
        string OriginalSql,
        string NormalizedSql,
        string Fingerprint,
        SqlStatementKind StatementKind,
        IReadOnlySet<string> TargetTables,
        IReadOnlySet<string> ReferencedTables,
        IReadOnlySet<string> SelectedColumns,
        IReadOnlySet<string> WhereColumns,
        IReadOnlySet<string> ParameterNames);
}
