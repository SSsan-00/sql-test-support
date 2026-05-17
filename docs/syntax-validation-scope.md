# 構文検証の範囲

## 何を検証するか

このヘルパーは、渡された SQL 文字列が SQL Server 2022 の T-SQL 文法として ScriptDom で parse できるかを検証します。

実装上は `TSql160Parser` を使います。

```text
SQL Server 2022
ScriptDom SqlVersion.Sql160
QUOTED_IDENTIFIER ON 相当
single batch command text
```

そのため、プロダクションコードからさまざまな T-SQL 文字列が渡されても、ScriptDom が SQL Server 2022 の T-SQL として解釈できる単一 batch の SQL であれば構文検証できます。

対象になり得るもの:

- `SELECT`
- `INSERT`
- `UPDATE`
- `DELETE`
- `MERGE`
- `EXEC`
- DDL
- transaction statement
- CTE
- subquery
- join
- function call
- SQL Server 2022 の ScriptDom grammar が扱える T-SQL

## 何を検証しないか

これは SQL Server 実行結果の検証ではありません。検証対象は文法です。

検証しないもの:

- table が存在するか
- column が存在するか
- schema が存在するか
- 型が合うか
- 権限があるか
- constraint に違反しないか
- lock hint や query hint が実行時に意味を持つか
- execution plan
- SQL Server 設定や compatibility level 依存の実行時挙動

たとえば次の SQL は、実 DB に `MissingTable` がなくても構文として valid なら通ります。

```sql
SELECT Id
FROM dbo.MissingTable;
```

## 明示的に拒否するもの

`GO` で区切られた複数 batch は拒否します。

```sql
SELECT 1;
GO
SELECT 2;
```

`GO` は SQL Server の文法要素ではなく、SSMS や sqlcmd などのクライアント側 batch separator です。通常の command text 実行では扱わないため、このヘルパーでも対象外にします。

空文字、null 相当、空白だけの SQL も拒否します。

## 動的 SQL の扱い

動的 SQL の文字列内部は検証しません。

```sql
EXEC(N'SELECT FROM WHERE');
```

この場合、外側の `EXEC(...)` が T-SQL として parse できれば、内部文字列 `SELECT FROM WHERE` の文法までは検証しません。内部文字列も検証したい場合は、呼び出し側でその文字列を別途 `Assert.IsValidSql` または `SqlValidationService.Analyze` に渡します。

## 正規化の安全性

`NormalizeSql` と Mock router 内部の正規化は fail-closed です。

```text
original SQL
  -> parse
  -> original AST fingerprint
  -> normalized SQL を生成
  -> normalized SQL を再 parse
  -> normalized AST fingerprint
  -> fingerprint が一致した場合だけ返す
```

fingerprint が一致しない場合、正規化後 SQL は返しません。

## Mock 分岐 metadata の範囲

構文検証は ScriptDom の parse 結果全体に対して行います。一方で、Mock 分岐用 metadata の抽出は初期版として実用範囲に絞っています。

抽出するもの:

- `StatementKind`
- `TargetTables`
- `ReferencedTables`
- `SelectedColumns`
- `WhereColumns`
- `ParameterNames`

主に `SELECT`、`INSERT`、`UPDATE`、`DELETE`、`MERGE` の Mock 分岐を想定します。DDL や特殊な T-SQL は構文検証できても、Mock 分岐用 metadata が `Unknown` や空集合になる場合があります。その場合は `NormalizedSql.Contains(...)` を escape hatch として使うか、inspection の visitor を拡張します。
