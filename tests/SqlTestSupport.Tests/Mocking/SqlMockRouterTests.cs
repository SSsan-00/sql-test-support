using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport.Tests
{
    [TestClass]
    public sealed class SqlMockRouterTests
    {
        [TestMethod]
        public void Scalar_returns_registered_value_for_matching_select()
        {
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
        public void Router_rejects_unregistered_sql()
        {
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
            var router = new SqlMockRouter();
            router.WhenSql(_ => true).ReturnsScalar(1);

            Assert.Throws<AssertFailedException>(() =>
                router.Scalar<int>("SELECT FROM WHERE"));
        }

        [TestMethod]
        public void Scalar_sequence_returns_values_in_order()
        {
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
            var router = new SqlMockRouter();
            router.WhenSql(q => q.IsSelectFrom("dbo.Customers")).ReturnsScalar("Alice");

            Assert.Throws<AssertFailedException>(() => router.VerifyAll());
        }
    }
}
