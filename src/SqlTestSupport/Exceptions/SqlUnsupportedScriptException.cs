namespace SqlTestSupport
{
    // command text として扱わない SQL 入力を明示する例外。
    public sealed class SqlUnsupportedScriptException : SqlValidationException
    {
        public SqlUnsupportedScriptException(string sql, string reason)
            : base($"SQL script is not supported by this test helper: {reason}", sql)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}
