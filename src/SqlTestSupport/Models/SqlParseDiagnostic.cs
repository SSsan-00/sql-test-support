namespace SqlTestSupport
{
    public sealed record SqlParseDiagnostic(
        int Number,
        int Line,
        int Column,
        int Offset,
        string Message);
}
