namespace SqlTestSupport
{
    public sealed class SqlSyntaxValidationException : SqlValidationException
    {
        public SqlSyntaxValidationException(string sql, IReadOnlyList<SqlParseDiagnostic> diagnostics)
            : base("SQL syntax validation failed.", sql)
        {
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<SqlParseDiagnostic> Diagnostics { get; }
    }
}
