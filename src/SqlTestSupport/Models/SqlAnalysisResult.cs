using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlTestSupport
{
    // ScriptDom parse 結果と正規化ガード用 fingerprint を保持する。
    public sealed record SqlAnalysisResult(
        string OriginalSql,
        TSqlFragment Fragment,
        string Fingerprint);
}
