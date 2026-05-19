using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport
{
    // 独自 Assert クラスから委譲する MSTest 向け facade。
    public static class SqlAssertFacade
    {
        private static readonly SqlValidationService ValidationService = new();

        // 構文検証だけを行い、失敗時は AssertFailedException に変換する。
        public static void IsValidSql(string sql, string? message = null)
        {
            try
            {
                ValidationService.Analyze(sql);
            }
            catch (SqlValidationException exception)
            {
                throw new AssertFailedException(
                    SqlAssertMessageBuilder.Build(message, exception),
                    exception);
            }
        }

    }
}
