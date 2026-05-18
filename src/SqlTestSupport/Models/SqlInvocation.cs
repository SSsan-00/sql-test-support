namespace SqlTestSupport
{
    // WhenSql predicate に渡す、検証・正規化・抽出済みの SQL 呼び出し情報。
    public sealed record SqlInvocation(
        DbCallMethod Method,
        string OriginalSql,
        string NormalizedSql,
        string Fingerprint,
        SqlStatementKind StatementKind,
        IReadOnlySet<string> TargetTables,
        IReadOnlySet<string> ReferencedTables,
        IReadOnlySet<string> SelectedColumns,
        IReadOnlySet<string> WhereColumns,
        IReadOnlySet<string> ParameterNames,
        int GlobalCallIndex,
        int MethodCallIndex)
    {
        // テストコード側で SQL 形状を簡潔に表現する matcher。
        public bool IsSelectFrom(string table)
            => StatementKind == SqlStatementKind.Select && ReferencesTable(table);

        public bool IsInsertInto(string table)
            => StatementKind == SqlStatementKind.Insert && TargetsTable(table);

        public bool IsUpdate(string table)
            => StatementKind == SqlStatementKind.Update && TargetsTable(table);

        public bool IsDeleteFrom(string table)
            => StatementKind == SqlStatementKind.Delete && TargetsTable(table);

        public bool WhereUses(string column)
            => ContainsIdentifier(WhereColumns, column);

        public bool SelectsColumn(string column)
            => ContainsIdentifier(SelectedColumns, column);

        public bool ReferencesTable(string table)
            => ContainsIdentifier(ReferencedTables, table);

        public bool TargetsTable(string table)
            => ContainsIdentifier(TargetTables, table);

        public bool HasParameter(string parameterName)
            => ContainsIdentifier(ParameterNames, parameterName);

        private static bool ContainsIdentifier(IReadOnlySet<string> values, string expected)
            // schema 付き・schema なしの両方を軽く許容。
            => values.Any(value =>
                StringComparer.OrdinalIgnoreCase.Equals(value, expected) ||
                StringComparer.OrdinalIgnoreCase.Equals(LastPart(value), expected) ||
                StringComparer.OrdinalIgnoreCase.Equals(value, LastPart(expected)));

        private static string LastPart(string value)
        {
            var index = value.LastIndexOf('.');
            return index < 0 ? value : value[(index + 1)..];
        }
    }
}
