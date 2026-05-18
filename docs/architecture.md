# アーキテクチャ

開発時は責務ごとにファイルを分け、導入時は bootstrap ツールで単一ファイルにまとめる構成です。bundle 生成時は `using` と assembly attribute を集約し、各 source file の namespace wrapper を外して 1 つの namespace block に再構成します。

## ランタイム構成

```text
SqlAssertFacade
  SQL 検証エラーを MSTest の AssertFailedException へ変換する。

SqlValidationService
  Analyze、Normalize、Inspect の処理順を調停する。

SqlServer2022SyntaxAnalyzer
  ScriptDom を使って SQL Server 2022 構文として parse する。

SqlServer2022Normalizer
  正規化 SQL を生成し、AST fingerprint が変わらないことを検証する。

SqlAstFingerprinter
  ScriptDom AST から構造 hash を生成する。

SqlInspectionService
  Mock 分岐用のメタデータを AST から抽出する。

SqlMockRouter
  WhenSql ルールを評価し、登録済みの Mock 戻り値を返す。
```

## 正規化の契約

正規化は fail-closed です。

```text
original SQL
  -> Sql160 として parse
  -> original AST fingerprint
  -> normalized SQL を生成
  -> normalized SQL を Sql160 として再 parse
  -> normalized AST fingerprint
  -> fingerprint 比較
  -> 一致した場合だけ normalized SQL を返す
```

fingerprint が一致しない場合は `SqlNormalizationChangedAstException` を投げ、生成済み SQL は返しません。

## Fingerprint の対象

含める情報:

- AST ノード型
- public な意味的プロパティ
- enum 値
- 文字列、数値、boolean 値
- 子ノードの順序
- ScriptDom が公開する識別子値と quote 情報
- literal 値と式構造

除外する情報:

- 行番号、列番号
- offset、token index
- token stream
- 空白
- コメント

fingerprint は正規化で AST 構造が変わっていないことを検出するための構造ガードです。DB メタデータ上の意味、権限、実行時挙動までは証明しません。

## SQL 方言

対象は次で固定します。

```text
SQL Server 2022
ScriptDom SqlVersion.Sql160
SqlEngineType.Standalone
QUOTED_IDENTIFIER ON
```

`GO` は SQL Server Management Studio などのクライアント側バッチ区切りであり、通常の command text API では扱いづらいため拒否します。

## Inspection metadata の扱い

`SqlInspectionResult` と `SqlInvocation` は次を公開します。

- `StatementKind`
- `TargetTables`
- `ReferencedTables`
- `SelectedColumns`
- `WhereColumns`
- `ParameterNames`
- `NormalizedSql`
- `Fingerprint`

alias 解決は浅く扱います。たとえば `c.Name` は `c.Name` のまま保持し、`c` が `dbo.Customers` であることを DB 接続なしに解決しません。
