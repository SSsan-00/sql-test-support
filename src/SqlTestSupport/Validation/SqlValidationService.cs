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

        public SqlAnalysisResult Analyze(string sql)
            => _analyzer.Analyze(sql);

        public SqlNormalizationResult Normalize(string sql)
            => _normalizer.Normalize(sql);

        public SqlInspectionResult Inspect(string sql)
        {
            var normalized = Normalize(sql);
            return _inspectionService.Inspect(normalized);
        }
    }
}
