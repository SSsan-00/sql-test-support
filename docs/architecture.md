# Architecture

The repository keeps development code split by responsibility, then uses the
bootstrap tool to emit single-file artifacts for adoption.

## Runtime components

```text
SqlAssertFacade
  Converts SQL validation errors into MSTest AssertFailedException.

SqlValidationService
  Coordinates Analyze, Normalize, and Inspect operations.

SqlServer2022SyntaxAnalyzer
  Parses SQL with ScriptDom using SQL Server 2022 syntax.

SqlServer2022Normalizer
  Generates normalized SQL and verifies AST fingerprint stability.

SqlAstFingerprinter
  Creates a structural hash from the ScriptDom AST.

SqlInspectionService
  Extracts mock-routing metadata from the AST.

SqlMockRouter
  Evaluates WhenSql rules and returns registered mock behavior.
```

## Normalization contract

Normalization is fail-closed.

```text
original SQL
  -> parse as Sql160
  -> original AST fingerprint
  -> generate normalized SQL
  -> parse normalized SQL as Sql160
  -> normalized AST fingerprint
  -> compare fingerprints
  -> return normalized SQL only when fingerprints match
```

If the fingerprints differ, the normalizer throws
`SqlNormalizationChangedAstException` and does not return the generated SQL.

## Fingerprint scope

Included:

- AST node types
- public semantic properties
- enum values
- string, numeric, and boolean values
- child node order
- identifier values and quote metadata exposed by ScriptDom
- literal values and expression structure

Excluded:

- line and column numbers
- offsets and token indexes
- token stream
- whitespace
- comments

The fingerprint is a structural guard for normalization. It is not a database
semantic proof. It does not validate metadata, permissions, or runtime behavior.

## SQL dialect

The helper targets:

```text
SQL Server 2022
ScriptDom SqlVersion.Sql160
SqlEngineType.Standalone
QUOTED_IDENTIFIER ON
```

`GO` batch separators are rejected because SQL command-text APIs generally do
not accept `GO`.

## Inspection metadata

`SqlInspectionResult` and `SqlInvocation` expose:

- `StatementKind`
- `TargetTables`
- `ReferencedTables`
- `SelectedColumns`
- `WhereColumns`
- `ParameterNames`
- `NormalizedSql`
- `Fingerprint`

Alias resolution is intentionally shallow. For example, `c.Name` is kept as
`c.Name`; the helper does not connect to the database to resolve `c` to
`dbo.Customers`.
