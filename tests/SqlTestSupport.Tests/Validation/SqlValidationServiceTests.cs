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
            // 仕様: SQL Server 2022 として valid な SQL は fingerprint 付きで解析できる。
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
            // 仕様: parse error は構造化された diagnostic を持つ例外になる。
            var exception = Assert.Throws<SqlSyntaxValidationException>(() =>
                _service.Analyze("SELECT FROM WHERE"));

            Assert.IsGreaterThan(0, exception.Diagnostics.Count);
        }

        [TestMethod]
        public void Normalize_returns_sql_only_when_round_trip_fingerprint_matches()
        {
            // 仕様: 正規化後 SQL は再 parse され、元 SQL と同じ fingerprint の場合だけ返る。
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
            // 仕様: SELECT の参照 table、選択列、WHERE 列、parameter を抽出する。
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
            // 仕様: UPDATE は更新対象 table と WHERE 列を Mock 分岐に使える。
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
