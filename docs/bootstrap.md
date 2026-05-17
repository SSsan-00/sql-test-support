# Bootstrap Design

Development code is split into many files to keep responsibilities clear and to
make TDD practical. Existing test projects may prefer one file to adopt and
review. The bootstrap tool supports that adoption path.

## Generated files

Running the bootstrap tool creates:

```text
dist/SqlTestSupport.cs
dist/SqlTestSupport.Tests.cs
```

`SqlTestSupport.cs` includes runtime helpers:

- assert facade
- validation service
- syntax analyzer
- normalizer
- AST fingerprinter
- inspection service
- mock router
- models
- exceptions

`SqlTestSupport.Tests.cs` includes MSTest coverage for the runtime helpers and a
minimal mock DB integration example.

## Command

```bash
dotnet run --project tools/SqlTestSupport.Bootstrap/SqlTestSupport.Bootstrap.csproj
```

or:

```bash
./bootstrap/bootstrap.sh
```

## Bundling rules

The bootstrap tool:

- scans `src/SqlTestSupport/**/*.cs`
- scans `tests/SqlTestSupport.Tests/**/*.cs`
- excludes `MSTestSettings.cs` to avoid duplicate assembly-level settings in
  adopting projects
- collects top-level `using` directives
- removes duplicate `using` directives
- preserves namespace blocks and type definitions
- emits `#nullable enable`
- writes generated files with UTF-8 without BOM

The generated files are artifacts. Development should continue in the split
source files.

## Adopting project checklist

1. Add `dist/SqlTestSupport.cs` to the test project.
2. Add package references:

   ```xml
   <PackageReference Include="Microsoft.SqlServer.TransactSql.ScriptDom" Version="180.18.1" />
   <PackageReference Include="MSTest.TestFramework" Version="4.0.2" />
   ```

3. Add the two forwarding methods to the existing custom Assert class:

   ```csharp
   public static void IsValidSql(string sql, string? message = null)
       => SqlAssertFacade.IsValidSql(sql, message);

   public static string NormalizeSql(string sql, string? message = null)
       => SqlAssertFacade.NormalizeSql(sql, message);
   ```

4. Optionally add `dist/SqlTestSupport.Tests.cs` to validate the adoption.
