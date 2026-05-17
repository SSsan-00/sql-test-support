# Bootstrap 設計

開発時は TDD と保守性を優先して複数ファイルに分けます。一方、既存テストプロジェクトへ導入する時は、レビューしやすい単一ファイルを好む場合があります。bootstrap ツールはその導入経路を支えるものです。

## 生成ファイル

bootstrap を実行すると次を生成します。

```text
dist/SqlTestSupport.cs
dist/SqlTestSupport.Tests.cs
```

`SqlTestSupport.cs` に含まれるもの:

- assert facade
- validation service
- syntax analyzer
- normalizer
- AST fingerprinter
- inspection service
- mock router
- models
- exceptions

`SqlTestSupport.Tests.cs` には、本体ヘルパーと最小 Mock DB 統合例を検証する MSTest をまとめます。

## 実行方法

```bash
dotnet run --project tools/SqlTestSupport.Bootstrap/SqlTestSupport.Bootstrap.csproj
```

または:

```bash
./bootstrap/bootstrap.sh
```

## Bundle ルール

bootstrap ツールは次のルールでファイルをまとめます。

- `src/SqlTestSupport/**/*.cs` を対象にする
- `tests/SqlTestSupport.Tests/**/*.cs` を対象にする
- 導入先プロジェクトの assembly 設定と衝突しないよう `MSTestSettings.cs` は除外する
- top-level `using` を集約する
- 重複 `using` を除外する
- namespace block と型定義は保持する
- `#nullable enable` を出力する
- UTF-8 without BOM で生成する

生成ファイルは成果物です。開発は分割された source files 側で続けます。

## 導入チェックリスト

1. `dist/SqlTestSupport.cs` を導入先のテストプロジェクトへ追加する
2. package reference を追加する

   ```xml
   <PackageReference Include="Microsoft.SqlServer.TransactSql.ScriptDom" Version="180.18.1" />
   <PackageReference Include="MSTest.TestFramework" Version="4.0.2" />
   ```

3. 既存の独自 `Assert` クラスへ forwarding method を 2 つ追加する

   ```csharp
   public static void IsValidSql(string sql, string? message = null)
       => SqlAssertFacade.IsValidSql(sql, message);

   public static string NormalizeSql(string sql, string? message = null)
       => SqlAssertFacade.NormalizeSql(sql, message);
   ```

4. 導入先で同じ仕様を検証したい場合は `dist/SqlTestSupport.Tests.cs` も追加する
