namespace SqlTestSupport
{
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
