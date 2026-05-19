# SqlTestSupport

SqlTestSupport は、MSTest を使う .NET 9 テストプロジェクト向けの SQL 構文検証・DB Mock 支援ライブラリです。

プロダクションコードに SQL Server 向け T-SQL がベタ書きされていて、既存の DB 実行クラスを継承・override した Mock DB で差し替える構成を想定しています。

導入からテスト記述までを 1 ファイルで確認したい場合は、まず [利用手順](USAGE.md) を参照してください。

## 目的

- Microsoft ScriptDom で T-SQL 構文を検証する
- SQL Server 2022 構文、つまり `TSql160Parser` を対象に固定する
- Mock 分岐に使う AST 由来の情報を抽出する
  - statement kind
  - target tables
  - referenced tables
  - joined tables
  - selected columns
  - where columns
  - order by columns
  - group by columns
  - having columns / functions
  - parameter names
- DB テストダブル向けに `WhenSql(...).ReturnsResult(...)` 形式のルーターを提供する
- 既存テストプロジェクトへ導入しやすいように、bootstrap で単一ファイル成果物を生成する

正規化と AST fingerprint 比較は廃止しています。主目的は「SQL 文字列として構文が正しいか」の検証であり、Mock 分岐は正規化済み文字列ではなく、元 SQL の AST metadata で行います。

## 対象外

- SQL Server へ接続しない
- テーブル存在、カラム存在、権限、型互換性は検証しない
- DB メタデータを使った alias 完全解決は行わない
- 動的 SQL 文字列の内部までは検証しない
- command text として実行しづらい `GO` バッチ区切りは扱わない
- async API は提供しない。parse と AST inspection はメモリ内の同期処理

## 公開 API

既存の独自 `Assert` クラスへ、次の forwarding method を追加する想定です。

```csharp
public static void IsValidSql(string sql, string? message = null)
    => SqlAssertFacade.IsValidSql(sql, message);
```

テストコード側では次の形で使います。

```csharp
Assert.IsValidSql(sql);
```

Mock DB では `SqlMockRouter` を持たせ、第一引数の SQL だけを渡します。

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

Mock の振る舞いは、解析済み SQL に対する条件で登録します。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsResult("Alice");

db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.JoinsTable("dbo.Orders"))
  .ReturnsResult(new CustomerRows());
```

未登録 SQL も、既定では安全に返せる範囲だけ fallback します。

```csharp
object? value = router.ExecuteResult<object?>(
    "SELECT ParentCustomerId FROM dbo.Customers WHERE Id = @Id");

CustomerRows rows = router.ExecuteResult<CustomerRows>(
    "SELECT Id, Name FROM dbo.Customers");
```

`ExecuteResult<object?>` は構文解析・履歴記録後に `null` を返します。`Dictionary` 継承または `IDictionary<TKey,TValue>` 実装の具象クラスは、未登録なら `new()` した空コレクションを返します。`int` のように戻り値を安全に決められない型は未登録なら失敗します。

## Bootstrap

```bash
./bootstrap/bootstrap.sh
```

または:

```bash
dotnet run --project tools/SqlTestSupport.Bootstrap/SqlTestSupport.Bootstrap.csproj
```

単一ファイル bootstrap も更新する場合:

```bash
./bootstrap/bootstrap.sh \
  --self-contained-script bootstrap/SqlTestSupport.expand.sh \
  --self-contained-targets dist/SqlTestSupport.Directory.Build.targets \
  --self-contained-csharp bootstrap/SqlTestSupport.Bootstrap.cs
```

生成物:

```text
dist/SqlTestSupport.cs
dist/SqlTestSupport.Tests.cs
dist/SqlTestSupport.Directory.Build.targets
bootstrap/SqlTestSupport.Bootstrap.cs
```

通常導入では、生成された `SqlTestSupport.cs` をテストプロジェクトへ追加します。導入先でも自己検証したい場合だけ `SqlTestSupport.Tests.cs` も追加します。

.NET SDK や元リポジトリなしで生成済みソースを展開したい場合は、単一ファイル bootstrap を使えます。

```bash
./bootstrap/SqlTestSupport.expand.sh /path/to/test-project/SqlTestSupport
```

## 開発

```bash
dotnet restore
dotnet test
./bootstrap/bootstrap.sh \
  --self-contained-script bootstrap/SqlTestSupport.expand.sh \
  --self-contained-targets dist/SqlTestSupport.Directory.Build.targets \
  --self-contained-csharp bootstrap/SqlTestSupport.Bootstrap.cs
```

## ドキュメント

- [利用手順](USAGE.md)
- [アーキテクチャ](docs/architecture.md)
- [API リファレンス](docs/api.md)
- [構文検証の範囲](docs/syntax-validation-scope.md)
- [テストメソッドでの使い方](docs/test-method-usage.md)
- [Mock DB 連携](docs/mock-db-integration.md)
- [Bootstrap 設計](docs/bootstrap.md)
- [テスト方針](docs/testing.md)
