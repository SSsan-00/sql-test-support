using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlTestSupport
{
    public sealed record SqlAnalysisResult(
        string OriginalSql,
        TSqlFragment Fragment,
        string Fingerprint);
}
