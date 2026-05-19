using System.Collections;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport
{
    public sealed class SqlMockRouter
    {
        private readonly SqlValidationService _validationService;
        private readonly List<SqlMockRule> _rules = new();
        private readonly List<SqlInvocation> _history = new();
        private int _callCount;

        public SqlMockRouter()
            : this(new SqlValidationService())
        {
        }

        public SqlMockRouter(SqlValidationService validationService)
        {
            _validationService = validationService;
        }

        // 実行順や SQL 形状を後から検証できる履歴。
        public IReadOnlyList<SqlInvocation> History => _history;

        // SQL 形状に対する期待値を登録する。
        public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);

            var rule = new SqlMockRule(predicate);
            _rules.Add(rule);
            return new SqlMockSetup(rule);
        }

        public T ExecuteResult<T>(string sql)
        {
            var invocation = CreateInvocation(sql);
            var rule = FindRuleOrDefault(invocation);
            var value = rule is null
                ? CreateDefaultResult<T>(invocation)
                : rule.GetResult(invocation, () => CreateDefaultResult<T>(invocation));

            return ConvertResult<T>(value);
        }

        public void VerifyAll()
        {
            // 未使用 rule はテストの期待漏れとして失敗させる。
            var failures = _rules
                .Where(rule => rule.CallCount == 0)
                .Select((_, index) => $"SQL mock rule #{index + 1} は呼び出されていません。")
                .ToArray();

            if (failures.Length == 0)
            {
                return;
            }

            throw new AssertFailedException(string.Join(Environment.NewLine, failures));
        }

        private SqlInvocation CreateInvocation(string sql)
        {
            SqlInspectionResult inspection;
            try
            {
                // すべての Mock 実行 SQL は rule matching 前に検証・解析。
                inspection = _validationService.Inspect(sql);
            }
            catch (SqlValidationException exception)
            {
                throw new AssertFailedException(
                    SqlAssertMessageBuilder.Build("SQL mock が不正な SQL を受け取りました。", exception),
                    exception);
            }

            var invocation = new SqlInvocation(
                inspection.OriginalSql,
                inspection.StatementKind,
                inspection.TargetTables,
                inspection.ReferencedTables,
                inspection.JoinedTables,
                inspection.SelectedColumns,
                inspection.WhereColumns,
                inspection.OrderByColumns,
                inspection.GroupByColumns,
                inspection.HavingColumns,
                inspection.HavingFunctions,
                inspection.ParameterNames,
                _callCount++);

            _history.Add(invocation);
            return invocation;
        }

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

        private static object? CreateDefaultResult<T>(SqlInvocation invocation)
        {
            var returnType = typeof(T);
            if (TryCreateEmptyDictionaryLike(returnType, out var dictionaryResult))
            {
                return dictionaryResult;
            }

            if (default(T) is null)
            {
                return default(T);
            }

            throw UnexpectedSql<T>(invocation);
        }

        private static bool TryCreateEmptyDictionaryLike(Type type, out object? result)
        {
            result = null;
            var isDictionaryLike =
                typeof(IDictionary).IsAssignableFrom(type) ||
                type.GetInterfaces().Any(IsGenericDictionaryInterface);

            if (!isDictionaryLike || type.IsInterface || type.IsAbstract)
            {
                return false;
            }

            var constructor = type.GetConstructor(Type.EmptyTypes);
            if (constructor is null)
            {
                return false;
            }

            result = Activator.CreateInstance(type);
            return true;
        }

        private static bool IsGenericDictionaryInterface(Type type)
            => type.IsGenericType &&
               type.GetGenericTypeDefinition() == typeof(IDictionary<,>);

        private static T ConvertResult<T>(object? value)
        {
            if (value is null)
            {
                if (default(T) is null)
                {
                    return default!;
                }

                throw new AssertFailedException(
                    $"SQL mock の戻り値が null です。戻り値型 {typeof(T).FullName} は null を受け取れません。");
            }

            if (value is T typed)
            {
                return typed;
            }

            try
            {
                var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                var converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                return (T)converted;
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException)
            {
                throw new AssertFailedException($"""
                    SQL mock の戻り値型を変換できません。

                    期待型:
                    {typeof(T).FullName}

                    実際型:
                    {value.GetType().FullName}
                    """, exception);
            }
        }

        private static AssertFailedException UnexpectedSql<T>(SqlInvocation invocation)
        {
            var message = $"""
                未登録 SQL の戻り値を決定できません。

                戻り値型:
                {typeof(T).FullName}

                Statement kind:
                {invocation.StatementKind}

                対象 SQL:
                {invocation.OriginalSql}
                """;

            return new AssertFailedException(message);
        }
    }
}
