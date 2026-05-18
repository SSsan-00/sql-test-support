# テストメソッドでの使い方

このヘルパーの使い方は 2 種類あります。

1. テストコード中で SQL 文字列を直接検証する
2. Mock DB を通して、テスト対象メソッドが実行した SQL をすべて検証する

既存プロダクションコードの SQL が DB 実行クラスへ渡る構成では、基本は 2 の Mock DB 経由を使います。これにより、テスト対象メソッド内の SQL を個別に取り出さなくても、実行された SQL はすべて validate / normalize / inspect されます。

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

使いどころ:

- テストデータ作成 SQL の文法確認
- helper 内に置いた SQL literal の文法確認
- 正規化までは不要な軽い検証

失敗時は `AssertFailedException` になり、parse error の行・列・message を含みます。

## 正規化済み SQL を使う

テスト内で SQL を実行 helper に渡す前に正規化したい場合は `Assert.NormalizeSql` を使います。

```csharp
[TestMethod]
public void Seed_customer()
{
    var sql = Assert.NormalizeSql("""
        insert into dbo.Customers (Id, Name)
        values (1, N'Alice')
        """);

    ExecuteSql(sql);
}
```

`NormalizeSql` は構文検証に加えて、正規化前後の AST fingerprint 一致も確認します。一致しない場合は正規化済み SQL を返さず、`AssertFailedException` で失敗します。

## テスト対象メソッドの SQL を Mock DB 経由で検証する

プロダクションコードが DB 実行クラスを直接呼ぶ場合、テストではその DB クラスを継承した Mock DB を渡します。

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

この形にすると、`Execute` / `Scalar` に渡された第一引数 SQL はすべて Mock router 内で検証されます。

```text
テスト対象メソッド
  -> MockAppDb.Execute(sql)
  -> SqlMockRouter
  -> validate
  -> normalize
  -> fingerprint 比較
  -> inspect
  -> WhenSql rule matching
```

## Scalar 系メソッドの例

テスト対象が `Scalar<T>` で値を取得する場合は `ReturnsScalar` を登録します。

```csharp
[TestMethod]
public void Get_customer_name_returns_name()
{
    var db = new MockAppDb();
    db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
      .ReturnsScalar("Alice");

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
- Mock は scalar 値として `Alice` を返す
- 登録した rule が実際に呼ばれる

## Execute 系メソッドの例

テスト対象が `Execute` で更新件数を受け取る場合は `ReturnsAffectedRows` を登録します。

```csharp
[TestMethod]
public void Rename_customer_updates_customer()
{
    var db = new MockAppDb();
    db.WhenSql(q => q.IsUpdate("dbo.Customers") && q.WhereUses("Id"))
      .ReturnsAffectedRows(1);

    var service = new CustomerService(db);

    var updated = service.RenameCustomer(1, "Alice");

    Assert.IsTrue(updated);
    db.VerifyAllSqlExpectations();
}
```

このテストで保証すること:

- `CustomerService` が valid な T-SQL を実行する
- SQL が `dbo.Customers` を更新対象にする `UPDATE` である
- `WHERE` 句で `Id` を使う
- Mock は affected rows として `1` を返す
- 登録した rule が実際に呼ばれる

## 戻り値なし Execute 系メソッドの例

テスト対象が `void Execute(...)` のような戻り値なし実行メソッドを使う場合は `Completes` を登録し、Mock DB 側では `ExecuteCommand` に委譲します。

```csharp
public sealed class MockAppDb : AppDb
{
    private readonly SqlMockRouter _router = new();

    public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
        => _router.WhenSql(predicate);

    public void VerifyAllSqlExpectations()
        => _router.VerifyAll();

    public override void Execute(string sql, object? parameters = null)
        => _router.ExecuteCommand(sql);
}
```

```csharp
[TestMethod]
public void Rename_customer_executes_update_command()
{
    var db = new MockAppDb();
    db.WhenSql(q => q.IsUpdate("dbo.Customers") && q.WhereUses("Id"))
      .Completes();

    var service = new CustomerService(db);

    service.RenameCustomer(1, "Alice");

    db.VerifyAllSqlExpectations();
}
```

このテストで保証すること:

- `CustomerService` が valid な T-SQL を実行する
- SQL が `dbo.Customers` を更新対象にする `UPDATE` である
- `WHERE` 句で `Id` を使う
- 登録した戻り値なし command は `Completes` が登録された場合だけ成功する
- 登録した rule が実際に呼ばれる

`ReturnsAffectedRows` は `ExecuteNonQuery` 用です。戻り値なし command に流用すると失敗します。

## 未登録の戻り値なし SQL を構文解析だけ通す

テスト対象メソッドが戻り値なし SQL を多数実行し、そのうち一部だけ Mock 振る舞いを指定したい場合、既定の `SqlMockRouter()` で未登録 SQL を構文解析だけ通せます。

```csharp
public sealed class MockAppDb : AppDb
{
    private readonly SqlMockRouter _router = new();

    public SqlMockSetup WhenSql(Func<SqlInvocation, bool> predicate)
        => _router.WhenSql(predicate);

    public override void Execute(string sql, object? parameters = null)
        => _router.ExecuteCommand(sql);
}
```

```csharp
[TestMethod]
public void Save_customer_mocks_only_the_audit_command()
{
    var db = new MockAppDb();
    db.WhenSql(q => q.IsInsertInto("dbo.AuditLogs"))
      .Completes();

    var service = new CustomerService(db);

    service.SaveCustomer(1, "Alice");
}
```

既定動作では、`dbo.AuditLogs` 以外の戻り値なし SQL も構文解析・正規化・履歴記録までは行われます。`WhenSql` に一致しなくても失敗しません。

## 未登録の nullable scalar を null で通す

戻り値型が nullable の scalar は、未登録 SQL でも構文解析・正規化・履歴記録後に `null` を返します。

```csharp
[TestMethod]
public void Get_parent_customer_id_returns_null_when_query_is_unregistered()
{
    var db = new MockAppDb();
    var service = new CustomerService(db);

    int? parentId = service.GetParentCustomerId(1);

    Assert.IsNull(parentId);
}
```

reference type の scalar も null 返却可能な型として扱います。`string` と `string?` の違いは実行時の generic 型だけでは厳密に判定しません。

ただし、次のメソッドでは未登録 SQL は失敗します。

- non-nullable `Scalar<T>`: 返す値が必要
- `ExecuteNonQuery`: affected rows が必要

戻り値が必要な SQL は、`ReturnsScalar` または `ReturnsAffectedRows` を登録します。nullable な scalar で `null` を期待する場合だけ、未登録または `ReturnsScalar` 省略の rule を許容できます。

## 同じ分類の SQL が複数回呼ばれる場合

同じ matcher に複数回一致する場合は sequence を使います。

```csharp
[TestMethod]
public void Retry_reads_status_until_ready()
{
    var db = new MockAppDb();
    db.WhenSql(q => q.IsSelectFrom("dbo.Jobs") && q.WhereUses("Id"))
      .ReturnsScalarSequence("Pending", "Ready");

    var service = new JobService(db);

    var status = service.WaitUntilReady(10);

    Assert.AreEqual("Ready", status);
    db.VerifyAllSqlExpectations();
}
```

sequence を使い切った後に追加呼び出しがあると失敗します。これは「想定より多く SQL が呼ばれた」ことを検出するためです。

## 未登録 SQL の扱い

既定の Mock router は、未登録 SQL でも次の範囲は通します。

- 戻り値なし command: 構文解析・正規化・履歴記録だけ行う
- nullable scalar: 構文解析・正規化・履歴記録後に `null` を返す

すべての未登録 SQL を失敗させたい場合は strict mode を明示します。

```csharp
[TestMethod]
public void Unexpected_sql_fails_the_test()
{
    var db = new StrictMockAppDb();

    var service = new CustomerService(db);

    Assert.Throws<AssertFailedException>(() => service.GetCustomerName(1));
}
```

これは「テスト対象メソッドが想定外の SQL を実行した」ことを明示的に検出したい場合に使います。

## 各メソッドの使い分け

| メソッド | 使う場所 | 目的 |
| --- | --- | --- |
| `Assert.IsValidSql(sql)` | テストコード | SQL 文字列の構文だけを確認する |
| `Assert.NormalizeSql(sql)` | テストコード | 正規化済み SQL を取得する |
| `SqlMockRouter.ExecuteNonQuery(sql)` | Mock DB override 内 | `Execute` 系 SQL を検証し、affected rows を返す |
| `SqlMockRouter.Scalar<T>(sql)` | Mock DB override 内 | scalar 系 SQL を検証し、登録値を返す |
| `SqlMockRouter.ExecuteCommand(sql)` | Mock DB override 内 | 戻り値なし SQL command を検証する |
| `WhenSql(predicate)` | テストの Arrange | Mock の振る舞い条件を登録する |
| `Completes()` | テストの Arrange | 戻り値なし command の成功を登録する |
| `UnmatchedSqlBehavior.Strict` | Mock DB 初期化 | 未登録 SQL をすべて失敗させる |
| `UnmatchedSqlBehavior.ValidateOnlyForCommands` | Mock DB 初期化 | 未登録の戻り値なし command だけを構文解析で通す |
| `VerifyAll()` | テストの Assert / cleanup | 登録 rule が呼ばれたことを検証する |

テスト対象メソッドがプロダクション SQL を実行する場合、テストメソッド側で毎回 `Assert.IsValidSql` を直接呼ぶ必要はありません。Mock DB の override を通せば、実行された SQL は router 内で必ず検証されます。
