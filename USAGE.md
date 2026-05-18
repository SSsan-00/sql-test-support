# 利用手順

このファイルは、SqlTestSupport を既存の MSTest プロジェクトへ導入し、SQL 検証・正規化・Mock DB 分岐を使い始めるための手順を 1 つにまとめたものです。

## 前提

- .NET 9 の MSTest プロジェクト
- SQL Server 2022 向け T-SQL
- 本番コードの SQL 実行メソッドは、第一引数に SQL 文字列を受け取る
- テストでは本番 DB クラスを継承し、SQL 実行メソッドだけを override できる

検証は SQL Server への接続を行いません。ScriptDom で文法を検証し、正規化前後の AST fingerprint が一致する場合だけ正規化 SQL を返します。

## 1. 導入方法を選ぶ

通常は A を選びます。既存プロジェクトにファイルを増やしたくない場合だけ B を使います。

### A. 生成済み C# ファイルを追加する

`dist/SqlTestSupport.cs` を導入先のテストプロジェクトへ追加します。

導入先でも SqlTestSupport 自体の仕様を検証したい場合だけ、`dist/SqlTestSupport.Tests.cs` も追加します。

必要な package reference:

```xml
<PackageReference Include="Microsoft.SqlServer.TransactSql.ScriptDom" Version="180.18.1" />
<PackageReference Include="MSTest.TestFramework" Version="4.0.2" />
```

導入先が `MSTest` meta package を使っている場合は、既存構成に合わせてください。

### B. ビルド時に自動展開する

`dist/SqlTestSupport.Directory.Build.targets` を、導入先テストプロジェクトと同じディレクトリへ `Directory.Build.targets` という名前で配置します。

```text
dist/SqlTestSupport.Directory.Build.targets
  -> /path/to/test-project/Directory.Build.targets
```

既存の `Directory.Build.targets` がある場合は上書きせず、`SqlTestSupport.Directory.Build.targets` という名前で配置し、既存ファイルから import します。

```xml
<Import Project="SqlTestSupport.Directory.Build.targets" />
```

### C. bootstrap で展開する

リポジトリを取得できる場合:

```bash
./bootstrap/SqlTestSupport.expand.sh /path/to/test-project/SqlTestSupport
```

リポジトリを取得できず、Web UI から 1 ファイルだけコピーする場合:

```bash
mkdir SqlTestSupportBootstrap
cd SqlTestSupportBootstrap
dotnet new console --force
```

生成された `Program.cs` を `bootstrap/SqlTestSupport.Bootstrap.cs` の内容で置き換え、次を実行します。

```bash
dotnet run -- /path/to/test-project/SqlTestSupport
```

## 2. 独自 Assert クラスに forwarding method を追加する

テストコードからは `Assert.IsValidSql(sql)` と `Assert.NormalizeSql(sql)` で呼べる形にします。

```csharp
public static void IsValidSql(string sql, string? message = null)
    => SqlAssertFacade.IsValidSql(sql, message);

public static string NormalizeSql(string sql, string? message = null)
    => SqlAssertFacade.NormalizeSql(sql, message);
```

これにより、構文不正や正規化前後の AST fingerprint 不一致は MSTest の `AssertFailedException` として失敗します。

## 3. SQL 文字列を直接検証する

テストコード内で SQL 文字列だけを検証したい場合:

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

正規化済み SQL を使いたい場合:

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

## 4. Mock DB を作る

本番 DB クラスが次のような実行メソッドを持つ前提です。

```csharp
public class AppDb
{
    public virtual int Execute(string sql, object? parameters = null)
    {
        throw new NotImplementedException();
    }

    public virtual T Scalar<T>(string sql, object? parameters = null)
    {
        throw new NotImplementedException();
    }

    public virtual void ExecuteCommand(string sql, object? parameters = null)
    {
        throw new NotImplementedException();
    }
}
```

テスト側では SQL 実行境界だけを override し、第一引数の SQL を `SqlMockRouter` に渡します。

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

    public override void ExecuteCommand(string sql, object? parameters = null)
        => _router.ExecuteCommand(sql);
}
```

この形にすると、テスト対象メソッドが実行した SQL はすべて validate / normalize / inspect されます。

## 5. Mock の振る舞いを登録する

`WhenSql` には、正規化・解析済みの `SqlInvocation` が渡されます。SQL 文字列の部分一致より、AST metadata を使った条件を優先します。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsScalar("Alice");

db.WhenSql(q => q.IsUpdate("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsAffectedRows(1);

db.WhenSql(q => q.IsInsertInto("dbo.AuditLogs"))
  .Completes();
```

主な matcher:

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

## 6. テストメソッドで使う

Scalar 値を返す SQL:

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

更新件数を返す SQL:

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

戻り値なし command:

```csharp
[TestMethod]
public void Save_customer_writes_audit_log()
{
    var db = new MockAppDb();
    db.WhenSql(q => q.IsInsertInto("dbo.AuditLogs"))
      .Completes();

    var service = new CustomerService(db);

    service.SaveCustomer(1, "Alice");

    db.VerifyAllSqlExpectations();
}
```

## 7. Mock 振る舞いなしで構文だけ確認する

テスト対象メソッド内で実行される SQL の構文エラーだけを確認したい場合は、基本的に `WhenSql(...)` を登録しません。SQL は `SqlMockRouter` に渡った時点で必ず validate / normalize / inspect されるため、構文不正があればテスト対象メソッドの実行中に `AssertFailedException` で失敗します。

### 戻り値なし実行メソッドだけの場合

戻り値なしの `ExecuteCommand(sql)` だけを使うテスト対象なら、Mock DB を作ってテスト対象メソッドを実行するだけです。下の例では、少なくとも 1 つの SQL が通ったことを確認するために `History` を公開しています。

```csharp
[TestMethod]
public void Save_customer_sql_has_no_syntax_error()
{
    var db = new MockAppDb();
    var service = new CustomerService(db);

    service.SaveCustomer(1, "Alice");

    Assert.IsGreaterThan(0, db.History.Count);
}
```

この用途では `WhenSql(...).Completes()` も `VerifyAllSqlExpectations()` も不要です。未登録の `ExecuteCommand` は、構文解析・正規化・履歴記録だけ行って成功します。

`History` は Mock DB から router の `History` をそのまま公開します。

```csharp
public sealed class MockAppDb : AppDb
{
    private readonly SqlMockRouter _router = new();

    public IReadOnlyList<SqlInvocation> History => _router.History;

    public override void ExecuteCommand(string sql, object? parameters = null)
        => _router.ExecuteCommand(sql);
}
```

### 戻り値のある実行メソッドがある場合

戻り値が必要なメソッドは、テスト対象メソッドを最後まで進めるためのダミー値を返します。構文解析は rule matching より前に必ず実行されるため、`WhenSql(_ => true)` のような広い rule を使っても構文検証はスキップされません。

scalar 戻り値のダミー:

```csharp
[TestMethod]
public void Get_customer_name_sql_has_no_syntax_error()
{
    var db = new MockAppDb();
    db.WhenSql(_ => true)
      .ReturnsScalar("dummy");

    var service = new CustomerService(db);

    service.GetCustomerName(1);
}
```

更新件数のダミー:

```csharp
[TestMethod]
public void Rename_customer_sql_has_no_syntax_error()
{
    var db = new MockAppDb();
    db.WhenSql(_ => true)
      .ReturnsAffectedRows(1);

    var service = new CustomerService(db);

    service.RenameCustomer(1, "Alice");
}
```

複数回呼ばれる場合は sequence を使います。

```csharp
db.WhenSql(_ => true)
  .ReturnsScalarSequence("Alice", "Bob");

db.WhenSql(_ => true)
  .ReturnsAffectedRowsSequence(1, 1, 0);
```

nullable scalar の戻り値が `null` でよい場合は、未登録のままでも構文解析後に `null` が返ります。

```csharp
int? parentId = db.Scalar<int?>("""
    SELECT ParentCustomerId
    FROM dbo.Customers
    WHERE Id = @Id
    """);
```

`VerifyAllSqlExpectations()` は、登録した rule が本当に呼ばれたことも確認したい場合だけ呼びます。構文確認だけが目的なら必須ではありません。

## 8. 未登録 SQL の既定動作

`new SqlMockRouter()` の既定動作は、未登録 SQL でも安全に返せる範囲だけ fallback します。

| 呼び出し | 未登録 SQL の既定動作 |
| --- | --- |
| `ExecuteCommand(sql)` | 構文解析・正規化・履歴記録だけ行って成功 |
| `Scalar<int?>(sql)` | 構文解析・正規化・履歴記録後に `null` を返す |
| `Scalar<string?>(sql)` | 構文解析・正規化・履歴記録後に `null` を返す |
| `Scalar<int>(sql)` | 返す値を決められないため失敗 |
| `ExecuteNonQuery(sql)` | affected rows を決められないため失敗 |

reference type の scalar は、C# の nullable annotation を実行時に厳密判定しないため null 返却可能な型として扱います。

すべての未登録 SQL を失敗させたい場合:

```csharp
private readonly SqlMockRouter _router =
    new(UnmatchedSqlBehavior.Strict);
```

戻り値なし command だけを未登録許可し、nullable scalar は未登録時に失敗させたい場合:

```csharp
private readonly SqlMockRouter _router =
    new(UnmatchedSqlBehavior.ValidateOnlyForCommands);
```

## 9. 同じ SQL 分類が複数回呼ばれる場合

同じ matcher に複数回一致する場合は sequence を使います。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Jobs") && q.WhereUses("Id"))
  .ReturnsScalarSequence("Pending", "Ready");
```

1 回目は `Pending`、2 回目は `Ready` を返します。sequence を使い切った後の追加呼び出しは失敗します。

更新件数も同じ考え方です。

```csharp
db.WhenSql(q => q.IsUpdate("dbo.Customers"))
  .ReturnsAffectedRowsSequence(0, 1);
```

## 10. 何を検証するか

検証するもの:

- SQL Server 2022 / ScriptDom `TSql160Parser` で parse できるか
- `GO` を含まない single batch command text か
- 正規化前後の AST fingerprint が一致するか
- Mock 分岐に使う statement kind、table、column、parameter metadata

検証しないもの:

- table や column が実 DB に存在するか
- 型、権限、constraint、実行計画
- 動的 SQL 文字列の内部
- DB メタデータを使った alias 完全解決

`EXEC(N'SELECT FROM WHERE')` のような動的 SQL は、外側の `EXEC(...)` が valid なら通ります。内部文字列も検証したい場合は、その文字列を別途 `Assert.IsValidSql` に渡します。

## 11. 導入後の確認

導入先で確認すること:

- `Assert.IsValidSql("SELECT 1")` が通る
- `Assert.IsValidSql("SELECT FROM WHERE")` が `AssertFailedException` で失敗する
- `MockAppDb` 経由の SQL が履歴に記録される
- `VerifyAllSqlExpectations()` が未使用 rule を検出する
- 未登録 SQL の既定動作がプロジェクトのテスト方針に合っている

導入先でも SqlTestSupport の自己検証を実行したい場合は、`dist/SqlTestSupport.Tests.cs` を追加して `dotnet test` を実行します。

## 12. よくある調整

既存 DB クラスに `Execute(string sql, object? parameters = null)` 以外の overload がある場合でも、最終的に第一引数の SQL 文字列が共通実行メソッドへ渡るなら、その境界だけを override します。

```csharp
public override int Execute(string sql, object? parameters = null, int timeoutSeconds = 30)
    => _router.ExecuteNonQuery(sql);
```

SQL 形状だけでは分岐しづらい場合は、escape hatch として `NormalizedSql` を使えます。

```csharp
db.WhenSql(q =>
    q.IsSelectFrom("dbo.Customers") &&
    q.NormalizedSql.Contains("WITH (UPDLOCK)", StringComparison.OrdinalIgnoreCase))
  .ReturnsScalar("Alice");
```

ただし、標準の分岐面は `StatementKind`、`TargetTables`、`ReferencedTables`、`SelectedColumns`、`WhereColumns`、`ParameterNames` に寄せます。
