# Mock DB 連携

想定する本番 DB クラスは、第一引数に SQL command text を受け取る virtual な実行メソッドを持つ形です。

```csharp
public class AppDb
{
    public virtual int Execute(string sql, object? parameters = null)
    {
        // 本番DB実行
    }

    public virtual T Scalar<T>(string sql, object? parameters = null)
    {
        // 本番DB実行
    }
}
```

テストダブルでは実行境界だけを override します。

```csharp
public sealed class MockAppDb : AppDb
{
    private readonly SqlMockRouter _router = new();

    public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
        => _router.WhenSql(predicate);

    public void VerifyAllSqlExpectations()
        => _router.VerifyAll();

    public override int Execute(string sql, object? parameters = null)
        => _router.ExecuteNonQuery(sql);

    public override T Scalar<T>(string sql, object? parameters = null)
        => _router.Scalar<T>(sql);
}
```

戻り値なしの本番メソッドを override する場合は `ExecuteCommand` に委譲します。

```csharp
public override void Execute(string sql, object? parameters = null)
    => _router.ExecuteCommand(sql);
```

## 処理フロー

Mock 経由の SQL 呼び出しは、必ず同じ流れを通ります。

```text
SQL string
  -> validate
  -> normalize
  -> AST fingerprint stability を検証
  -> AST metadata を抽出
  -> invocation history に記録
  -> WhenSql ルールを評価
  -> 登録済みの振る舞いを返す。未登録なら fail
```

テストメソッドからの具体的な利用パターンは [テストメソッドでの使い方](test-method-usage.md) を参照します。

ルールは AST 由来の metadata で登録します。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsScalar("Alice");

db.WhenSql(q => q.IsUpdate("dbo.Customers"))
  .ReturnsAffectedRows(1);
```

## Strict 動作

router はデフォルトで strict に動きます。

- invalid SQL は matching 前に失敗する
- 未登録 SQL は失敗する
- `ReturnsScalar` の rule は `ExecuteNonQuery` を満たせない
- `ReturnsAffectedRows` の rule は `Scalar<T>` を満たせない
- `ReturnsAffectedRows` の rule は `ExecuteCommand` を満たせない
- nullable な `Scalar<T>` は、`WhenSql` に一致する未設定 rule の場合 `null` を返せる
- 戻り値なし command には `Completes` rule が必要
- 登録済み rule が一度も呼ばれない場合、`VerifyAll()` で失敗する

## 未登録 void command の validate-only mode

戻り値なし実行メソッドに限り、未登録 SQL を構文解析だけで通す mode を選べます。

```csharp
public sealed class MockAppDb : AppDb
{
    private readonly SqlMockRouter _router =
        new(UnmatchedSqlBehavior.ValidateOnlyForCommands);

    public override void Execute(string sql, object? parameters = null)
        => _router.ExecuteCommand(sql);
}
```

この mode の挙動:

- 未登録の `ExecuteCommand` SQL は validate / normalize / inspect / history 記録だけ行う
- invalid SQL は失敗する
- 登録済み `WhenSql(...).Completes()` に一致した SQL は通常通り rule 呼び出しとして扱う
- `Scalar<T>` と `ExecuteNonQuery` の未登録 SQL は引き続き失敗する

戻り値が必要なメソッドでは返す値を決められないため、登録必須のままにします。

## 複数回呼び出し

同じ分類の SQL が複数回呼ばれる場合は sequence return を使います。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers"))
  .ReturnsScalarSequence("Alice", "Bob");
```

1 回目は `Alice`、2 回目は `Bob` を返します。sequence を使い切った後の追加呼び出しは失敗します。

## Matching 指針

まず次の matcher を使います。

```csharp
q.IsSelectFrom("dbo.Customers")
q.IsInsertInto("dbo.Customers")
q.IsUpdate("dbo.Customers")
q.IsDeleteFrom("dbo.Customers")
q.WhereUses("Id")
q.SelectsColumn("Name")
q.ReferencesTable("dbo.Customers")
q.TargetsTable("dbo.Customers")
q.HasParameter("@Id")
```

`NormalizedSql.Contains(...)` も escape hatch として使えます。ただし標準の分岐面は AST metadata に寄せます。
