using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlTestSupport
{
    public sealed class SqlServer2022Normalizer
    {
        private readonly SqlServer2022SyntaxAnalyzer _analyzer;

        public SqlServer2022Normalizer(SqlServer2022SyntaxAnalyzer? analyzer = null)
        {
            _analyzer = analyzer ?? new SqlServer2022SyntaxAnalyzer();
        }

        public SqlNormalizationResult Normalize(string sql)
        {
            var original = _analyzer.Analyze(sql);
            var normalizedSql = Generate(original.Fragment);
            var normalized = _analyzer.Analyze(normalizedSql);

            // 正規化は fail-closed。AST 構造が変わる疑いがあれば返さない。
            if (!StringComparer.Ordinal.Equals(original.Fingerprint, normalized.Fingerprint))
            {
                throw new SqlNormalizationChangedAstException(
                    original.OriginalSql,
                    normalizedSql,
                    original.Fingerprint,
                    normalized.Fingerprint);
            }

            return new SqlNormalizationResult(
                original.OriginalSql,
                normalizedSql,
                original.Fingerprint,
                normalized.Fingerprint,
                original,
                normalized);
        }

        private static string Generate(TSqlFragment fragment)
        {
            var options = new SqlScriptGeneratorOptions
            {
                SqlVersion = SqlVersion.Sql160,
                SqlEngineType = SqlEngineType.Standalone,
                IncludeSemicolons = true,
                KeywordCasing = KeywordCasing.Uppercase,
                IndentationSize = 4,
                NewLineBeforeFromClause = true,
                NewLineBeforeWhereClause = true,
                NewLineBeforeOrderByClause = true,
                NewLineBeforeGroupByClause = true,
                NewLineBeforeHavingClause = true,
                NewLineBeforeJoinClause = true
            };

            var generator = new Sql160ScriptGenerator(options);
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            generator.GenerateScript(fragment, writer);
            return writer.ToString();
        }
    }
}
