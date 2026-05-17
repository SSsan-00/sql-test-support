using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlTestSupport
{
    public sealed class SqlInspectionService
    {
        public SqlInspectionResult Inspect(SqlNormalizationResult normalization)
        {
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
}
