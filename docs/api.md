# API リファレンス

## SqlAssertFacade

```csharp
SqlAssertFacade.IsValidSql(sql);
var normalized = SqlAssertFacade.NormalizeSql(sql);
```

既存の独自 `Assert` クラスから呼ぶ facade です。検証失敗時は MSTest の `AssertFailedException` に変換します。

## SqlValidationService

```csharp
var analysis = service.Analyze(sql);
var normalization = service.Normalize(sql);
var inspection = service.Inspect(sql);
```

- `Analyze`: SQL を parse し、AST fingerprint を返す
- `Normalize`: 正規化 SQL を生成し、fingerprint が変わらないことを検証する
- `Inspect`: SQL を正規化し、Mock 分岐に使う AST metadata を抽出する

構文検証の対象範囲は [構文検証の範囲](syntax-validation-scope.md) を参照します。テストメソッドでの使い方は [テストメソッドでの使い方](test-method-usage.md) にまとめています。

## SqlMockRouter

```csharp
var router = new SqlMockRouter();

router.WhenSql(q => q.IsSelectFrom("dbo.Customers"))
      .ReturnsScalar("Alice");

var name = router.Scalar<string>("SELECT Name FROM dbo.Customers");

router.WhenSql(q => q.IsUpdate("dbo.Customers"))
      .Completes();

router.ExecuteCommand("UPDATE dbo.Customers SET Name = @Name WHERE Id = @Id");

router.VerifyAll();
```

既定の `SqlMockRouter()` は、未登録 SQL でも安全に返せる範囲だけ fallback します。

```csharp
router.ExecuteCommand("UPDATE dbo.Customers SET Name = @Name WHERE Id = @Id");
int? parentId = router.Scalar<int?>("SELECT ParentCustomerId FROM dbo.Customers WHERE Id = @Id");
```

- 未登録の `ExecuteCommand`: validate / normalize / inspect / history 記録だけ行って成功する
- 未登録の nullable `Scalar<T?>`: validate / normalize / inspect / history 記録後に `null` を返す
- 未登録の non-nullable `Scalar<T>`: 返す値を決められないため失敗する
- 未登録の `ExecuteNonQuery`: affected rows を決められないため失敗する

nullable reference type は実行時の `T` だけでは非 nullable reference type と区別しづらいため、reference type の scalar は null 返却可能な型として扱います。

未登録 SQL をすべて失敗させたい場合は、router 作成時に `Strict` を指定します。

```csharp
var router = new SqlMockRouter(UnmatchedSqlBehavior.Strict);
```

登録メソッド:

```csharp
ReturnsScalar(object? value)
ReturnsScalarSequence(params object?[] values)
ReturnsAffectedRows(int affectedRows)
ReturnsAffectedRowsSequence(params int[] affectedRows)
Completes()
```

実行メソッド:

```csharp
ExecuteNonQuery(string sql)
Scalar<T>(string sql)
ExecuteCommand(string sql)
```

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
HasParameter(parameterName)
```

主なプロパティ:

```csharp
OriginalSql
NormalizedSql
Fingerprint
StatementKind
TargetTables
ReferencedTables
SelectedColumns
WhereColumns
ParameterNames
GlobalCallIndex
MethodCallIndex
```
