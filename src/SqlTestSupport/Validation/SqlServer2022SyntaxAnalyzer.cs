using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlTestSupport
{
    public sealed class SqlServer2022SyntaxAnalyzer
    {
        private readonly SqlAstFingerprinter _fingerprinter;

        public SqlServer2022SyntaxAnalyzer(SqlAstFingerprinter? fingerprinter = null)
        {
            _fingerprinter = fingerprinter ?? new SqlAstFingerprinter();
        }

        public SqlAnalysisResult Analyze(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new SqlUnsupportedScriptException(sql ?? string.Empty, "SQL text must not be empty.");
            }

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
                throw new SqlUnsupportedScriptException(sql, "GO batch separators are not supported for command-text execution.");
            }

            return new SqlAnalysisResult(sql, fragment, _fingerprinter.CreateFingerprint(fragment));
        }
    }
}
