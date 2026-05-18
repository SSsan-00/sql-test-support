# Bootstrap 設計

開発時は TDD と保守性を優先して複数ファイルに分けます。一方、既存テストプロジェクトへ導入する時は、レビューしやすい単一ファイルを好む場合があります。bootstrap ツールはその導入経路を支えるものです。

## 生成ファイル

bootstrap を実行すると次を生成します。

```text
dist/SqlTestSupport.cs
dist/SqlTestSupport.Tests.cs
```

加えて、リポジトリ外でも成果物を展開できる単一ファイル bootstrap として、次のファイルも生成できます。

```text
bootstrap/SqlTestSupport.expand.sh
bootstrap/SqlTestSupport.Bootstrap.cs
dist/SqlTestSupport.Directory.Build.targets
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

`SqlTestSupport.Tests.cs` には、本体ヘルパーと最小 Mock DB 統合例を検証する MSTest をまとめます。導入先で自己検証したい場合だけ追加します。

## 実行方法

```bash
dotnet run --project tools/SqlTestSupport.Bootstrap/SqlTestSupport.Bootstrap.csproj
```

または:

```bash
./bootstrap/bootstrap.sh
```

単一ファイル bootstrap も同時に生成する場合:

```bash
./bootstrap/bootstrap.sh \
  --self-contained-script bootstrap/SqlTestSupport.expand.sh \
  --self-contained-targets dist/SqlTestSupport.Directory.Build.targets \
  --self-contained-csharp bootstrap/SqlTestSupport.Bootstrap.cs
```

生成済みの shell 版単一ファイル bootstrap から導入先へ展開する場合:

```bash
./bootstrap/SqlTestSupport.expand.sh /path/to/test-project/SqlTestSupport
```

引数を省略すると、カレントディレクトリ配下の `dist` に展開します。



### リポジトリをダウンロードできない場合

リポジトリ全体を取得できないユーザーは、Web UI で `bootstrap/SqlTestSupport.Bootstrap.cs` を開き、その内容だけをコピーしてビルドできます。この C# bootstrap は runtime bundle、test bundle、MSBuild targets を埋め込んでいるため、元リポジトリは不要です。

1. 任意の一時フォルダーで console app を作成します。

   ```bash
   mkdir SqlTestSupportBootstrap
   cd SqlTestSupportBootstrap
   dotnet new console --force
   ```

2. 生成された `Program.cs` を、コピーした `bootstrap/SqlTestSupport.Bootstrap.cs` の内容で置き換えます。
3. 導入先ディレクトリを指定して実行します。

   ```bash
   dotnet run -- /path/to/test-project/SqlTestSupport
   ```

   引数を省略すると、一時フォルダー配下の `dist` に展開します。self-test や targets が不要な場合は次の option を使えます。

   ```bash
   dotnet run -- /path/to/test-project/SqlTestSupport --skip-tests
   dotnet run -- /path/to/test-project/SqlTestSupport --skip-targets
   ```

4. 通常の単一 C# ファイルとして使う場合は、生成された `SqlTestSupport.cs` をテストプロジェクトに追加します。
5. ビルド時に自動展開したい場合は、生成された `SqlTestSupport.Directory.Build.targets` をテストプロジェクトと同じディレクトリへ `Directory.Build.targets` という名前でコピーし、`dotnet build` または `dotnet test` を実行します。既存の `Directory.Build.targets` がある場合は、生成ファイルを `SqlTestSupport.Directory.Build.targets` という名前で置き、既存ファイルから `<Import Project="SqlTestSupport.Directory.Build.targets" />` を追加します。


ビルド時に自動展開する単一ファイル bootstrap を使う場合は、次の 1 ファイルだけを導入先テストプロジェクトと同じディレクトリへ `Directory.Build.targets` という名前で配置します。

```text
dist/SqlTestSupport.Directory.Build.targets -> /path/to/test-project/Directory.Build.targets
```

その後、通常どおり `dotnet build` または `dotnet test` を実行します。MSBuild はこの targets file を自動 import し、埋め込み済みの `SqlTestSupport.cs` を `obj/SqlTestSupport/SqlTestSupport.cs` に展開して compile item に追加します。導入先に `Directory.Build.targets` がすでにある場合は、既存ファイルへ次の import を追加してください。

```xml
<Import Project="SqlTestSupport.Directory.Build.targets" />
```

この場合は、`dist/SqlTestSupport.Directory.Build.targets` を `SqlTestSupport.Directory.Build.targets` という名前で既存 `Directory.Build.targets` と同じディレクトリへコピーします。

## Bundle ルール

bootstrap ツールは次のルールでファイルをまとめます。

- `src/SqlTestSupport/**/*.cs` を対象にする
- `tests/SqlTestSupport.Tests/**/*.cs` を対象にする
- 導入先プロジェクトの assembly 設定と衝突しないよう `MSTestSettings.cs` は除外する
- top-level `using` を集約する
- 重複 `using` を除外する
- 各 source file の namespace wrapper は外し、bundle 全体を 1 つの namespace block にまとめる
- 型定義、メソッド、コメントは source file 側の内容を保持する
- `#nullable enable` を出力する
- UTF-8 without BOM で生成する

## 単一ファイル bootstrap

`--self-contained-script <path>` を指定すると、直前に生成した `dist/SqlTestSupport.cs` と `dist/SqlTestSupport.Tests.cs` を base64 として埋め込んだ shell script を出力します。この script は .NET SDK や元リポジトリを必要とせず、script 単体で 2 つの C# ファイルを指定ディレクトリへ展開します。

`--self-contained-csharp <path>` を指定すると、同じ bundle と MSBuild targets 生成処理を埋め込んだ C# source file を出力します。shell script を実行しづらい環境や、リポジトリをダウンロードできず Web UI から source だけをコピーする導入経路ではこちらを使います。

shell 版は `SqlTestSupport.cs` と `SqlTestSupport.Tests.cs` だけを出力します。C# bootstrap は既定で次の 3 ファイルを出力します。

```text
SqlTestSupport.cs
SqlTestSupport.Tests.cs
SqlTestSupport.Directory.Build.targets
```

ビルド時自動展開を使う場合は、`dist/SqlTestSupport.Directory.Build.targets` または C# bootstrap が生成した `SqlTestSupport.Directory.Build.targets` を導入先に配置します。

生成ファイルは成果物です。開発は分割された source files 側で続けます。

### ビルド時自動展開 targets

`--self-contained-targets <path>` を指定すると、runtime bundle と test bundle を base64 として埋め込んだ MSBuild targets file を出力します。

この targets file は、導入先ビルド中に次の処理を行います。

- `Microsoft.SqlServer.TransactSql.ScriptDom` と `MSTest.TestFramework` の package reference を追加する
- `obj/SqlTestSupport/SqlTestSupport.cs` へ runtime bundle を展開する
- 展開した runtime bundle を `Compile` item に追加する
- `SqlTestSupportIncludeSelfTests=true` のときだけ `obj/SqlTestSupport/SqlTestSupport.Tests.cs` も展開して `Compile` item に追加する

主な切り替え property:

```xml
<PropertyGroup>
  <!-- 自動展開を止めたい場合 -->
  <SqlTestSupportExpandOnBuild>false</SqlTestSupportExpandOnBuild>

  <!-- 埋め込み self-test も compile したい場合 -->
  <SqlTestSupportIncludeSelfTests>true</SqlTestSupportIncludeSelfTests>

  <!-- package reference を導入先 csproj 側で管理したい場合 -->
  <SqlTestSupportAddPackageReferences>false</SqlTestSupportAddPackageReferences>
</PropertyGroup>
```

## 導入チェックリスト

### A. 通常の単一 C# ファイルとして導入する場合

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

### B. 1 ファイルだけ置いてビルド時に自動展開する場合

1. `dist/SqlTestSupport.Directory.Build.targets` を導入先テストプロジェクトと同じディレクトリへ `Directory.Build.targets` としてコピーする
2. 導入先プロジェクトで通常どおり `dotnet build` または `dotnet test` を実行する
3. ビルド後、必要に応じて `obj/SqlTestSupport/SqlTestSupport.cs` の展開結果を確認する
4. 導入先に既存の `Directory.Build.targets` がある場合は、上書きせず `SqlTestSupport.Directory.Build.targets` としてコピーし、既存 `Directory.Build.targets` から `<Import Project="SqlTestSupport.Directory.Build.targets" />` で読み込む
