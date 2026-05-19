# テスト方針

テストは SQL 解析 pipeline と Mock DB の振る舞いを両方検証します。テスト名とコメントを読めば、どの仕様を保証しているか分かる形にします。

テスト対象メソッドでの使い方は [テストメソッドでの使い方](test-method-usage.md) を参照します。

## 検証系テスト

保証する内容:

- SQL Server 2022 の valid T-SQL は parse できる
- invalid T-SQL は syntax validation exception になる
- inspection は table、column、statement kind、parameter を抽出する
- JOIN / ORDER BY / GROUP BY / HAVING の metadata を Mock 分岐に使える

## Assert facade テスト

保証する内容:

- valid SQL は facade を通過する
- invalid SQL は `AssertFailedException` へ変換される
- 呼び出し側の custom message は failure output に残る
- エラー出力は日本語の見出しで parse error と対象 SQL を確認できる

## Mock router テスト

保証する内容:

- `WhenSql(...).ReturnsResult(...)` は一致した SQL に戻り値を返す
- nullable な戻り値は `ReturnsResult` 省略時に `null` を返す
- 未登録の `object?` 戻り値は既定で `null` を返す
- 未登録の `Dictionary` 継承クラスは空インスタンスを返す
- 未登録の `IDictionary<TKey,TValue>` 実装クラスは空インスタンスを返す
- 未登録の非 nullable value type は失敗する
- invalid SQL は rule matching 前に失敗する
- sequence return は登録順に消費される
- sequence を使い切った後の追加呼び出しは失敗する
- 未使用 rule は `VerifyAll()` で検出される

## Mock DB 統合テスト

integration test では、本番 DB クラスに近い最小 base class を定義します。

```csharp
public virtual object? Execute(string sql, object? parameters = null)
public virtual CustomerRows QueryRows(string sql, object? parameters = null)
public virtual object? get_value(string columns, string table, string where)
```

Mock subclass は SQL 実行境界だけを override し、`SqlMockRouter` に委譲します。実 DB は不要で、導入時の使い方に近い形を検証します。
