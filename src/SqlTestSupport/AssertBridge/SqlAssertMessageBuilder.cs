using System.Text;

namespace SqlTestSupport
{
    // 低レベル例外を MSTest の失敗メッセージとして読める形に整える。
    public static class SqlAssertMessageBuilder
    {
        public static string Build(string? userMessage, SqlValidationException exception)
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(userMessage))
            {
                builder.AppendLine(userMessage);
                builder.AppendLine();
            }

            builder.AppendLine(exception.Message);
            builder.AppendLine("Dialect: SQL Server 2022 / Sql160 / QUOTED_IDENTIFIER ON");
            builder.AppendLine();

            if (exception is SqlSyntaxValidationException syntaxException)
            {
                builder.AppendLine("Parse errors:");
                foreach (var diagnostic in syntaxException.Diagnostics)
                {
                    builder
                        .Append("  - ")
                        .Append("Line ").Append(diagnostic.Line)
                        .Append(", Column ").Append(diagnostic.Column)
                        .Append(", Number ").Append(diagnostic.Number)
                        .Append(": ")
                        .AppendLine(diagnostic.Message);
                }

                builder.AppendLine();
            }
            else if (exception is SqlNormalizationChangedAstException changedAstException)
            {
                builder.AppendLine("Original fingerprint:");
                builder.AppendLine(changedAstException.OriginalFingerprint);
                builder.AppendLine();
                builder.AppendLine("Normalized fingerprint:");
                builder.AppendLine(changedAstException.NormalizedFingerprint);
                builder.AppendLine();
                builder.AppendLine("Normalized SQL:");
                builder.AppendLine(changedAstException.NormalizedSql);
                builder.AppendLine();
            }
            else if (exception is SqlUnsupportedScriptException unsupportedException)
            {
                builder.AppendLine("Reason:");
                builder.AppendLine(unsupportedException.Reason);
                builder.AppendLine();
            }

            builder.AppendLine("SQL:");
            builder.AppendLine(exception.Sql);

            return builder.ToString();
        }
    }
}
