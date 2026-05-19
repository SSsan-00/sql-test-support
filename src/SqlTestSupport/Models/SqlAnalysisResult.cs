using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlTestSupport
{
    // ScriptDom parse 結果を保持する。
    public sealed record SqlAnalysisResult(
        string OriginalSql,
        TSqlFragment Fragment);
}
