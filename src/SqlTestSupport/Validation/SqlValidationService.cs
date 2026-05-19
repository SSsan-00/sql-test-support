namespace SqlTestSupport
{
    public sealed class SqlValidationService
    {
        private readonly SqlServer2022SyntaxAnalyzer _analyzer;
        private readonly SqlInspectionService _inspectionService;

        public SqlValidationService()
            : this(null, null)
        {
        }

        public SqlValidationService(
            SqlServer2022SyntaxAnalyzer? analyzer,
            SqlInspectionService? inspectionService)
        {
            _analyzer = analyzer ?? new SqlServer2022SyntaxAnalyzer();
            _inspectionService = inspectionService ?? new SqlInspectionService();
        }

        // 構文解析だけが必要な呼び出し口。
        public SqlAnalysisResult Analyze(string sql)
            => _analyzer.Analyze(sql);

        // Mock 判定で使う形状情報まで元 SQL の AST から取り出す。
        public SqlInspectionResult Inspect(string sql)
        {
            var analysis = Analyze(sql);
            return _inspectionService.Inspect(analysis);
        }
    }
}
