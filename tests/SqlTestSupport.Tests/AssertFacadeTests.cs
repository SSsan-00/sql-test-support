using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport.Tests
{
    [TestClass]
    public sealed class AssertFacadeTests
    {
        [TestMethod]
        public void IsValidSql_does_not_throw_for_valid_sql()
        {
            SqlAssertFacade.IsValidSql("""
                SELECT Id
                FROM dbo.Customers
                """);
        }

        [TestMethod]
        public void IsValidSql_throws_assert_failed_for_invalid_sql()
        {
            var exception = Assert.Throws<AssertFailedException>(() =>
                SqlAssertFacade.IsValidSql("SELECT FROM WHERE", "Custom SQL failed."));

            Assert.IsTrue(exception.Message.Contains("Custom SQL failed.", StringComparison.Ordinal));
            Assert.IsTrue(exception.Message.Contains("Parse errors:", StringComparison.Ordinal));
        }

        [TestMethod]
        public void NormalizeSql_returns_normalized_sql()
        {
            var normalized = SqlAssertFacade.NormalizeSql("""
                select Id
                from dbo.Customers
                """);

            Assert.IsTrue(normalized.Contains("SELECT", StringComparison.Ordinal));
        }
    }
}
