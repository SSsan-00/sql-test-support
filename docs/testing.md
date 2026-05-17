# テスト方針

テストは SQL 解析 pipeline と Mock DB の振る舞いを両方検証します。テスト名とコメントを読めば、どの仕様を保証しているか分かる形にします。

テスト対象メソッドでの使い方は [テストメソッドでの使い方](test-method-usage.md) を参照します。

## 検証系テスト

保証する内容:

- SQL Server 2022 の valid T-SQL は parse できる
- invalid T-SQL は syntax validation exception になる
- normalization は fingerprint が一致する場合だけ SQL を返す
- inspection は table、column、statement kind、parameter を抽出する

## Assert facade テスト

保証する内容:

- valid SQL は facade を通過する
- invalid SQL は `AssertFailedException` へ変換される
- 呼び出し側の custom message は failure output に残る
- 正規化済み SQL を呼び出し側へ返せる

## Mock router テスト

保証する内容:

- `WhenSql(...).ReturnsScalar(...)` は scalar call に対応する
- `WhenSql(...).ReturnsAffectedRows(...)` は non-query call に対応する
- `WhenSql(...).Completes()` は void command call に対応する
- 未登録 SQL は失敗する
- `ValidateOnlyForCommands` では未登録 void command を構文解析だけで通す
- invalid SQL は rule matching 前に失敗する
- void command に affected rows rule を流用すると失敗する
- `ValidateOnlyForCommands` でも scalar / non-query の未登録 SQL は失敗する
- sequence return は登録順に消費される
- 未使用 rule は `VerifyAll()` で検出される

## Mock DB 統合テスト

integration test では、本番 DB クラスに近い最小 base class を定義します。

```csharp
public virtual int Execute(string sql, object? parameters = null)
public virtual T Scalar<T>(string sql, object? parameters = null)
public virtual void ExecuteCommand(string sql, object? parameters = null)
```

Mock subclass はこの 2 メソッドだけを override し、`SqlMockRouter` に委譲します。実 DB は不要で、導入時の使い方に近い形を検証します。
