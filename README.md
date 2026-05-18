# SqlTestSupport

SqlTestSupport は、MSTest を使う .NET 9 テストプロジェクト向けの SQL 検証・正規化・DB Mock 支援ライブラリです。

プロダクションコードに SQL Server 向け T-SQL がベタ書きされていて、既存の DB 実行クラスを継承・override した Mock DB で差し替える構成を想定しています。

## 目的

- Microsoft ScriptDom で T-SQL 構文を検証する
- SQL Server 2022 構文、つまり `SqlVersion.Sql160` を対象に固定する
- AST fingerprint が変わらない場合だけ SQL を正規化する
- Mock 分岐に使う AST 由来の情報を抽出する
  - statement kind
  - target tables
  - referenced tables
  - selected columns
  - where columns
  - parameter names
- DB テストダブル向けに `WhenSql(...).Returns...` 形式のルーターを提供する
- 既存テストプロジェクトへ導入しやすいように、bootstrap で単一ファイル成果物を生成する

## 対象外

- SQL Server へ接続しない
- テーブル存在、カラム存在、権限、型互換性は検証しない
- DB メタデータを使った alias 完全解決は行わない
- 動的 SQL 文字列の内部までは検証しない
- command text として実行しづらい `GO` バッチ区切りは扱わない
- async API は提供しない。parse、正規化、fingerprint はメモリ内の同期処理

## 公開 API

既存の独自 `Assert` クラスへ、次の 2 メソッドだけを追加する想定です。

```csharp
public static void IsValidSql(string sql, string? message = null)
    => SqlAssertFacade.IsValidSql(sql, message);

public static string NormalizeSql(string sql, string? message = null)
    => SqlAssertFacade.NormalizeSql(sql, message);
```

テストコード側では次の形で使います。

```csharp
Assert.IsValidSql(sql);
var normalized = Assert.NormalizeSql(sql);
```

Mock DB では `SqlMockRouter` を持たせ、第一引数の SQL だけを渡します。

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

Mock の振る舞いは、解析済み SQL に対する条件で登録します。

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsScalar("Alice");

db.WhenSql(q => q.IsUpdate("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsAffectedRows(1);
```

戻り値なし SQL 実行だけ、未登録 SQL を構文解析のみで通す mode もあります。

```csharp
var router = new SqlMockRouter(UnmatchedSqlBehavior.ValidateOnlyForCommands);
router.ExecuteCommand("UPDATE dbo.Customers SET Name = @Name WHERE Id = @Id");
```

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

.NET SDK や元リポジトリなしで生成済みソースを展開したい場合は、単一ファイル bootstrap を使えます。

```bash
./bootstrap/SqlTestSupport.expand.sh /path/to/test-project/SqlTestSupport
```

リポジトリをダウンロードできない環境では、GitHub などの Web UI で `bootstrap/SqlTestSupport.Bootstrap.cs` の中身をコピーし、任意の一時フォルダーで console app の `Program.cs` として貼り付けることで、導入用ファイルを生成できます。

```bash
mkdir SqlTestSupportBootstrap
cd SqlTestSupportBootstrap
dotnet new console --force
# Program.cs を bootstrap/SqlTestSupport.Bootstrap.cs の内容で置き換える
dotnet run -- /path/to/test-project/SqlTestSupport
```

生成された `SqlTestSupport.cs` をテストプロジェクトへ追加してください。ビルド時に自動展開したい場合は、同時に生成される `SqlTestSupport.Directory.Build.targets` をテストプロジェクトと同じディレクトリへ `Directory.Build.targets` という名前でコピーしてから `dotnet build` または `dotnet test` を実行します。

「単一ファイルだけを置いて、ビルド時に展開済みソースを使いたい」場合は、`dist/SqlTestSupport.Directory.Build.targets` を導入先テストプロジェクトと同じディレクトリに `Directory.Build.targets` という名前でコピーしてから通常どおりビルドします。MSBuild が `obj/SqlTestSupport/SqlTestSupport.cs` を自動生成し、その生成済みソースを compile item に追加します。

`SqlTestSupport.cs` は導入先で利用する本体ファイルです。`SqlTestSupport.Tests.cs` は、導入先でも同じ仕様を検証したい場合に使う MSTest の単一ファイルです。

## 開発

```bash
dotnet restore
dotnet test
dotnet run --project tools/SqlTestSupport.Bootstrap/SqlTestSupport.Bootstrap.csproj
./bootstrap/bootstrap.sh \
  --self-contained-script bootstrap/SqlTestSupport.expand.sh \
  --self-contained-targets dist/SqlTestSupport.Directory.Build.targets \
  --self-contained-csharp bootstrap/SqlTestSupport.Bootstrap.cs
```

## ドキュメント

- [アーキテクチャ](docs/architecture.md)
- [API リファレンス](docs/api.md)
- [構文検証の範囲](docs/syntax-validation-scope.md)
- [テストメソッドでの使い方](docs/test-method-usage.md)
- [Mock DB 連携](docs/mock-db-integration.md)
- [Bootstrap 設計](docs/bootstrap.md)
- [テスト方針](docs/testing.md)
