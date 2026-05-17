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
            var result = _service.Analyze("""
                SELECT Id, Name
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Fingerprint));
        }

        [TestMethod]
        public void Analyze_rejects_invalid_tsql()
        {
            var exception = Assert.Throws<SqlSyntaxValidationException>(() =>
                _service.Analyze("SELECT FROM WHERE"));

            Assert.IsGreaterThan(0, exception.Diagnostics.Count);
        }

        [TestMethod]
        public void Normalize_returns_sql_only_when_round_trip_fingerprint_matches()
        {
            var result = _service.Normalize("""
                select Id, Name
                from dbo.Customers
                where Id = @Id
                """);

            Assert.AreEqual(result.OriginalFingerprint, result.NormalizedFingerprint);
            Assert.IsTrue(result.NormalizedSql.Contains("SELECT", StringComparison.Ordinal));
            Assert.IsTrue(result.NormalizedSql.Contains("FROM", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Inspect_extracts_select_shape()
        {
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
            var result = _service.Inspect("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            Assert.AreEqual(SqlStatementKind.Update, result.StatementKind);
            CollectionAssert.Contains(result.TargetTables.ToList(), "dbo.Customers");
            CollectionAssert.Contains(result.WhereColumns.ToList(), "Id");
        }
    }
}
