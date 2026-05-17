using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport
{
    public sealed class SqlMockRouter
    {
        private readonly SqlValidationService _validationService;
        private readonly List<SqlMockRule> _rules = new();
        private readonly List<SqlInvocation> _history = new();
        private readonly Dictionary<DbCallMethod, int> _methodCallCounts = new();
        private int _globalCallCount;

        public SqlMockRouter()
            : this(new SqlValidationService())
        {
        }

        public SqlMockRouter(SqlValidationService validationService)
        {
            _validationService = validationService;
        }

        public IReadOnlyList<SqlInvocation> History => _history;

        public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);

            var rule = new SqlMockRule(predicate);
            _rules.Add(rule);
            return new SqlMockSetup(rule);
        }

        public int ExecuteNonQuery(string sql)
        {
            var invocation = CreateInvocation(DbCallMethod.ExecuteNonQuery, sql);
            var rule = FindRule(invocation);
            return rule.GetAffectedRows(invocation);
        }

        public void ExecuteCommand(string sql)
        {
            var invocation = CreateInvocation(DbCallMethod.Command, sql);
            var rule = FindRule(invocation);
            rule.Complete(invocation);
        }

        public T Scalar<T>(string sql)
        {
            var invocation = CreateInvocation(DbCallMethod.Scalar, sql);
            var rule = FindRule(invocation);
            var value = rule.GetScalar(invocation);

            if (value is null)
            {
                if (default(T) is null)
                {
                    return default!;
                }

                throw new AssertFailedException($"SQL mock returned null for non-nullable scalar type {typeof(T).FullName}.");
            }

            if (value is T typed)
            {
                return typed;
            }

            return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        public void VerifyAll()
        {
            var failures = _rules
                .Where(rule => rule.CallCount == 0)
                .Select((rule, index) => $"Rule #{index + 1} was not called.")
                .ToArray();

            if (failures.Length == 0)
            {
                return;
            }

            throw new AssertFailedException(string.Join(Environment.NewLine, failures));
        }

        private SqlInvocation CreateInvocation(DbCallMethod method, string sql)
        {
            SqlInspectionResult inspection;
            try
            {
                // すべての Mock 実行 SQL は rule matching 前に検証・正規化。
                inspection = _validationService.Inspect(sql);
            }
            catch (SqlValidationException exception)
            {
                throw new AssertFailedException(
                    SqlAssertMessageBuilder.Build("SQL mock received invalid SQL.", exception),
                    exception);
            }

            var methodCallIndex = _methodCallCounts.TryGetValue(method, out var count) ? count : 0;
            _methodCallCounts[method] = methodCallIndex + 1;

            var invocation = new SqlInvocation(
                method,
                inspection.OriginalSql,
                inspection.NormalizedSql,
                inspection.Fingerprint,
                inspection.StatementKind,
                inspection.TargetTables,
                inspection.ReferencedTables,
                inspection.SelectedColumns,
                inspection.WhereColumns,
                inspection.ParameterNames,
                _globalCallCount++,
                methodCallIndex);

            _history.Add(invocation);
            return invocation;
        }

        private SqlMockRule FindRule(SqlInvocation invocation)
        {
            foreach (var rule in _rules)
            {
                if (rule.Matches(invocation))
                {
                    return rule;
                }
            }

            // strict mode。未登録 SQL はテスト失敗。
            throw UnexpectedSql(invocation);
        }

        private AssertFailedException UnexpectedSql(SqlInvocation invocation)
        {
            var message = $"""
                Unexpected SQL execution.

                Method:
                {invocation.Method}

                Statement kind:
                {invocation.StatementKind}

                Normalized SQL:
                {invocation.NormalizedSql}
                """;

            return new AssertFailedException(message);
        }
    }
}
