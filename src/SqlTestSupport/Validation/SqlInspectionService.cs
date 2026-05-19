using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlTestSupport
{
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
}
