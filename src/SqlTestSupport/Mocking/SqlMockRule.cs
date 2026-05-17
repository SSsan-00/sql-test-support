using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport
{
    internal enum SqlMockReturnKind
    {
        None = 0,
        AffectedRows,
        Scalar
    }

    internal sealed class SqlMockRule
    {
        private readonly Func<SqlInvocation, bool> _predicate;
        private readonly Queue<object?> _returns = new();
        private bool _isSequence;
        private object? _singleReturn;

        public SqlMockRule(Func<SqlInvocation, bool> predicate)
        {
            _predicate = predicate;
        }

        public int CallCount { get; private set; }

        public SqlMockReturnKind ReturnKind { get; private set; }

        public bool Matches(SqlInvocation invocation)
            => _predicate(invocation);

        public void SetAffectedRows(int affectedRows)
        {
            ReturnKind = SqlMockReturnKind.AffectedRows;
            _isSequence = false;
            _singleReturn = affectedRows;
            _returns.Clear();
        }

        public void SetAffectedRowsSequence(params int[] affectedRows)
        {
            ReturnKind = SqlMockReturnKind.AffectedRows;
            SetSequence(affectedRows.Cast<object?>());
        }

        public void SetScalar(object? value)
        {
            ReturnKind = SqlMockReturnKind.Scalar;
            _isSequence = false;
            _singleReturn = value;
            _returns.Clear();
        }

        public void SetScalarSequence(params object?[] values)
        {
            ReturnKind = SqlMockReturnKind.Scalar;
            SetSequence(values);
        }

        public int GetAffectedRows(SqlInvocation invocation)
        {
            if (ReturnKind != SqlMockReturnKind.AffectedRows)
            {
                throw new AssertFailedException("Matched SQL rule does not return affected rows.");
            }

            var value = NextReturn(invocation);
            if (value is int affectedRows)
            {
                return affectedRows;
            }

            throw new AssertFailedException($"Affected rows rule returned {value?.GetType().FullName ?? "null"}.");
        }

        public object? GetScalar(SqlInvocation invocation)
        {
            if (ReturnKind != SqlMockReturnKind.Scalar)
            {
                throw new AssertFailedException("Matched SQL rule does not return a scalar value.");
            }

            return NextReturn(invocation);
        }

        private void SetSequence(IEnumerable<object?> values)
        {
            _isSequence = true;
            _singleReturn = null;
            _returns.Clear();

            foreach (var value in values)
            {
                _returns.Enqueue(value);
            }
        }

        private object? NextReturn(SqlInvocation invocation)
        {
            CallCount++;

            if (!_isSequence)
            {
                return _singleReturn;
            }

            if (_returns.Count == 0)
            {
                throw new AssertFailedException($"""
                    SQL mock sequence was exhausted.

                    Method:
                    {invocation.Method}

                    Normalized SQL:
                    {invocation.NormalizedSql}
                    """);
            }

            return _returns.Dequeue();
        }
    }
}
