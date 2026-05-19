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
            builder.AppendLine("方言: SQL Server 2022 / Sql160 / QUOTED_IDENTIFIER ON");
            builder.AppendLine();

            if (exception is SqlSyntaxValidationException syntaxException)
            {
                builder.AppendLine("構文解析エラー:");
                foreach (var diagnostic in syntaxException.Diagnostics)
                {
                    builder
                        .Append("  - ")
                        .Append("行 ").Append(diagnostic.Line)
                        .Append(", 列 ").Append(diagnostic.Column)
                        .Append(", 番号 ").Append(diagnostic.Number)
                        .Append(": ")
                        .AppendLine(diagnostic.Message);
                }

                builder.AppendLine();
            }
            else if (exception is SqlUnsupportedScriptException unsupportedException)
            {
                builder.AppendLine("理由:");
                builder.AppendLine(unsupportedException.Reason);
                builder.AppendLine();
            }

            builder.AppendLine("対象 SQL:");
            builder.AppendLine(exception.Sql);

            return builder.ToString();
        }
    }
}
