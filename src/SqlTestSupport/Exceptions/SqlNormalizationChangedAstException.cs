namespace SqlTestSupport
{
    public sealed class SqlNormalizationChangedAstException : SqlValidationException
    {
        public SqlNormalizationChangedAstException(
            string originalSql,
            string normalizedSql,
            string originalFingerprint,
            string normalizedFingerprint)
            : base("SQL normalization changed the parsed AST fingerprint.", originalSql)
        {
            NormalizedSql = normalizedSql;
            OriginalFingerprint = originalFingerprint;
            NormalizedFingerprint = normalizedFingerprint;
        }

        public string NormalizedSql { get; }

        public string OriginalFingerprint { get; }

        public string NormalizedFingerprint { get; }
    }
}
