namespace SqlTestSupport
{
    // 正規化前後の SQL と fingerprint 比較結果を保持する。
    public sealed record SqlNormalizationResult(
        string OriginalSql,
        string NormalizedSql,
        string OriginalFingerprint,
        string NormalizedFingerprint,
        SqlAnalysisResult OriginalAnalysis,
        SqlAnalysisResult NormalizedAnalysis);
}
