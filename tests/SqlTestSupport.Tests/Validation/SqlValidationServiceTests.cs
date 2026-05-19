using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport.Tests
{
    [TestClass]
    public sealed class SqlValidationServiceTests
    {
        private readonly SqlValidationService _service = new();

        [TestMethod]
        public void Analyze_accepts_valid_sql_server_2022_tsql()
        {
            // SQL Server 2022 として valid な SQL は ScriptDom AST として解析できる。
            var result = _service.Analyze("""
                SELECT Id, Name
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsNotNull(result.Fragment);
            Assert.IsTrue(result.OriginalSql.Contains("dbo.Customers", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Analyze_rejects_invalid_tsql()
        {
            // parse error は構造化された diagnostic を持つ例外になる。
            var exception = Assert.Throws<SqlSyntaxValidationException>(() =>
                _service.Analyze("SELECT FROM WHERE"));

            Assert.IsGreaterThan(0, exception.Diagnostics.Count);
        }

        [TestMethod]
        public void Inspect_extracts_select_shape()
        {
            // SELECT の参照 table、選択列、WHERE 列、parameter を抽出する。
            var result = _service.Inspect("""
                SELECT Id, Name
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.AreEqual(SqlStatementKind.Select, result.StatementKind);
            CollectionAssert.Contains(result.ReferencedTables.ToList(), "dbo.Customers");
            CollectionAssert.Contains(result.SelectedColumns.ToList(), "Id");
            CollectionAssert.Contains(result.SelectedColumns.ToList(), "Name");
            CollectionAssert.Contains(result.WhereColumns.ToList(), "Id");
            CollectionAssert.Contains(result.ParameterNames.ToList(), "@Id");
        }

        [TestMethod]
        public void Inspect_extracts_update_target_and_where_columns()
        {
            // UPDATE は更新対象 table と WHERE 列を Mock 分岐に使える。
            var result = _service.Inspect("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            Assert.AreEqual(SqlStatementKind.Update, result.StatementKind);
            CollectionAssert.Contains(result.TargetTables.ToList(), "dbo.Customers");
            CollectionAssert.Contains(result.WhereColumns.ToList(), "Id");
        }

        [TestMethod]
        public void Inspect_extracts_join_order_group_and_having_metadata()
        {
            // JOIN / ORDER BY / GROUP BY / HAVING も SQL 形状の分岐条件に使える。
            var result = _service.Inspect("""
                SELECT c.Id, COUNT(o.Id) AS OrderCount
                FROM dbo.Customers AS c
                INNER JOIN dbo.Orders AS o ON o.CustomerId = c.Id
                WHERE c.IsActive = @IsActive
                GROUP BY c.Id
                HAVING COUNT(o.Id) > 0
                ORDER BY c.Id
                """);

            CollectionAssert.Contains(result.ReferencedTables.ToList(), "dbo.Customers");
            CollectionAssert.Contains(result.JoinedTables.ToList(), "dbo.Orders");
            CollectionAssert.Contains(result.OrderByColumns.ToList(), "Id");
            CollectionAssert.Contains(result.GroupByColumns.ToList(), "Id");
            CollectionAssert.Contains(result.HavingColumns.ToList(), "Id");
            CollectionAssert.Contains(result.HavingFunctions.ToList(), "COUNT");
        }
    }
}
