using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport.Tests
{
    [TestClass]
    public sealed class SqlMockRouterTests
    {
        [TestMethod]
        public void Scalar_returns_registered_value_for_matching_select()
        {
            // 仕様: SELECT 形状に一致した rule は scalar 戻り値を返す。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
                .ReturnsScalar("Alice");

            var value = router.Scalar<string>("""
                SELECT Name
                FROM dbo.Customers
                WHERE Id = @Id
                """);

            Assert.AreEqual("Alice", value);
            router.VerifyAll();
        }

        [TestMethod]
        public void ExecuteNonQuery_returns_registered_affected_rows_for_matching_update()
        {
            // 仕様: UPDATE 形状に一致した rule は affected rows を返す。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsUpdate("dbo.Customers") && q.WhereUses("Id"))
                .ReturnsAffectedRows(1);

            var affectedRows = router.ExecuteNonQuery("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            Assert.AreEqual(1, affectedRows);
            router.VerifyAll();
        }

        [TestMethod]
        public void ExecuteCommand_completes_for_matching_void_command()
        {
            // 仕様: 戻り値なし実行は Completes rule に一致した場合だけ成功する。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsUpdate("dbo.Customers") && q.WhereUses("Id"))
                .Completes();

            router.ExecuteCommand("""
                UPDATE dbo.Customers
                SET Name = @Name
                WHERE Id = @Id
                """);

            router.VerifyAll();
        }

        [TestMethod]
        public void ExecuteCommand_rejects_rule_that_returns_affected_rows()
        {
            // 仕様: void 実行には Completes rule を明示し、affected rows rule は流用しない。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsUpdate("dbo.Customers"))
                .ReturnsAffectedRows(1);

            Assert.Throws<AssertFailedException>(() =>
                router.ExecuteCommand("""
                    UPDATE dbo.Customers
                    SET Name = @Name
                    WHERE Id = @Id
                    """));
        }

        [TestMethod]
        public void Router_rejects_unregistered_sql()
        {
            // 仕様: strict mode。valid でも未登録 SQL は失敗する。
            var router = new SqlMockRouter();

            Assert.Throws<AssertFailedException>(() =>
                router.Scalar<int>("""
                    SELECT COUNT(1)
                    FROM dbo.Customers
                    """));
        }

        [TestMethod]
        public void Router_rejects_invalid_sql_before_matching()
        {
            // 仕様: rule matching より先に SQL 構文検証を強制する。
            var router = new SqlMockRouter();
            router.WhenSql(_ => true).ReturnsScalar(1);

            Assert.Throws<AssertFailedException>(() =>
                router.Scalar<int>("SELECT FROM WHERE"));
        }

        [TestMethod]
        public void Scalar_sequence_returns_values_in_order()
        {
            // 仕様: sequence は同じ rule への呼び出し順に消費される。
            var router = new SqlMockRouter();
            router
                .WhenSql(q => q.IsSelectFrom("dbo.Customers"))
                .ReturnsScalarSequence("Alice", "Bob");

            var first = router.Scalar<string>("SELECT Name FROM dbo.Customers");
            var second = router.Scalar<string>("SELECT Name FROM dbo.Customers");

            Assert.AreEqual("Alice", first);
            Assert.AreEqual("Bob", second);
        }

        [TestMethod]
        public void VerifyAll_fails_when_rule_was_not_called()
        {
            // 仕様: 登録した rule が未使用なら VerifyAll で検出する。
            var router = new SqlMockRouter();
            router.WhenSql(q => q.IsSelectFrom("dbo.Customers")).ReturnsScalar("Alice");

            Assert.Throws<AssertFailedException>(() => router.VerifyAll());
        }
    }
}
