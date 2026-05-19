namespace SqlTestSupport
{
    // Mock 分岐に使う AST 由来の SQL metadata。
    public sealed record SqlInspectionResult(
        string OriginalSql,
        SqlStatementKind StatementKind,
        IReadOnlySet<string> TargetTables,
        IReadOnlySet<string> ReferencedTables,
        IReadOnlySet<string> JoinedTables,
        IReadOnlySet<string> SelectedColumns,
        IReadOnlySet<string> WhereColumns,
        IReadOnlySet<string> OrderByColumns,
        IReadOnlySet<string> GroupByColumns,
        IReadOnlySet<string> HavingColumns,
        IReadOnlySet<string> HavingFunctions,
        IReadOnlySet<string> ParameterNames);
}
