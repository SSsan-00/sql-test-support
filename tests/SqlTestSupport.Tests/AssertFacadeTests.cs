using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport.Tests
{
    [TestClass]
    public sealed class AssertFacadeTests
    {
        [TestMethod]
        public void IsValidSql_does_not_throw_for_valid_sql()
        {
            // Assert facade は valid SQL を失敗扱いにしない。
            SqlAssertFacade.IsValidSql("""
                SELECT Id
                FROM dbo.Customers
                """);
        }

        [TestMethod]
        public void IsValidSql_throws_assert_failed_for_invalid_sql()
        {
            // test-facing API は低レベル例外を AssertFailedException に変換する。
            var exception = Assert.Throws<AssertFailedException>(() =>
                SqlAssertFacade.IsValidSql("SELECT FROM WHERE", "Custom SQL failed."));

            Assert.IsTrue(exception.Message.Contains("Custom SQL failed.", StringComparison.Ordinal));
            Assert.IsTrue(exception.Message.Contains("Parse errors:", StringComparison.Ordinal));
        }

        [TestMethod]
        public void NormalizeSql_returns_normalized_sql()
        {
            // 呼び出し側は正規化済み SQL を string として受け取れる。
            var normalized = SqlAssertFacade.NormalizeSql("""
                select Id
                from dbo.Customers
                """);

            Assert.IsTrue(normalized.Contains("SELECT", StringComparison.Ordinal));
        }
    }
}
