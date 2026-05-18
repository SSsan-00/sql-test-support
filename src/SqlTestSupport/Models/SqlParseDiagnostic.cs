namespace SqlTestSupport
{
    // ScriptDom parse error を assertion message へ渡すための診断情報。
    public sealed record SqlParseDiagnostic(
        int Number,
        int Line,
        int Column,
        int Offset,
        string Message);
}
