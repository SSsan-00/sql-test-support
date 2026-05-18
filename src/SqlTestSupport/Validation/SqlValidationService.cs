namespace SqlTestSupport
{
    public sealed class SqlValidationService
    {
        private readonly SqlServer2022SyntaxAnalyzer _analyzer;
        private readonly SqlServer2022Normalizer _normalizer;
        private readonly SqlInspectionService _inspectionService;

        public SqlValidationService()
            : this(null, null, null)
        {
        }

        public SqlValidationService(
            SqlServer2022SyntaxAnalyzer? analyzer,
            SqlServer2022Normalizer? normalizer,
            SqlInspectionService? inspectionService)
        {
            _analyzer = analyzer ?? new SqlServer2022SyntaxAnalyzer();
            _normalizer = normalizer ?? new SqlServer2022Normalizer(_analyzer);
            _inspectionService = inspectionService ?? new SqlInspectionService();
        }

        // 構文解析だけが必要な呼び出し口。
        public SqlAnalysisResult Analyze(string sql)
            => _analyzer.Analyze(sql);

        // 表記ゆれを揃え、AST が変わらないことも確認する。
        public SqlNormalizationResult Normalize(string sql)
            => _normalizer.Normalize(sql);

        // Mock 判定で使う形状情報まで一度に取り出す。
        public SqlInspectionResult Inspect(string sql)
        {
            var normalized = Normalize(sql);
            return _inspectionService.Inspect(normalized);
        }
    }
}
