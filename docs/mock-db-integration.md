# Mock DB 連携

想定する本番 DB クラスは、第一引数に SQL command text を受け取る virtual な実行メソッドを持つ形です。

```csharp
public class AppDb
{
    public virtual object? Execute(string sql, object? parameters = null)
    {
        // 本番DB実行
    }

    public virtual CustomerRows QueryRows(string sql, object? parameters = null)
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

    public IReadOnlyList<SqlInvocation> History => _router.History;

    public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
        => _router.WhenSql(predicate);

    public void VerifyAllSqlExpectations()
        => _router.VerifyAll();

    public override object? Execute(string sql, object? parameters = null)
        => _router.ExecuteResult<object?>(sql);

    public override CustomerRows QueryRows(string sql, object? parameters = null)
        => _router.ExecuteResult<CustomerRows>(sql);
}
```

## 処理フロー

Mock 経由の SQL 呼び出しは、必ず同じ流れを通ります。

```text
SQL string
  -> validate / parse
  -> AST metadata を抽出
  -> invocation history に記録
  -> WhenSql ルールを評価
  -> 登録済みの振る舞い、または既定 fallback を返す
```

テストメソッドからの具体的な利用パターンは [テストメソッドでの使い方](test-method-usage.md) を参照します。

## ルール登録

ルールは AST 由来の metadata で登録します。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsResult("Alice");

db.WhenSql(q =>
      q.IsSelectFrom("dbo.Customers") &&
      q.JoinsTable("dbo.Orders") &&
      q.HavingCalls("COUNT"))
  .ReturnsResult(new CustomerRows());
```

同じ実行メソッドが複数回呼ばれても、SQL 形状ごとに分岐できます。複数 rule が一致し得る場合は、より具体的な rule を先に登録します。

## 既定動作

router は既定で、未登録 SQL でも安全に扱える範囲だけ fallback します。

- invalid SQL は matching 前に失敗する
- 未登録の `object?` 戻り値は validate / inspect / history 記録後に `null` を返す
- 未登録の nullable value type は validate / inspect / history 記録後に `null` を返す
- 未登録の reference type は validate / inspect / history 記録後に `null` を返す
- 未登録の `Dictionary` 継承または `IDictionary<TKey,TValue>` 実装の具象クラスは空の `new()` を返す
- 未登録の非 nullable value type は失敗する
- 登録済み rule が一度も呼ばれない場合、`VerifyAll()` で失敗する

reference type は、C# の nullable annotation を実行時に厳密判定しないため null 返却可能な型として扱います。null ではテスト対象を進められない場合は `ReturnsResult` で明示値を返します。

## 複数回呼び出し

同じ分類の SQL が複数回呼ばれる場合は sequence return を使います。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers"))
  .ReturnsResultSequence("Alice", "Bob");
```

1 回目は `Alice`、2 回目は `Bob` を返します。sequence を使い切った後の追加呼び出しは失敗します。

## get_value の扱い

本番コードに次のようなメソッドがある場合:

```csharp
public virtual object? get_value(string columns, string table, string where)
{
    var sql = $"SELECT {columns} FROM {table} WHERE {where}";
    return Execute(sql);
}
```

`Execute(sql)` が virtual なら、Mock DB で `Execute` を override するだけで十分です。`get_value` が呼ばれると、組み立て後 SQL が virtual dispatch で Mock 側の `Execute` に入り、router が検証します。

`get_value` が非 virtual 実行境界を直接呼ぶ場合は、Mock DB 側で `get_value` も override します。

```csharp
public override object? get_value(string columns, string table, string where)
{
    var sql = $"SELECT {columns} FROM {table} WHERE {where}";
    return _router.ExecuteResult<object?>(sql);
}
```

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
q.JoinsTable("dbo.Orders")
q.OrdersBy("CreatedAt")
q.GroupsBy("CustomerId")
q.HavingUses("Id")
q.HavingCalls("COUNT")
q.HasParameter("@Id")
```

標準の分岐面は AST metadata に寄せます。SQL 文字列の部分一致は、metadata で表現しづらいケースに限定します。
