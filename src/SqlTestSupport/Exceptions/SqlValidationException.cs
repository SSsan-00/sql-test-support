namespace SqlTestSupport
{
    // SQL 検証系例外の共通基底。失敗した SQL 文字列を必ず保持する。
    public class SqlValidationException : Exception
    {
        public SqlValidationException(string message, string sql, Exception? innerException = null)
            : base(message, innerException)
        {
            Sql = sql;
        }

        public string Sql { get; }
    }
}
