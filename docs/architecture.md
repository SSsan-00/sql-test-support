# アーキテクチャ

開発時は責務ごとにファイルを分け、導入時は bootstrap ツールで単一ファイルにまとめる構成です。bundle 生成時は `using` を集約し、各 source file の namespace wrapper を外して 1 つの namespace block に再構成します。

## ランタイム構成

```text
SqlAssertFacade
  SQL 検証エラーを MSTest の AssertFailedException へ変換する。

SqlValidationService
  Analyze と Inspect の呼び出し口を提供する。

SqlServer2022SyntaxAnalyzer
  ScriptDom を使って SQL Server 2022 構文として parse する。

SqlInspectionService
  Mock 分岐用のメタデータを元 SQL の AST から抽出する。

SqlMockRouter
  WhenSql ルールを評価し、登録済みの Mock 戻り値または既定 fallback を返す。
```

正規化と AST fingerprint は runtime pipeline から外しています。構文検証が主目的であり、SQL の意味が変わらない正規化を DB 接続なしに保証するより、元 SQL の AST をそのまま Mock 分岐に使う方針です。

## SQL 方言

対象は次で固定します。

```text
SQL Server 2022
ScriptDom TSql160Parser
QUOTED_IDENTIFIER ON 相当
single batch command text
```

`GO` は SQL Server Management Studio などのクライアント側バッチ区切りであり、通常の command text API では扱いづらいため拒否します。

## Inspection metadata の扱い

`SqlInspectionResult` と `SqlInvocation` は次を公開します。

- `StatementKind`
- `TargetTables`
- `ReferencedTables`
- `JoinedTables`
- `SelectedColumns`
- `WhereColumns`
- `OrderByColumns`
- `GroupByColumns`
- `HavingColumns`
- `HavingFunctions`
- `ParameterNames`
- `OriginalSql`
- `CallIndex`

alias 解決は浅く扱います。たとえば `c.Name` は `c.Name` と `Name` を保持しますが、`c` が `dbo.Customers` であることを DB 接続なしに完全解決しません。

## Mock router の責務

`SqlMockRouter` は次の順序で処理します。

```text
SQL string
  -> validate / parse
  -> AST metadata を抽出
  -> invocation history に記録
  -> WhenSql ルールを登録順に評価
  -> 登録済みの戻り値、または既定 fallback を返す
```

未登録 SQL でも解析は必ず行われます。構文確認だけが目的なら `WhenSql` を登録しなくても、`ExecuteResult<object?>` や独自コレクション戻り値でテスト対象メソッドを進められます。

## Bootstrap 方針

導入先のレビューを軽くするため、runtime と self-test をそれぞれ単一ファイルにまとめます。展開後ソースには導入先プロジェクトと衝突しやすい `#nullable enable` や assembly attribute を出力しません。
