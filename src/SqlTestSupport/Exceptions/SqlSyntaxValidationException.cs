namespace SqlTestSupport
{
    // ScriptDom が返した parse error を保持する構文検証例外。
    public sealed class SqlSyntaxValidationException : SqlValidationException
    {
        public SqlSyntaxValidationException(string sql, IReadOnlyList<SqlParseDiagnostic> diagnostics)
            : base("SQL の構文検証に失敗しました。", sql)
        {
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<SqlParseDiagnostic> Diagnostics { get; }
    }
}
