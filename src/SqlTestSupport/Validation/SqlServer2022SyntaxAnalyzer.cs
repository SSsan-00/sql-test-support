using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlTestSupport
{
    public sealed class SqlServer2022SyntaxAnalyzer
    {
        public SqlAnalysisResult Analyze(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new SqlUnsupportedScriptException(sql ?? string.Empty, "SQL 文字列が空です。");
            }

            // SQL Server 2022 固定。QUOTED_IDENTIFIER は ON 相当。
            var parser = new TSql160Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(sql);
            var fragment = parser.Parse(reader, out var parseErrors);
            var diagnostics = parseErrors
                .Select(error => new SqlParseDiagnostic(
                    error.Number,
                    error.Line,
                    error.Column,
                    error.Offset,
                    error.Message))
                .ToArray();

            if (diagnostics.Length > 0)
            {
                throw new SqlSyntaxValidationException(sql, diagnostics);
            }

            if (fragment is TSqlScript script && script.Batches.Count > 1)
            {
                // command text 実行では GO を扱わない。
                throw new SqlUnsupportedScriptException(sql, "GO による複数 batch は command text として扱わないため未対応です。");
            }

            return new SqlAnalysisResult(sql, fragment);
        }
    }
}
