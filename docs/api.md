# API リファレンス

## SqlAssertFacade

```csharp
SqlAssertFacade.IsValidSql(sql);
```

既存の独自 `Assert` クラスから呼ぶ facade です。検証失敗時は MSTest の `AssertFailedException` に変換します。

正規化APIは提供しません。主目的は構文検証であり、Mock 分岐は正規化済み文字列ではなく AST metadata を使います。

## SqlValidationService

```csharp
var analysis = service.Analyze(sql);
var inspection = service.Inspect(sql);
```

- `Analyze`: SQL を SQL Server 2022 の T-SQL として parse し、ScriptDom AST を返す
- `Inspect`: SQL を parse し、Mock 分岐に使う AST metadata を抽出する

構文検証の対象範囲は [構文検証の範囲](syntax-validation-scope.md) を参照します。テストメソッドでの使い方は [テストメソッドでの使い方](test-method-usage.md) にまとめています。

## SqlMockRouter

```csharp
var router = new SqlMockRouter();

router.WhenSql(q => q.IsSelectFrom("dbo.Customers"))
      .ReturnsResult("Alice");

var name = router.ExecuteResult<string>("SELECT Name FROM dbo.Customers");

router.VerifyAll();
```

`ExecuteResult<T>` は SQL を必ず rule matching 前に構文解析します。invalid SQL は登録 rule の有無に関係なく `AssertFailedException` になります。

未登録 SQL の既定動作:

- `object?`、nullable value type、reference type は `null` を返す
- `Dictionary` 継承または `IDictionary<TKey,TValue>` 実装の具象クラスは、public parameterless constructor があれば空の `new()` を返す
- `int` などの非 nullable value type は、返す値を決められないため失敗する

登録メソッド:

```csharp
ReturnsResult(object? value)
ReturnsResultSequence(params object?[] values)
```

実行メソッド:

```csharp
ExecuteResult<T>(string sql)
```

検証メソッド:

```csharp
VerifyAll()
```

`VerifyAll()` は、登録した `WhenSql` rule が少なくとも 1 回呼ばれたことを検証します。

## SqlInvocation

`SqlInvocation` は `WhenSql` の predicate に渡される解析済み SQL 情報です。

matcher helper:

```csharp
IsSelectFrom(table)
IsInsertInto(table)
IsUpdate(table)
IsDeleteFrom(table)
WhereUses(column)
SelectsColumn(column)
ReferencesTable(table)
TargetsTable(table)
JoinsTable(table)
OrdersBy(column)
GroupsBy(column)
HavingUses(column)
HavingCalls(functionName)
HasParameter(parameterName)
```

主なプロパティ:

```csharp
OriginalSql
StatementKind
TargetTables
ReferencedTables
JoinedTables
SelectedColumns
WhereColumns
OrderByColumns
GroupByColumns
HavingColumns
HavingFunctions
ParameterNames
CallIndex
```

識別子比較は大文字小文字を無視します。`dbo.Customers` と `Customers` のような schema 付き・なしの差も matcher で軽く吸収します。
