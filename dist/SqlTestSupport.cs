using Microsoft.SqlServer.TransactSql.ScriptDom;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections;
using System.Globalization;
using System.Text;

namespace SqlTestSupport
{
    // 独自 Assert クラスから委譲する MSTest 向け facade。
    public static class SqlAssertFacade
    {
        private static readonly SqlValidationService ValidationService = new();

        // 構文検証だけを行い、失敗時は AssertFailedException に変換する。
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

    }

    // 低レベル例外を MSTest の失敗メッセージとして読める形に整える。
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
            builder.AppendLine("方言: SQL Server 2022 / Sql160 / QUOTED_IDENTIFIER ON");
            builder.AppendLine();

            if (exception is SqlSyntaxValidationException syntaxException)
            {
                builder.AppendLine("構文解析エラー:");
                foreach (var diagnostic in syntaxException.Diagnostics)
                {
                    builder
                        .Append("  - ")
                        .Append("行 ").Append(diagnostic.Line)
                        .Append(", 列 ").Append(diagnostic.Column)
                        .Append(", 番号 ").Append(diagnostic.Number)
                        .Append(": ")
                        .AppendLine(diagnostic.Message);
                }

                builder.AppendLine();
            }
            else if (exception is SqlUnsupportedScriptException unsupportedException)
            {
                builder.AppendLine("理由:");
                builder.AppendLine(unsupportedException.Reason);
                builder.AppendLine();
            }

            builder.AppendLine("対象 SQL:");
            builder.AppendLine(exception.Sql);

            return builder.ToString();
        }
    }

    // ScriptDom が返した parse error を保持する構文検証例外。
    public sealed class SqlSyntaxValidationException : SqlValidationException
    {
        public SqlSyntaxValidationException(string sql, IReadOnlyList<SqlParseDiagnostic> diagnostics)
            : base("SQL の構文検証に失敗しました。", sql)
        {
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<SqlParseDiagnostic> Diagnostics { get; }
    }

    // command text として扱わない SQL 入力を明示する例外。
    public sealed class SqlUnsupportedScriptException : SqlValidationException
    {
        public SqlUnsupportedScriptException(string sql, string reason)
            : base($"このテストヘルパーでは扱えない SQL です: {reason}", sql)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }

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

    // ScriptDom parse 結果を保持する。
    public sealed record SqlAnalysisResult(
        string OriginalSql,
        TSqlFragment Fragment);

    // Mock 分岐に使う AST 由来の SQL metadata。
    public sealed record SqlInspectionResult(
        string OriginalSql,
        SqlStatementKind StatementKind,
        IReadOnlySet<string> TargetTables,
        IReadOnlySet<string> ReferencedTables,
        IReadOnlySet<string> JoinedTables,
        IReadOnlySet<string> SelectedColumns,
        IReadOnlySet<string> WhereColumns,
        IReadOnlySet<string> OrderByColumns,
        IReadOnlySet<string> GroupByColumns,
        IReadOnlySet<string> HavingColumns,
        IReadOnlySet<string> HavingFunctions,
        IReadOnlySet<string> ParameterNames);

    // WhenSql predicate に渡す、検証・抽出済みの SQL 呼び出し情報。
    public sealed record SqlInvocation(
        string OriginalSql,
        SqlStatementKind StatementKind,
        IReadOnlySet<string> TargetTables,
        IReadOnlySet<string> ReferencedTables,
        IReadOnlySet<string> JoinedTables,
        IReadOnlySet<string> SelectedColumns,
        IReadOnlySet<string> WhereColumns,
        IReadOnlySet<string> OrderByColumns,
        IReadOnlySet<string> GroupByColumns,
        IReadOnlySet<string> HavingColumns,
        IReadOnlySet<string> HavingFunctions,
        IReadOnlySet<string> ParameterNames,
        int CallIndex)
    {
        // テストコード側で SQL 形状を簡潔に表現する matcher。
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

        public bool JoinsTable(string table)
            => ContainsIdentifier(JoinedTables, table);

        public bool OrdersBy(string column)
            => ContainsIdentifier(OrderByColumns, column);

        public bool GroupsBy(string column)
            => ContainsIdentifier(GroupByColumns, column);

        public bool HavingUses(string column)
            => ContainsIdentifier(HavingColumns, column);

        public bool HavingCalls(string functionName)
            => ContainsIdentifier(HavingFunctions, functionName);

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

    // ScriptDom parse error を assertion message へ渡すための診断情報。
    public sealed record SqlParseDiagnostic(
        int Number,
        int Line,
        int Column,
        int Offset,
        string Message);

    // Mock 分岐で使う先頭 SQL statement の大まかな分類。
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

    public sealed class SqlInspectionService
    {
        public SqlInspectionResult Inspect(SqlAnalysisResult analysis)
        {
            // 元 SQL の AST から Mock 分岐用 metadata を抽出する。
            ArgumentNullException.ThrowIfNull(analysis);

            var visitor = new InspectionVisitor();
            analysis.Fragment.Accept(visitor);

            return new SqlInspectionResult(
                analysis.OriginalSql,
                visitor.GetStatementKind(),
                visitor.TargetTables,
                visitor.ReferencedTables,
                visitor.JoinedTables,
                visitor.SelectedColumns,
                visitor.WhereColumns,
                visitor.OrderByColumns,
                visitor.GroupByColumns,
                visitor.HavingColumns,
                visitor.HavingFunctions,
                visitor.ParameterNames);
        }

        private enum ColumnContext
        {
            None,
            Selected,
            Where,
            OrderBy,
            GroupBy,
            Having
        }

        private sealed class InspectionVisitor : TSqlFragmentVisitor
        {
            private ColumnContext _columnContext;
            private readonly List<SqlStatementKind> _statementKinds = new();

            public IReadOnlySet<string> TargetTables => TargetTablesInternal;

            public IReadOnlySet<string> ReferencedTables => ReferencedTablesInternal;

            public IReadOnlySet<string> JoinedTables => JoinedTablesInternal;

            public IReadOnlySet<string> SelectedColumns => SelectedColumnsInternal;

            public IReadOnlySet<string> WhereColumns => WhereColumnsInternal;

            public IReadOnlySet<string> OrderByColumns => OrderByColumnsInternal;

            public IReadOnlySet<string> GroupByColumns => GroupByColumnsInternal;

            public IReadOnlySet<string> HavingColumns => HavingColumnsInternal;

            public IReadOnlySet<string> HavingFunctions => HavingFunctionsInternal;

            public IReadOnlySet<string> ParameterNames => ParameterNamesInternal;

            private HashSet<string> TargetTablesInternal { get; } = NewIdentifierSet();

            private HashSet<string> ReferencedTablesInternal { get; } = NewIdentifierSet();

            private HashSet<string> JoinedTablesInternal { get; } = NewIdentifierSet();

            private HashSet<string> SelectedColumnsInternal { get; } = NewIdentifierSet();

            private HashSet<string> WhereColumnsInternal { get; } = NewIdentifierSet();

            private HashSet<string> OrderByColumnsInternal { get; } = NewIdentifierSet();

            private HashSet<string> GroupByColumnsInternal { get; } = NewIdentifierSet();

            private HashSet<string> HavingColumnsInternal { get; } = NewIdentifierSet();

            private HashSet<string> HavingFunctionsInternal { get; } = NewIdentifierSet();

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

            public override void ExplicitVisit(QualifiedJoin node)
            {
                AddJoinedTableReference(node.SecondTableReference);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(UnqualifiedJoin node)
            {
                AddJoinedTableReference(node.SecondTableReference);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(QuerySpecification node)
            {
                node.FromClause?.Accept(this);

                WithColumnContext(ColumnContext.Selected, () =>
                {
                    foreach (var selectElement in node.SelectElements)
                    {
                        selectElement.Accept(this);
                    }
                });

                VisitWhereClause(node.WhereClause);

                if (node.GroupByClause is not null)
                {
                    WithColumnContext(ColumnContext.GroupBy, () => node.GroupByClause.Accept(this));
                }

                if (node.HavingClause is not null)
                {
                    WithColumnContext(ColumnContext.Having, () => node.HavingClause.Accept(this));
                }

                if (node.OrderByClause is not null)
                {
                    WithColumnContext(ColumnContext.OrderBy, () => node.OrderByClause.Accept(this));
                }

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

                var shortName = LastIdentifier(node.MultiPartIdentifier);
                switch (_columnContext)
                {
                    case ColumnContext.Selected:
                        AddColumnName(SelectedColumnsInternal, columnName, shortName);
                        break;
                    case ColumnContext.Where:
                        AddColumnName(WhereColumnsInternal, columnName, shortName);
                        break;
                    case ColumnContext.OrderBy:
                        AddColumnName(OrderByColumnsInternal, columnName, shortName);
                        break;
                    case ColumnContext.GroupBy:
                        AddColumnName(GroupByColumnsInternal, columnName, shortName);
                        break;
                    case ColumnContext.Having:
                        AddColumnName(HavingColumnsInternal, columnName, shortName);
                        break;
                }

                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(FunctionCall node)
            {
                if (_columnContext == ColumnContext.Having &&
                    !string.IsNullOrWhiteSpace(node.FunctionName.Value))
                {
                    HavingFunctionsInternal.Add(node.FunctionName.Value);
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

            private void AddJoinedTableReference(TableReference? tableReference)
            {
                if (tableReference is null)
                {
                    return;
                }

                var collector = new JoinedTableCollector(JoinedTablesInternal);
                tableReference.Accept(collector);
            }

            private static void AddTableName(ISet<string> names, SchemaObjectName? schemaObjectName)
            {
                var formatted = FormatSchemaObjectName(schemaObjectName);
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                    names.Add(formatted);
                }
            }

            private static void AddColumnName(ISet<string> names, string formatted, string shortName)
            {
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                    names.Add(formatted);
                }

                if (!string.IsNullOrWhiteSpace(shortName))
                {
                    names.Add(shortName);
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

            private sealed class JoinedTableCollector : TSqlFragmentVisitor
            {
                private readonly ISet<string> _names;

                public JoinedTableCollector(ISet<string> names)
                {
                    _names = names;
                }

                public override void ExplicitVisit(NamedTableReference node)
                {
                    AddTableName(_names, node.SchemaObject);
                }
            }
        }
    }

    public sealed class SqlServer2022SyntaxAnalyzer
    {
        public SqlAnalysisResult Analyze(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new SqlUnsupportedScriptException(sql ?? string.Empty, "SQL 文字列が空です。");
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
                throw new SqlUnsupportedScriptException(sql, "GO による複数 batch は command text として扱わないため未対応です。");
            }

            return new SqlAnalysisResult(sql, fragment);
        }
    }

    public sealed class SqlValidationService
    {
        private readonly SqlServer2022SyntaxAnalyzer _analyzer;
        private readonly SqlInspectionService _inspectionService;

        public SqlValidationService()
            : this(null, null)
        {
        }

        public SqlValidationService(
            SqlServer2022SyntaxAnalyzer? analyzer,
            SqlInspectionService? inspectionService)
        {
            _analyzer = analyzer ?? new SqlServer2022SyntaxAnalyzer();
            _inspectionService = inspectionService ?? new SqlInspectionService();
        }

        // 構文解析だけが必要な呼び出し口。
        public SqlAnalysisResult Analyze(string sql)
            => _analyzer.Analyze(sql);

        // Mock 判定で使う形状情報まで元 SQL の AST から取り出す。
        public SqlInspectionResult Inspect(string sql)
        {
            var analysis = Analyze(sql);
            return _inspectionService.Inspect(analysis);
        }
    }
}
