# Mock DB Integration

The intended production shape is a concrete DB class with virtual execution
methods whose first argument is SQL command text.

```csharp
public class AppDb
{
    public virtual int Execute(string sql, object? parameters = null)
    {
        // Production execution.
    }

    public virtual T Scalar<T>(string sql, object? parameters = null)
    {
        // Production execution.
    }
}
```

The test double overrides only the execution boundary:

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

## Flow

Every SQL call through the mock does the same work:

```text
SQL string
  -> validate
  -> normalize
  -> verify AST fingerprint stability
  -> inspect AST metadata
  -> record invocation history
  -> evaluate WhenSql rules
  -> return registered behavior or fail
```

Rules are configured with AST-derived metadata:

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsScalar("Alice");

db.WhenSql(q => q.IsUpdate("dbo.Customers"))
  .ReturnsAffectedRows(1);
```

## Strict behavior

The router is strict by default:

- invalid SQL fails before matching
- unregistered SQL fails
- a rule configured with `ReturnsScalar` cannot satisfy `ExecuteNonQuery`
- a rule configured with `ReturnsAffectedRows` cannot satisfy `Scalar<T>`
- `VerifyAll()` fails if a registered rule was never called

## Repeated calls

For repeated calls, use sequence returns:

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers"))
  .ReturnsScalarSequence("Alice", "Bob");
```

The first matching call returns `Alice`, the second returns `Bob`, and additional
calls fail after the sequence is exhausted.

## Matching guidance

Prefer these matchers:

```csharp
q.IsSelectFrom("dbo.Customers")
q.IsInsertInto("dbo.Customers")
q.IsUpdate("dbo.Customers")
q.IsDeleteFrom("dbo.Customers")
q.WhereUses("Id")
q.SelectsColumn("Name")
q.ReferencesTable("dbo.Customers")
q.TargetsTable("dbo.Customers")
q.HasParameter("@Id")
```

`NormalizedSql.Contains(...)` can still be used as an escape hatch, but AST
metadata should be the default matching surface.
