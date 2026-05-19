using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport
{
    // 1 つの WhenSql predicate と、それに対応する戻り値設定を保持する。
    internal sealed class SqlMockRule
    {
        private readonly Func<SqlInvocation, bool> _predicate;
        private readonly Queue<object?> _returns = new();
        private bool _hasConfiguredResult;
        private bool _isSequence;
        private object? _singleReturn;

        public SqlMockRule(Func<SqlInvocation, bool> predicate)
        {
            _predicate = predicate;
        }

        public int CallCount { get; private set; }

        public bool Matches(SqlInvocation invocation)
            => _predicate(invocation);

        public void SetResult(object? value)
        {
            _hasConfiguredResult = true;
            _isSequence = false;
            _singleReturn = value;
            _returns.Clear();
        }

        public void SetResultSequence(params object?[] values)
        {
            _hasConfiguredResult = true;
            _isSequence = true;
            _singleReturn = null;
            _returns.Clear();

            foreach (var value in values)
            {
                _returns.Enqueue(value);
            }
        }

        public object? GetResult(SqlInvocation invocation, Func<object?> defaultValueFactory)
        {
            CallCount++;

            if (!_hasConfiguredResult)
            {
                return defaultValueFactory();
            }

            if (!_isSequence)
            {
                return _singleReturn;
            }

            if (_returns.Count == 0)
            {
                // sequence は期待回数も表す。余分な呼び出しは失敗。
                throw new AssertFailedException($"""
                    SQL mock の戻り値シーケンスを使い切りました。

                    呼び出し番号:
                    {invocation.CallIndex}

                    対象 SQL:
                    {invocation.OriginalSql}
                    """);
            }

            return _returns.Dequeue();
        }
    }
}
