using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport
{
    public sealed class SqlMockRouter
    {
        private readonly SqlValidationService _validationService;
        private readonly UnmatchedSqlBehavior _unmatchedSqlBehavior;
        private readonly List<SqlMockRule> _rules = new();
        private readonly List<SqlInvocation> _history = new();
        private readonly Dictionary<DbCallMethod, int> _methodCallCounts = new();
        private int _globalCallCount;

        public SqlMockRouter()
            : this(UnmatchedSqlBehavior.Strict)
        {
        }

        public SqlMockRouter(UnmatchedSqlBehavior unmatchedSqlBehavior)
            : this(new SqlValidationService(), unmatchedSqlBehavior)
        {
        }

        public SqlMockRouter(
            SqlValidationService validationService,
            UnmatchedSqlBehavior unmatchedSqlBehavior = UnmatchedSqlBehavior.Strict)
        {
            _validationService = validationService;
            _unmatchedSqlBehavior = unmatchedSqlBehavior;
        }

        // 実行順や正規化後 SQL を後から検証できる履歴。
        public IReadOnlyList<SqlInvocation> History => _history;

        // SQL 形状に対する期待値を登録する。
        public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);

            var rule = new SqlMockRule(predicate);
            _rules.Add(rule);
            return new SqlMockSetup(rule);
        }

        public int ExecuteNonQuery(string sql)
        {
            // 更新系は一致 rule の affected rows を返す。
            var invocation = CreateInvocation(DbCallMethod.ExecuteNonQuery, sql);
            var rule = FindRule(invocation);
            return rule.GetAffectedRows(invocation);
        }

        public void ExecuteCommand(string sql)
        {
            var invocation = CreateInvocation(DbCallMethod.Command, sql);
            var rule = FindRuleOrDefault(invocation);

            if (rule is null)
            {
                if (_unmatchedSqlBehavior == UnmatchedSqlBehavior.ValidateOnlyForCommands)
                {
                    return;
                }

                throw UnexpectedSql(invocation);
            }

            rule.Complete(invocation);
        }

        public T Scalar<T>(string sql)
        {
            // scalar は nullable と non-nullable で null の扱いを分ける。
            var invocation = CreateInvocation(DbCallMethod.Scalar, sql);
            var rule = FindRule(invocation);
            var value = rule.GetScalar(invocation, default(T) is null);

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
            // 未使用 rule はテストの期待漏れとして失敗させる。
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
            => FindRuleOrDefault(invocation) ?? throw UnexpectedSql(invocation);

        private SqlMockRule? FindRuleOrDefault(SqlInvocation invocation)
        {
            foreach (var rule in _rules)
            {
                if (rule.Matches(invocation))
                {
                    return rule;
                }
            }

            return null;
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
