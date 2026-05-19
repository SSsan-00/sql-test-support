using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport.Tests
{
    [TestClass]
    public sealed class MockDbIntegrationTests
    {
        [TestMethod]
        public void Mock_db_can_override_first_sql_argument_methods()
        {
            // 本番DBクラスの第一引数 SQL 実行メソッドだけを override すれば Mock 化できる。
            var db = new MockProductionDb();
            db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.SelectsColumn("Name"))
                .ReturnsResult("Alice");

            var name = db.Execute("SELECT Name FROM dbo.Customers");

            Assert.AreEqual("Alice", name);
            db.VerifyAllSqlExpectations();
        }

        [TestMethod]
        public void Mock_db_can_validate_unregistered_object_result_sql_without_mock_behavior()
        {
            // object? 戻り値の未登録 SQL は構文解析と履歴記録だけ行い null を返す。
            var db = new MockProductionDb();

            var value = db.Execute("""
                SELECT MiddleName
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.IsNull(value);
            Assert.HasCount(1, db.History);
            Assert.IsTrue(db.History[0].WhereUses("Id"));
        }

        [TestMethod]
        public void Mock_db_returns_empty_collection_for_unregistered_dictionary_result()
        {
            // 独自コレクション戻り値の未登録 SQL は空の new() を返す。
            var db = new MockProductionDb();

            var rows = db.QueryRows("""
                SELECT Id, Name
                FROM dbo.Customers
                """);

            Assert.IsEmpty(rows);
            Assert.HasCount(1, db.History);
        }

        [TestMethod]
        public void Mock_db_can_branch_get_value_by_constructed_sql_metadata()
        {
            // get_value のように内部で SQL を組み立てるメソッドも、組み立て後 SQL を解析して分岐できる。
            var db = new MockProductionDb();
            db.WhenSql(q =>
                    q.IsSelectFrom("dbo.Customers") &&
                    q.SelectsColumn("Name") &&
                    q.WhereUses("Id"))
                .ReturnsResult("Alice");

            var name = db.get_value("Name", "dbo.Customers", "Id = @Id");

            Assert.AreEqual("Alice", name);
            db.VerifyAllSqlExpectations();
        }

        private class ProductionDb
        {
            // 導入先の本番DBクラスを最小化した形。
            public virtual object? Execute(string sql, object? parameters = null)
                => throw new NotSupportedException(sql);

            public virtual CustomerRows QueryRows(string sql, object? parameters = null)
                => throw new NotSupportedException(sql);

            public virtual object? get_value(string columns, string table, string where)
            {
                var sql = $"SELECT {columns} FROM {table} WHERE {where}";
                return Execute(sql);
            }
        }

        private sealed class MockProductionDb : ProductionDb
        {
            private readonly SqlMockRouter _router = new();

            // Mock DB は router の薄い facade に留める。
            public IReadOnlyList<SqlInvocation> History => _router.History;

            public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
                => _router.WhenSql(predicate);

            public void VerifyAllSqlExpectations()
                => _router.VerifyAll();

            public override object? Execute(string sql, object? parameters = null)
                => _router.ExecuteResult<object?>(sql);

            public override CustomerRows QueryRows(string sql, object? parameters = null)
                => _router.ExecuteResult<CustomerRows>(sql);
        }

        private sealed class CustomerRows : Dictionary<string, object?>
        {
        }
    }
}
