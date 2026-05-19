namespace SqlTestSupport
{
    // WhenSql で一致した SQL に対する戻り値設定を受け持つ。
    public sealed class SqlMockSetup
    {
        private readonly SqlMockRule _rule;

        internal SqlMockSetup(SqlMockRule rule)
        {
            _rule = rule;
        }

        public SqlMockSetup ReturnsResult(object? value)
        {
            _rule.SetResult(value);
            return this;
        }

        public SqlMockSetup ReturnsResultSequence(params object?[] values)
        {
            _rule.SetResultSequence(values);
            return this;
        }
    }
}
