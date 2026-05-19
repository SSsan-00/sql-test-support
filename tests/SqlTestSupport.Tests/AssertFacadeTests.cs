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
            // test-facing API は低レベル例外を日本語の AssertFailedException に変換する。
            var exception = Assert.Throws<AssertFailedException>(() =>
                SqlAssertFacade.IsValidSql("SELECT FROM WHERE", "SQL の検証に失敗しました。"));

            Assert.IsTrue(exception.Message.Contains("SQL の検証に失敗しました。", StringComparison.Ordinal));
            Assert.IsTrue(exception.Message.Contains("構文解析エラー:", StringComparison.Ordinal));
            Assert.IsTrue(exception.Message.Contains("対象 SQL:", StringComparison.Ordinal));
        }
    }
}
