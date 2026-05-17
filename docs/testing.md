# Testing Strategy

The tests cover both the SQL analysis pipeline and the mock DB behavior.

## Validation tests

These verify that:

- valid SQL Server 2022 T-SQL parses successfully
- invalid T-SQL throws a syntax validation exception
- normalization returns SQL only when fingerprints match
- inspection extracts tables, columns, statement kind, and parameters

## Assert facade tests

These verify that:

- valid SQL passes through the facade
- invalid SQL is converted into `AssertFailedException`
- custom user messages are preserved in assertion failure output
- normalized SQL can be returned for callers that want to execute normalized
  command text

## Mock router tests

These verify that:

- `WhenSql(...).ReturnsScalar(...)` works for scalar calls
- `WhenSql(...).ReturnsAffectedRows(...)` works for non-query calls
- unregistered SQL fails
- invalid SQL fails before rule matching
- sequence returns are consumed in order
- `VerifyAll()` fails for unused rules

## Mock DB integration test

The integration test defines a minimal production-like base DB class with virtual
methods:

```csharp
public virtual int Execute(string sql, object? parameters = null)
public virtual T Scalar<T>(string sql, object? parameters = null)
```

The mock subclass overrides those methods and delegates to `SqlMockRouter`. This
keeps the test representative of the intended adoption path without requiring a
real database.
