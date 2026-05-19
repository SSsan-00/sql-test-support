# テストメソッドでの使い方

このヘルパーの使い方は 2 種類あります。

1. テストコード中で SQL 文字列を直接検証する
2. Mock DB を通して、テスト対象メソッドが実行した SQL をすべて検証する

既存プロダクションコードの SQL が DB 実行クラスへ渡る構成では、基本は 2 の Mock DB 経由を使います。これにより、テスト対象メソッド内の SQL を個別に取り出さなくても、実行された SQL はすべて validate / inspect されます。

## 直接 SQL を検証する

SQL 文字列そのものの文法をテストしたい場合は `Assert.IsValidSql` を使います。

```csharp
[TestMethod]
public void Customer_select_sql_is_valid()
{
    Assert.IsValidSql("""
        SELECT Id, Name
        FROM dbo.Customers
        WHERE Id = @Id
        """);
}
```

失敗時は `AssertFailedException` になり、parse error の行・列・message を含みます。

## テスト対象メソッドの SQL を Mock DB 経由で検証する

プロダクションコードが DB 実行クラスを直接呼ぶ場合、テストではその DB クラスを継承した Mock DB を渡します。

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
}
```

この形にすると、`Execute` に渡された第一引数 SQL はすべて Mock router 内で検証されます。

```text
テスト対象メソッド
  -> MockAppDb.Execute(sql)
  -> SqlMockRouter
  -> validate / parse
  -> inspect
  -> WhenSql rule matching
```

## object? 戻り値の例

テスト対象が `object?` で値を取得する場合は `ReturnsResult` を登録します。

```csharp
[TestMethod]
public void Get_customer_name_returns_name()
{
    var db = new MockAppDb();
    db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
      .ReturnsResult("Alice");

    var service = new CustomerService(db);

    var name = service.GetCustomerName(1);

    Assert.AreEqual("Alice", name);
    db.VerifyAllSqlExpectations();
}
```

このテストで保証すること:

- `CustomerService` が valid な T-SQL を実行する
- SQL が `dbo.Customers` を参照する `SELECT` である
- `WHERE` 句で `Id` を使う
- Mock は戻り値として `Alice` を返す
- 登録した rule が実際に呼ばれる

## 独自コレクション戻り値の例

`Dictionary` 継承または `IDictionary<TKey,TValue>` 実装の独自コレクションを返すメソッドは、`ExecuteResult<T>` にその型を指定します。

```csharp
public sealed class CustomerRows : Dictionary<string, object?>
{
}
```

```csharp
public override CustomerRows QueryRows(string sql, object? parameters = null)
    => _router.ExecuteResult<CustomerRows>(sql);
```

未登録 SQL でも空の `CustomerRows` が返るため、構文確認だけのテストを書きやすくなります。特定 SQL だけ Mock 値を返したい場合は rule を登録します。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.JoinsTable("dbo.Orders"))
  .ReturnsResult(rows);
```

## get_value の例

本番コードに次のような helper がある場合:

```csharp
public virtual object? get_value(string columns, string table, string where)
{
    var sql = $"SELECT {columns} FROM {table} WHERE {where}";
    return Execute(sql);
}
```

`Execute(sql)` が virtual なら、Mock DB 側で `Execute` を override するだけで構文検証と Mock 分岐が働きます。

```csharp
db.WhenSql(q =>
      q.IsSelectFrom("dbo.Customers") &&
      q.SelectsColumn("Name") &&
      q.WhereUses("Id"))
  .ReturnsResult("Alice");

var name = db.get_value("Name", "dbo.Customers", "Id = @Id");
```

内部で非 virtual 実行メソッドを直接呼ぶ場合は、Mock DB で `get_value` 自体も override して、組み立てた SQL を router へ渡します。

## Mock 振る舞いなしで構文だけ通す

`WhenSql` を登録しなくても、router に渡った SQL は必ず構文解析されます。

```csharp
[TestMethod]
public void Get_customer_name_sql_has_no_syntax_error()
{
    var db = new MockAppDb();
    var service = new CustomerService(db);

    service.GetCustomerName(1);

    Assert.IsGreaterThan(0, db.History.Count);
}
```

`object?` 戻り値なら未登録 SQL は `null` を返します。独自コレクション戻り値なら空コレクションを返します。テスト対象を最後まで進めるために具体値が必要な場合は、広い rule でダミーを返します。

```csharp
db.WhenSql(_ => true)
  .ReturnsResult("dummy");
```

## 同じ分類の SQL が複数回呼ばれる場合

同じ matcher に複数回一致する場合は sequence を使います。

```csharp
[TestMethod]
public void Retry_reads_status_until_ready()
{
    var db = new MockAppDb();
    db.WhenSql(q => q.IsSelectFrom("dbo.Jobs") && q.WhereUses("Id"))
      .ReturnsResultSequence("Pending", "Ready");

    var service = new JobService(db);

    var status = service.WaitUntilReady(10);

    Assert.AreEqual("Ready", status);
    db.VerifyAllSqlExpectations();
}
```

sequence を使い切った後に追加呼び出しがあると失敗します。これは「想定より多く SQL が呼ばれた」ことを検出するためです。

## 各メソッドの使い分け

| メソッド | 使う場所 | 目的 |
| --- | --- | --- |
| `Assert.IsValidSql(sql)` | テストコード | SQL 文字列の構文だけを確認する |
| `SqlMockRouter.ExecuteResult<T>(sql)` | Mock DB override 内 | SQL を検証し、登録値または既定 fallback を返す |
| `WhenSql(predicate)` | テストの Arrange | Mock の振る舞い条件を登録する |
| `ReturnsResult(value)` | テストの Arrange | rule に一致した SQL の戻り値を登録する |
| `ReturnsResultSequence(values)` | テストの Arrange | 同じ rule の複数回呼び出しに順番付き戻り値を登録する |
| `VerifyAll()` | テストの Assert / cleanup | 登録 rule が呼ばれたことを検証する |

テスト対象メソッドがプロダクション SQL を実行する場合、テストメソッド側で毎回 `Assert.IsValidSql` を直接呼ぶ必要はありません。Mock DB の override を通せば、実行された SQL は router 内で必ず検証されます。
