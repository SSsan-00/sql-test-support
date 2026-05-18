using Microsoft.SqlServer.TransactSql.ScriptDom;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace SqlTestSupport
{
    public static class SqlAssertFacade
    {
        private static readonly SqlValidationService ValidationService = new();

        public static void IsValidSql(string sql, string? message = null)
        {
            try
            {
                ValidationService.Analyze(sql);
            }
            catch (SqlValidationException exception)
            {
                throw new AssertFailedException(
                    SqlAssertMessageBuilder.Build(message, exception),
                    exception);
            }
        }

        public static string NormalizeSql(string sql, string? message = null)
        {
            try
            {
                return ValidationService.Normalize(sql).NormalizedSql;
            }
            catch (SqlValidationException exception)
            {
                throw new AssertFailedException(
                    SqlAssertMessageBuilder.Build(message, exception),
                    exception);
            }
        }
    }

    public static class SqlAssertMessageBuilder
    {
        public static string Build(string? userMessage, SqlValidationException exception)
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(userMessage))
            {
                builder.AppendLine(userMessage);
                builder.AppendLine();
            }

            builder.AppendLine(exception.Message);
            builder.AppendLine("Dialect: SQL Server 2022 / Sql160 / QUOTED_IDENTIFIER ON");
            builder.AppendLine();

            if (exception is SqlSyntaxValidationException syntaxException)
            {
                builder.AppendLine("Parse errors:");
                foreach (var diagnostic in syntaxException.Diagnostics)
                {
                    builder
                        .Append("  - ")
                        .Append("Line ").Append(diagnostic.Line)
                        .Append(", Column ").Append(diagnostic.Column)
                        .Append(", Number ").Append(diagnostic.Number)
                        .Append(": ")
                        .AppendLine(diagnostic.Message);
                }

                builder.AppendLine();
            }
            else if (exception is SqlNormalizationChangedAstException changedAstException)
            {
                builder.AppendLine("Original fingerprint:");
                builder.AppendLine(changedAstException.OriginalFingerprint);
                builder.AppendLine();
                builder.AppendLine("Normalized fingerprint:");
                builder.AppendLine(changedAstException.NormalizedFingerprint);
                builder.AppendLine();
                builder.AppendLine("Normalized SQL:");
                builder.AppendLine(changedAstException.NormalizedSql);
                builder.AppendLine();
            }
            else if (exception is SqlUnsupportedScriptException unsupportedException)
            {
                builder.AppendLine("Reason:");
                builder.AppendLine(unsupportedException.Reason);
                builder.AppendLine();
            }

            builder.AppendLine("SQL:");
            builder.AppendLine(exception.Sql);

            return builder.ToString();
        }
    }

    public sealed class SqlNormalizationChangedAstException : SqlValidationException
    {
        public SqlNormalizationChangedAstException(
            string originalSql,
            string normalizedSql,
            string originalFingerprint,
            string normalizedFingerprint)
            : base("SQL normalization changed the parsed AST fingerprint.", originalSql)
        {
            NormalizedSql = normalizedSql;
            OriginalFingerprint = originalFingerprint;
            NormalizedFingerprint = normalizedFingerprint;
        }

        public string NormalizedSql { get; }

        public string OriginalFingerprint { get; }

        public string NormalizedFingerprint { get; }
    }

    public sealed class SqlSyntaxValidationException : SqlValidationException
    {
        public SqlSyntaxValidationException(string sql, IReadOnlyList<SqlParseDiagnostic> diagnostics)
            : base("SQL syntax validation failed.", sql)
        {
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<SqlParseDiagnostic> Diagnostics { get; }
    }

    public sealed class SqlUnsupportedScriptException : SqlValidationException
    {
        public SqlUnsupportedScriptException(string sql, string reason)
            : base($"SQL script is not supported by this test helper: {reason}", sql)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }

    public class SqlValidationException : Exception
    {
        public SqlValidationException(string message, string sql, Exception? innerException = null)
            : base(message, innerException)
        {
            Sql = sql;
        }

        public string Sql { get; }
    }

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

    internal enum SqlMockReturnKind
    {
        None = 0,
        AffectedRows,
        Scalar,
        Complete
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

        public void SetCompletes()
        {
            ReturnKind = SqlMockReturnKind.Complete;
            _isSequence = false;
            _singleReturn = null;
            _returns.Clear();
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

        public object? GetScalar(SqlInvocation invocation, bool allowUnconfiguredNull)
        {
            if (ReturnKind == SqlMockReturnKind.None && allowUnconfiguredNull)
            {
                CallCount++;
                return null;
            }

            if (ReturnKind != SqlMockReturnKind.Scalar)
            {
                throw new AssertFailedException("Matched SQL rule does not return a scalar value.");
            }

            return NextReturn(invocation);
        }

        public void Complete(SqlInvocation invocation)
        {
            if (ReturnKind != SqlMockReturnKind.Complete)
            {
                throw new AssertFailedException("Matched SQL rule is not configured to complete a void command.");
            }

            CallCount++;
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
                // sequence は期待回数も表す。余分な呼び出しは失敗。
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

    public enum DbCallMethod
    {
        ExecuteNonQuery = 0,
        Scalar,
        Command
    }

    public sealed record SqlAnalysisResult(
        string OriginalSql,
        TSqlFragment Fragment,
        string Fingerprint);

    public sealed record SqlInspectionResult(
        string OriginalSql,
        string NormalizedSql,
        string Fingerprint,
        SqlStatementKind StatementKind,
        IReadOnlySet<string> TargetTables,
        IReadOnlySet<string> ReferencedTables,
        IReadOnlySet<string> SelectedColumns,
        IReadOnlySet<string> WhereColumns,
        IReadOnlySet<string> ParameterNames);

    public sealed record SqlInvocation(
        DbCallMethod Method,
        string OriginalSql,
        string NormalizedSql,
        string Fingerprint,
        SqlStatementKind StatementKind,
        IReadOnlySet<string> TargetTables,
        IReadOnlySet<string> ReferencedTables,
        IReadOnlySet<string> SelectedColumns,
        IReadOnlySet<string> WhereColumns,
        IReadOnlySet<string> ParameterNames,
        int GlobalCallIndex,
        int MethodCallIndex)
    {
        public bool IsSelectFrom(string table)
            => StatementKind == SqlStatementKind.Select && ReferencesTable(table);

        public bool IsInsertInto(string table)
            => StatementKind == SqlStatementKind.Insert && TargetsTable(table);

        public bool IsUpdate(string table)
            => StatementKind == SqlStatementKind.Update && TargetsTable(table);

        public bool IsDeleteFrom(string table)
            => StatementKind == SqlStatementKind.Delete && TargetsTable(table);

        public bool WhereUses(string column)
            => ContainsIdentifier(WhereColumns, column);

        public bool SelectsColumn(string column)
            => ContainsIdentifier(SelectedColumns, column);

        public bool ReferencesTable(string table)
            => ContainsIdentifier(ReferencedTables, table);

        public bool TargetsTable(string table)
            => ContainsIdentifier(TargetTables, table);

        public bool HasParameter(string parameterName)
            => ContainsIdentifier(ParameterNames, parameterName);

        private static bool ContainsIdentifier(IReadOnlySet<string> values, string expected)
            // schema 付き・schema なしの両方を軽く許容。
            => values.Any(value =>
                StringComparer.OrdinalIgnoreCase.Equals(value, expected) ||
                StringComparer.OrdinalIgnoreCase.Equals(LastPart(value), expected) ||
                StringComparer.OrdinalIgnoreCase.Equals(value, LastPart(expected)));

        private static string LastPart(string value)
        {
            var index = value.LastIndexOf('.');
            return index < 0 ? value : value[(index + 1)..];
        }
    }

    public sealed record SqlNormalizationResult(
        string OriginalSql,
        string NormalizedSql,
        string OriginalFingerprint,
        string NormalizedFingerprint,
        SqlAnalysisResult OriginalAnalysis,
        SqlAnalysisResult NormalizedAnalysis);

    public sealed record SqlParseDiagnostic(
        int Number,
        int Line,
        int Column,
        int Offset,
        string Message);

    public enum SqlStatementKind
    {
        Unknown = 0,
        Select,
        Insert,
        Update,
        Delete,
        Merge,
        Execute,
        Multiple
    }

    public enum UnmatchedSqlBehavior
    {
        Strict = 0,
        ValidateOnlyForCommands
    }

    public sealed class SqlAstFingerprinter
    {
        // 位置情報と token stream は整形差分で変わるため fingerprint から除外。
        private static readonly HashSet<string> ExcludedProperties = new(StringComparer.Ordinal)
        {
            nameof(TSqlFragment.StartLine),
            nameof(TSqlFragment.StartColumn),
            nameof(TSqlFragment.StartOffset),
            nameof(TSqlFragment.FragmentLength),
            nameof(TSqlFragment.FirstTokenIndex),
            nameof(TSqlFragment.LastTokenIndex),
            nameof(TSqlFragment.ScriptTokenStream)
        };

        private static readonly ConcurrentDictionary<Type, IReadOnlyList<PropertyInfo>> FingerprintPropertiesCache = new();

        public string CreateFingerprint(TSqlFragment fragment)
        {
            ArgumentNullException.ThrowIfNull(fragment);

            var builder = new StringBuilder();
            WriteValue(builder, fragment, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
            return Convert.ToHexString(bytes);
        }

        private static void WriteValue(
            StringBuilder builder,
            object? value,
            ISet<object> visited,
            int depth)
        {
            if (value is null)
            {
                builder.Append("<null>");
                return;
            }

            if (depth > 128)
            {
                builder.Append("<max-depth>");
                return;
            }

            switch (value)
            {
                case string text:
                    builder.Append('"').Append(text).Append('"');
                    return;
                case char character:
                    builder.Append('\'').Append(character).Append('\'');
                    return;
                case bool boolean:
                    builder.Append(boolean ? "true" : "false");
                    return;
                case Enum enumValue:
                    builder.Append(value.GetType().FullName).Append('.').Append(enumValue);
                    return;
                case IFormattable formattable when IsScalar(value.GetType()):
                    builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                    return;
            }

            if (value is TSqlParserToken)
            {
                // コメント・空白・元 token への依存を避ける。
                builder.Append("<token>");
                return;
            }

            if (!value.GetType().IsValueType && !visited.Add(value))
            {
                builder.Append("<cycle>");
                return;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                builder.Append('[');
                var index = 0;
                foreach (var item in enumerable)
                {
                    if (index++ > 0)
                    {
                        builder.Append(',');
                    }

                    WriteValue(builder, item, visited, depth + 1);
                }

                builder.Append(']');
                return;
            }

            var type = value.GetType();
            builder.Append(type.FullName).Append('{');

            foreach (var property in GetFingerprintProperties(type))
            {
                builder.Append(property.Name).Append('=');
                WriteValue(builder, property.GetValue(value), visited, depth + 1);
                builder.Append(';');
            }

            builder.Append('}');
        }

        private static IReadOnlyList<PropertyInfo> GetFingerprintProperties(Type type)
            => FingerprintPropertiesCache.GetOrAdd(
                type,
                static key => key.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(property =>
                        property.GetMethod is not null &&
                        property.GetIndexParameters().Length == 0 &&
                        !ExcludedProperties.Contains(property.Name))
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToArray());

        private static bool IsScalar(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying.IsPrimitive ||
                   underlying == typeof(decimal) ||
                   underlying == typeof(DateTime) ||
                   underlying == typeof(DateTimeOffset) ||
                   underlying == typeof(Guid);
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public new bool Equals(object? x, object? y)
                => ReferenceEquals(x, y);

            public int GetHashCode(object obj)
                => RuntimeHelpers.GetHashCode(obj);
        }
    }

    public sealed class SqlInspectionService
    {
        public SqlInspectionResult Inspect(SqlNormalizationResult normalization)
        {
            // 正規化済み SQL からテーブル・列・パラメータを抽出する。
            ArgumentNullException.ThrowIfNull(normalization);

            var visitor = new InspectionVisitor();
            normalization.OriginalAnalysis.Fragment.Accept(visitor);

            return new SqlInspectionResult(
                normalization.OriginalSql,
                normalization.NormalizedSql,
                normalization.OriginalFingerprint,
                visitor.GetStatementKind(),
                visitor.TargetTables,
                visitor.ReferencedTables,
                visitor.SelectedColumns,
                visitor.WhereColumns,
                visitor.ParameterNames);
        }

        private enum ColumnContext
        {
            None,
            Selected,
            Where
        }

        private sealed class InspectionVisitor : TSqlFragmentVisitor
        {
            private ColumnContext _columnContext;
            private readonly List<SqlStatementKind> _statementKinds = new();

            public IReadOnlySet<string> TargetTables => TargetTablesInternal;

            public IReadOnlySet<string> ReferencedTables => ReferencedTablesInternal;

            public IReadOnlySet<string> SelectedColumns => SelectedColumnsInternal;

            public IReadOnlySet<string> WhereColumns => WhereColumnsInternal;

            public IReadOnlySet<string> ParameterNames => ParameterNamesInternal;

            private HashSet<string> TargetTablesInternal { get; } = NewIdentifierSet();

            private HashSet<string> ReferencedTablesInternal { get; } = NewIdentifierSet();

            private HashSet<string> SelectedColumnsInternal { get; } = NewIdentifierSet();

            private HashSet<string> WhereColumnsInternal { get; } = NewIdentifierSet();

            private HashSet<string> ParameterNamesInternal { get; } = NewIdentifierSet();

            public SqlStatementKind GetStatementKind()
            {
                // 複数種類の文が混じる場合は Multiple として扱う。
                var distinct = _statementKinds.Distinct().ToArray();
                return distinct.Length switch
                {
                    0 => SqlStatementKind.Unknown,
                    1 => distinct[0],
                    _ => SqlStatementKind.Multiple
                };
            }

            public override void ExplicitVisit(SelectStatement node)
            {
                _statementKinds.Add(SqlStatementKind.Select);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(InsertStatement node)
            {
                _statementKinds.Add(SqlStatementKind.Insert);
                node.InsertSpecification?.Accept(this);
            }

            public override void ExplicitVisit(UpdateStatement node)
            {
                _statementKinds.Add(SqlStatementKind.Update);
                node.UpdateSpecification?.Accept(this);
            }

            public override void ExplicitVisit(DeleteStatement node)
            {
                _statementKinds.Add(SqlStatementKind.Delete);
                node.DeleteSpecification?.Accept(this);
            }

            public override void ExplicitVisit(MergeStatement node)
            {
                _statementKinds.Add(SqlStatementKind.Merge);
                node.MergeSpecification?.Accept(this);
            }

            public override void ExplicitVisit(ExecuteStatement node)
            {
                _statementKinds.Add(SqlStatementKind.Execute);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(InsertSpecification node)
            {
                AddTargetTable(node.Target);
                foreach (var column in node.Columns)
                {
                    column.Accept(this);
                }

                node.InsertSource?.Accept(this);
                node.OutputClause?.Accept(this);
                node.OutputIntoClause?.Accept(this);
            }

            public override void ExplicitVisit(UpdateSpecification node)
            {
                AddTargetTable(node.Target);
                node.FromClause?.Accept(this);
                node.TopRowFilter?.Accept(this);
                foreach (var setClause in node.SetClauses)
                {
                    setClause.Accept(this);
                }

                VisitWhereClause(node.WhereClause);
                node.OutputClause?.Accept(this);
                node.OutputIntoClause?.Accept(this);
            }

            public override void ExplicitVisit(DeleteSpecification node)
            {
                AddTargetTable(node.Target);
                node.FromClause?.Accept(this);
                node.TopRowFilter?.Accept(this);
                VisitWhereClause(node.WhereClause);
                node.OutputClause?.Accept(this);
                node.OutputIntoClause?.Accept(this);
            }

            public override void ExplicitVisit(MergeSpecification node)
            {
                AddTargetTable(node.Target);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(NamedTableReference node)
            {
                AddTableName(ReferencedTablesInternal, node.SchemaObject);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(QuerySpecification node)
            {
                node.FromClause?.Accept(this);

                // SELECT 句と WHERE 句だけ列用途を分けて収集。
                WithColumnContext(ColumnContext.Selected, () =>
                {
                    foreach (var selectElement in node.SelectElements)
                    {
                        selectElement.Accept(this);
                    }
                });

                if (node.WhereClause?.SearchCondition is not null)
                {
                    WithColumnContext(ColumnContext.Where, () => node.WhereClause.SearchCondition.Accept(this));
                }

                node.GroupByClause?.Accept(this);
                node.HavingClause?.Accept(this);
                node.OrderByClause?.Accept(this);
                node.TopRowFilter?.Accept(this);
            }

            public override void ExplicitVisit(SelectStarExpression node)
            {
                SelectedColumnsInternal.Add("*");
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                var columnName = FormatMultiPartIdentifier(node.MultiPartIdentifier);
                if (string.IsNullOrWhiteSpace(columnName))
                {
                    base.ExplicitVisit(node);
                    return;
                }

                if (_columnContext == ColumnContext.Selected)
                {
                    SelectedColumnsInternal.Add(columnName);
                }
                else if (_columnContext == ColumnContext.Where)
                {
                    WhereColumnsInternal.Add(columnName);
                    var shortName = LastIdentifier(node.MultiPartIdentifier);
                    if (!string.IsNullOrWhiteSpace(shortName))
                    {
                        WhereColumnsInternal.Add(shortName);
                    }
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(VariableReference node)
            {
                if (!string.IsNullOrWhiteSpace(node.Name))
                {
                    ParameterNamesInternal.Add(node.Name);
                }

                base.ExplicitVisit(node);
            }

            private void WithColumnContext(ColumnContext context, Action action)
            {
                var previous = _columnContext;
                _columnContext = context;
                try
                {
                    action();
                }
                finally
                {
                    _columnContext = previous;
                }
            }

            private void VisitWhereClause(WhereClause? whereClause)
            {
                if (whereClause?.SearchCondition is null)
                {
                    return;
                }

                WithColumnContext(ColumnContext.Where, () => whereClause.SearchCondition.Accept(this));
            }

            private void AddTargetTable(TableReference? tableReference)
            {
                // 初期版は NamedTableReference のみ対象。派生 table source は参照側で扱う。
                if (tableReference is NamedTableReference namedTableReference)
                {
                    AddTableName(TargetTablesInternal, namedTableReference.SchemaObject);
                }
            }

            private static void AddTableName(ISet<string> names, SchemaObjectName? schemaObjectName)
            {
                var formatted = FormatSchemaObjectName(schemaObjectName);
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                    names.Add(formatted);
                }
            }

            // SQL の識別子比較では大文字小文字差を無視する。
            private static HashSet<string> NewIdentifierSet()
                => new(StringComparer.OrdinalIgnoreCase);

            private static string FormatSchemaObjectName(SchemaObjectName? name)
            {
                if (name is null)
                {
                    return string.Empty;
                }

                return string.Join(".", name.Identifiers.Select(identifier => identifier.Value));
            }

            private static string FormatMultiPartIdentifier(MultiPartIdentifier? name)
            {
                if (name is null)
                {
                    return string.Empty;
                }

                return string.Join(".", name.Identifiers.Select(identifier => identifier.Value));
            }

            private static string LastIdentifier(MultiPartIdentifier? name)
                => name?.Identifiers.LastOrDefault()?.Value ?? string.Empty;
        }
    }

    public sealed class SqlServer2022Normalizer
    {
        private readonly SqlServer2022SyntaxAnalyzer _analyzer;

        public SqlServer2022Normalizer(SqlServer2022SyntaxAnalyzer? analyzer = null)
        {
            _analyzer = analyzer ?? new SqlServer2022SyntaxAnalyzer();
        }

        public SqlNormalizationResult Normalize(string sql)
        {
            var original = _analyzer.Analyze(sql);
            var normalizedSql = Generate(original.Fragment);
            var normalized = _analyzer.Analyze(normalizedSql);

            // 正規化は fail-closed。AST 構造が変わる疑いがあれば返さない。
            if (!StringComparer.Ordinal.Equals(original.Fingerprint, normalized.Fingerprint))
            {
                throw new SqlNormalizationChangedAstException(
                    original.OriginalSql,
                    normalizedSql,
                    original.Fingerprint,
                    normalized.Fingerprint);
            }

            return new SqlNormalizationResult(
                original.OriginalSql,
                normalizedSql,
                original.Fingerprint,
                normalized.Fingerprint,
                original,
                normalized);
        }

        private static string Generate(TSqlFragment fragment)
        {
            var options = new SqlScriptGeneratorOptions
            {
                SqlVersion = SqlVersion.Sql160,
                SqlEngineType = SqlEngineType.Standalone,
                IncludeSemicolons = true,
                KeywordCasing = KeywordCasing.Uppercase,
                IndentationSize = 4,
                NewLineBeforeFromClause = true,
                NewLineBeforeWhereClause = true,
                NewLineBeforeOrderByClause = true,
                NewLineBeforeGroupByClause = true,
                NewLineBeforeHavingClause = true,
                NewLineBeforeJoinClause = true
            };

            var generator = new Sql160ScriptGenerator(options);
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            generator.GenerateScript(fragment, writer);
            return writer.ToString();
        }
    }

    public sealed class SqlServer2022SyntaxAnalyzer
    {
        private readonly SqlAstFingerprinter _fingerprinter;

        public SqlServer2022SyntaxAnalyzer(SqlAstFingerprinter? fingerprinter = null)
        {
            _fingerprinter = fingerprinter ?? new SqlAstFingerprinter();
        }

        public SqlAnalysisResult Analyze(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new SqlUnsupportedScriptException(sql ?? string.Empty, "SQL text must not be empty.");
            }

            // SQL Server 2022 固定。QUOTED_IDENTIFIER は ON 相当。
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(sql);
            var fragment = parser.Parse(reader, out var parseErrors);
            var diagnostics = parseErrors
                .Select(error => new SqlParseDiagnostic(
                    error.Number,
                    error.Line,
                    error.Column,
                    error.Offset,
                    error.Message))
                .ToArray();

            if (diagnostics.Length > 0)
            {
                throw new SqlSyntaxValidationException(sql, diagnostics);
            }

            if (fragment is TSqlScript script && script.Batches.Count > 1)
            {
                // command text 実行では GO を扱わない。
                throw new SqlUnsupportedScriptException(sql, "GO batch separators are not supported for command-text execution.");
            }

            return new SqlAnalysisResult(sql, fragment, _fingerprinter.CreateFingerprint(fragment));
        }
    }

    public sealed class SqlValidationService
    {
        private readonly SqlServer2022SyntaxAnalyzer _analyzer;
        private readonly SqlServer2022Normalizer _normalizer;
        private readonly SqlInspectionService _inspectionService;

        public SqlValidationService()
            : this(null, null, null)
        {
        }

        public SqlValidationService(
            SqlServer2022SyntaxAnalyzer? analyzer,
            SqlServer2022Normalizer? normalizer,
            SqlInspectionService? inspectionService)
        {
            _analyzer = analyzer ?? new SqlServer2022SyntaxAnalyzer();
            _normalizer = normalizer ?? new SqlServer2022Normalizer(_analyzer);
            _inspectionService = inspectionService ?? new SqlInspectionService();
        }

        // 構文解析だけが必要な呼び出し口。
        public SqlAnalysisResult Analyze(string sql)
            => _analyzer.Analyze(sql);

        // 表記ゆれを揃え、AST が変わらないことも確認する。
        public SqlNormalizationResult Normalize(string sql)
            => _normalizer.Normalize(sql);

        // Mock 判定で使う形状情報まで一度に取り出す。
        public SqlInspectionResult Inspect(string sql)
        {
            var normalized = Normalize(sql);
            return _inspectionService.Inspect(normalized);
        }
    }
}
