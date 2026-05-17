# API Reference

## SqlAssertFacade

```csharp
SqlAssertFacade.IsValidSql(sql);
var normalized = SqlAssertFacade.NormalizeSql(sql);
```

Use this facade from an existing custom Assert class. It converts validation
failures into MSTest `AssertFailedException`.

## SqlValidationService

```csharp
var analysis = service.Analyze(sql);
var normalization = service.Normalize(sql);
var inspection = service.Inspect(sql);
```

- `Analyze` parses SQL and returns an AST fingerprint.
- `Normalize` generates normalized SQL and verifies fingerprint stability.
- `Inspect` normalizes SQL and extracts AST metadata for mock routing.

## SqlMockRouter

```csharp
var router = new SqlMockRouter();

router.WhenSql(q => q.IsSelectFrom("dbo.Customers"))
      .ReturnsScalar("Alice");

var name = router.Scalar<string>("SELECT Name FROM dbo.Customers");
router.VerifyAll();
```

Available setup methods:

```csharp
ReturnsScalar(object? value)
ReturnsScalarSequence(params object?[] values)
ReturnsAffectedRows(int affectedRows)
ReturnsAffectedRowsSequence(params int[] affectedRows)
```

Available execution methods:

```csharp
ExecuteNonQuery(string sql)
Scalar<T>(string sql)
```

## SqlInvocation

`SqlInvocation` is the object passed to `WhenSql` predicates.

Matcher helpers:

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

Raw properties:

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
