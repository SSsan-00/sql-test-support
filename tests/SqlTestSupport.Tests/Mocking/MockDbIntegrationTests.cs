using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport.Tests
{
    [TestClass]
    public sealed class MockDbIntegrationTests
    {
        [TestMethod]
        public void Mock_db_can_override_first_sql_argument_methods()
        {
            // 仕様: 本番DBクラスの第一引数 SQL 実行メソッドだけを override すれば Mock 化できる。
            var db = new MockProductionDb();
            db.WhenSql(q => q.IsSelectFrom("dbo.Customers")).ReturnsScalar("Alice");
            db.WhenSql(q => q.IsUpdate("dbo.Customers")).ReturnsAffectedRows(1);

            var name = db.Scalar<string>("SELECT Name FROM dbo.Customers");
            var affectedRows = db.Execute("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            Assert.AreEqual("Alice", name);
            Assert.AreEqual(1, affectedRows);
            db.VerifyAllSqlExpectations();
        }

        private class ProductionDb
        {
            // 導入先の本番DBクラスを最小化した形。
            public virtual int Execute(string sql, object? parameters = null)
                => throw new NotSupportedException(sql);

            public virtual T Scalar<T>(string sql, object? parameters = null)
                => throw new NotSupportedException(sql);
        }

        private sealed class MockProductionDb : ProductionDb
        {
            private readonly SqlMockRouter _router = new();

            // Mock DB は router の薄い facade に留める。
            public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
                => _router.WhenSql(predicate);

            public void VerifyAllSqlExpectations()
                => _router.VerifyAll();

            public override int Execute(string sql, object? parameters = null)
                => _router.ExecuteNonQuery(sql);

            public override T Scalar<T>(string sql, object? parameters = null)
                => _router.Scalar<T>(sql);
        }
    }
}
