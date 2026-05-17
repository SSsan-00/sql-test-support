namespace SqlTestSupport
{
    public sealed record SqlNormalizationResult(
        string OriginalSql,
        string NormalizedSql,
        string OriginalFingerprint,
        string NormalizedFingerprint,
        SqlAnalysisResult OriginalAnalysis,
        SqlAnalysisResult NormalizedAnalysis);
}
