using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlTestSupport
{
    public static class SqlAssertFacade
    {
        private static readonly SqlValidationService ValidationService = new();

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

        public static string NormalizeSql(string sql, string? message = null)
        {
            try
            {
                return ValidationService.Normalize(sql).NormalizedSql;
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
