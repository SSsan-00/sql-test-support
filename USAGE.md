# 利用手順

このファイルは、SqlTestSupport を既存の MSTest プロジェクトへ導入し、SQL 構文検証と Mock DB 分岐を使い始めるための手順を 1 つにまとめたものです。

## 前提

- .NET 9 の MSTest プロジェクト
- SQL Server 2022 向け T-SQL
- 本番コードの SQL 実行メソッドは、最終的に第一引数に SQL 文字列を受け取る
- テストでは本番 DB クラスを継承し、SQL 実行メソッドだけを override できる
- 本番 DB メソッドの戻り値は主に `object?` または独自コレクション型

検証は SQL Server への接続を行いません。ScriptDom で文法を検証し、Mock 分岐に必要な metadata を AST から抽出します。正規化は行いません。

## 1. 導入方法を選ぶ

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

リリースで配布された単一実行ファイルを使う場合:

```bash
# Windows
SqlTestSupport.Bootstrap.exe C:\path\to\test-project\SqlTestSupport

# macOS / Linux
./SqlTestSupport.Bootstrap /path/to/test-project/SqlTestSupport
```

この実行ファイルは runtime bundle、test bundle、MSBuild targets を内包しています。導入先には `SqlTestSupport.cs`、`SqlTestSupport.Tests.cs`、`SqlTestSupport.Directory.Build.targets` が展開されます。self-test や targets が不要な場合は次の option を使えます。

```bash
./SqlTestSupport.Bootstrap /path/to/test-project/SqlTestSupport --skip-tests
./SqlTestSupport.Bootstrap /path/to/test-project/SqlTestSupport --skip-targets
```

## 2. 独自 Assert クラスに forwarding method を追加する

テストコードからは `Assert.IsValidSql(sql)` で呼べる形にします。

```csharp
public static void IsValidSql(string sql, string? message = null)
    => SqlAssertFacade.IsValidSql(sql, message);
```

構文不正は MSTest の `AssertFailedException` として失敗します。メッセージには SQL Server 2022 固定で検証したこと、ScriptDom の parse error、対象 SQL が含まれます。

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

使いどころ:

- テストデータ作成 SQL の文法確認
- helper 内に置いた SQL literal の文法確認
- DB Mock を通らない SQL の軽い構文確認

## 4. Mock DB を作る

本番 DB クラスが次のような実行メソッドを持つ前提です。

```csharp
public class AppDb
{
    public virtual object? Execute(string sql, object? parameters = null)
    {
        throw new NotImplementedException();
    }

    public virtual CustomerRows QueryRows(string sql, object? parameters = null)
    {
        throw new NotImplementedException();
    }

    public virtual object? get_value(string columns, string table, string where)
    {
        var sql = $"SELECT {columns} FROM {table} WHERE {where}";
        return Execute(sql);
    }
}
```

テスト側では SQL 実行境界だけを override し、第一引数の SQL を `SqlMockRouter` に渡します。

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

`get_value` が上の例のように virtual な `Execute(sql)` を呼ぶなら、`get_value` 自体を override しなくても Mock 側の `Execute` が呼ばれます。内部で非 virtual 実行メソッドを直接呼ぶ場合は、Mock DB で `get_value` も override し、組み立て後 SQL を `_router.ExecuteResult<object?>(sql)` に渡します。

## 5. Mock の振る舞いを登録する

`WhenSql` には、構文解析済みの `SqlInvocation` が渡されます。SQL 文字列の部分一致より、AST metadata を使った条件を優先します。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsResult("Alice");

db.WhenSql(q =>
      q.IsSelectFrom("dbo.Customers") &&
      q.JoinsTable("dbo.Orders") &&
      q.OrdersBy("Id"))
  .ReturnsResult(new CustomerRows());
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
q.JoinsTable("dbo.Orders")
q.OrdersBy("CreatedAt")
q.GroupsBy("CustomerId")
q.HavingUses("Id")
q.HavingCalls("COUNT")
q.HasParameter("@Id")
```

複数 rule が一致し得る場合は、より具体的な rule を先に登録します。router は登録順に最初に一致した rule を使います。

## 6. SQL の内容に応じて同じ関数の振る舞いを変える

同じ `Execute(sql)` でも、SQL 形状で戻り値を分岐できます。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsResult("Alice");

db.WhenSql(q => q.IsSelectFrom("dbo.Settings") && q.WhereUses("Key"))
  .ReturnsResult("Enabled");
```

`get_value("Name", "dbo.Customers", "Id = @Id")` のようなメソッドでも、最終的に組み立てられた SQL が router に渡れば同じ matcher を使えます。

```csharp
db.WhenSql(q =>
      q.IsSelectFrom("dbo.Customers") &&
      q.SelectsColumn("Name") &&
      q.WhereUses("Id"))
  .ReturnsResult("Alice");
```

## 7. Mock 振る舞いなしで構文だけ確認する

テスト対象メソッド内で実行される SQL の構文エラーだけを確認したい場合は、基本的に `WhenSql(...)` を登録しません。SQL は `SqlMockRouter` に渡った時点で必ず validate / inspect されるため、構文不正があれば `AssertFailedException` で失敗します。

### 戻り値が `object?` の実行メソッド

未登録 SQL は構文解析後に `null` を返します。

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

テスト対象を最後まで進めるために具体値が必要な場合は、広い rule でダミーを返します。

```csharp
db.WhenSql(_ => true)
  .ReturnsResult("dummy");
```

### 戻り値が独自コレクション型の実行メソッド

`Dictionary` 継承または `IDictionary<TKey,TValue>` 実装の具象クラスで、public parameterless constructor がある場合は、未登録 SQL に対して空の `new()` を返します。

```csharp
public sealed class CustomerRows : Dictionary<string, object?>
{
}
```

```csharp
[TestMethod]
public void Search_customer_sql_has_no_syntax_error()
{
    var db = new MockAppDb();
    var service = new CustomerService(db);

    var rows = service.SearchCustomers();

    Assert.IsEmpty(rows);
}
```

### 戻り値を安全に決められない型

`int` などの非 nullable value type は未登録 SQL では失敗します。今回の想定プロダクションコードでは使っていないため、更新件数専用 API は提供していません。必要な場合は `object?` 戻り値に寄せるか、明示的な rule で値を返します。

```csharp
db.WhenSql(_ => true)
  .ReturnsResult(1);
```

## 8. 同じ分類の SQL が複数回呼ばれる場合

同じ matcher に複数回一致する場合は sequence を使います。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Jobs") && q.WhereUses("Id"))
  .ReturnsResultSequence("Pending", "Ready");
```

1 回目は `Pending`、2 回目は `Ready` を返します。sequence を使い切った後の追加呼び出しは失敗します。これは「想定より多く SQL が呼ばれた」ことを検出するためです。

## 9. VerifyAll と Completes

`VerifyAllSqlExpectations()` は、登録した `WhenSql` rule が少なくとも 1 回呼ばれたことを検証します。登録した Mock 振る舞いが本当に使われたことまで確認したいテストで呼びます。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers"))
  .ReturnsResult("Alice");

var name = service.GetCustomerName(1);

Assert.AreEqual("Alice", name);
db.VerifyAllSqlExpectations();
```

構文確認だけが目的で `WhenSql` を登録していない場合、`VerifyAllSqlExpectations()` は必須ではありません。

`Completes()` は廃止しています。本番コードに戻り値なし SQL 実行メソッドがない前提に合わせ、API を `ExecuteResult<T>` と `ReturnsResult` に整理しています。

## 10. 未登録 SQL の既定動作

`new SqlMockRouter()` の既定動作は、未登録 SQL でも安全に返せる範囲だけ fallback します。

| 戻り値型 | 未登録 SQL の既定動作 |
| --- | --- |
| `object?` | 構文解析・履歴記録後に `null` を返す |
| nullable value type | 構文解析・履歴記録後に `null` を返す |
| reference type | 構文解析・履歴記録後に `null` を返す |
| `Dictionary` 継承の具象クラス | 構文解析・履歴記録後に空の `new()` を返す |
| `IDictionary<TKey,TValue>` 実装の具象クラス | 構文解析・履歴記録後に空の `new()` を返す |
| `int` などの非 nullable value type | 返す値を決められないため失敗 |

C# の nullable annotation は実行時に厳密判定できないため、reference type は null 返却可能な型として扱います。テスト対象が null を受け取れない場合は `WhenSql(...).ReturnsResult(...)` で明示値を返します。

## 11. 何を検証するか

検証するもの:

- SQL Server 2022 / ScriptDom `TSql160Parser` で parse できるか
- `GO` を含まない single batch command text か
- Mock 分岐に使う statement kind、table、column、parameter metadata

検証しないもの:

- table や column が実 DB に存在するか
- 型、権限、constraint、実行計画
- 動的 SQL 文字列の内部
- DB メタデータを使った alias 完全解決

`EXEC(N'SELECT FROM WHERE')` のような動的 SQL は、外側の `EXEC(...)` が valid なら通ります。内部文字列も検証したい場合は、その文字列を別途 `Assert.IsValidSql` に渡します。

## 12. 導入後の確認

導入先で確認すること:

- `Assert.IsValidSql("SELECT 1")` が通る
- `Assert.IsValidSql("SELECT FROM WHERE")` が `AssertFailedException` で失敗する
- `MockAppDb` 経由の SQL が履歴に記録される
- `VerifyAllSqlExpectations()` が未使用 rule を検出する
- 未登録 SQL の既定動作がプロジェクトのテスト方針に合っている

導入先でも SqlTestSupport の自己検証を実行したい場合は、`dist/SqlTestSupport.Tests.cs` を追加して `dotnet test` を実行します。
