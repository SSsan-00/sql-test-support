# SqlTestSupport

SqlTestSupport is a .NET 9 test helper for MSTest projects that need to validate
and normalize raw SQL Server command text during unit tests.

The project is designed for codebases where SQL is written directly in production
methods and a production database class is replaced by a test double through
inheritance and method overrides.

## Goals

- Validate T-SQL syntax with Microsoft ScriptDom.
- Target SQL Server 2022 syntax (`SqlVersion.Sql160`).
- Normalize SQL command text only when the AST fingerprint is unchanged.
- Extract AST-derived metadata for mock routing:
  - statement kind
  - target tables
  - referenced tables
  - selected columns
  - where columns
  - parameter names
- Provide a small `WhenSql(...).Returns...` router for DB test doubles.
- Generate single-file bootstrap artifacts for easy adoption in existing test
  projects.

## Non-goals

- It does not connect to SQL Server.
- It does not validate table existence, column existence, permissions, or type
  compatibility.
- It does not fully resolve aliases against database metadata.
- It does not support `GO` batch separators for command-text execution.
- It does not require async APIs; parsing and normalization are in-memory CPU
  work.

## Public API

The intended integration into an existing custom Assert class is two methods:

```csharp
public static void IsValidSql(string sql, string? message = null)
    => SqlAssertFacade.IsValidSql(sql, message);

public static string NormalizeSql(string sql, string? message = null)
    => SqlAssertFacade.NormalizeSql(sql, message);
```

Test code can then use:

```csharp
Assert.IsValidSql(sql);
var normalized = Assert.NormalizeSql(sql);
```

The mock router can be used from a DB test double:

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

Mock behavior is configured against inspected SQL:

```csharp
db.WhenSql(q => q.IsSelectFrom("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsScalar("Alice");

db.WhenSql(q => q.IsUpdate("dbo.Customers") && q.WhereUses("Id"))
  .ReturnsAffectedRows(1);
```

## Bootstrap

Run:

```bash
./bootstrap/bootstrap.sh
```

or:

```bash
dotnet run --project tools/SqlTestSupport.Bootstrap/SqlTestSupport.Bootstrap.csproj
```

This generates:

```text
dist/SqlTestSupport.cs
dist/SqlTestSupport.Tests.cs
```

`SqlTestSupport.cs` is the runtime helper bundle. `SqlTestSupport.Tests.cs`
contains the MSTest coverage bundle that can be copied into an adopting project
when desired.

## Development

```bash
dotnet restore
dotnet test
dotnet run --project tools/SqlTestSupport.Bootstrap/SqlTestSupport.Bootstrap.csproj
```

## Documentation

- [Architecture](docs/architecture.md)
- [API reference](docs/api.md)
- [Mock DB integration](docs/mock-db-integration.md)
- [Bootstrap design](docs/bootstrap.md)
- [Testing strategy](docs/testing.md)
