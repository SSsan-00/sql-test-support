namespace SqlTestSupport
{
    public sealed class SqlMockSetup
    {
        private readonly SqlMockRule _rule;

        internal SqlMockSetup(SqlMockRule rule)
        {
            _rule = rule;
        }

        public SqlMockSetup ReturnsAffectedRows(int affectedRows)
        {
            _rule.SetAffectedRows(affectedRows);
            return this;
        }

        public SqlMockSetup ReturnsAffectedRowsSequence(params int[] affectedRows)
        {
            _rule.SetAffectedRowsSequence(affectedRows);
            return this;
        }

        public SqlMockSetup ReturnsScalar(object? value)
        {
            _rule.SetScalar(value);
            return this;
        }

        public SqlMockSetup ReturnsScalarSequence(params object?[] values)
        {
            _rule.SetScalarSequence(values);
            return this;
        }

        public SqlMockSetup Completes()
        {
            _rule.SetCompletes();
            return this;
        }
    }
}
