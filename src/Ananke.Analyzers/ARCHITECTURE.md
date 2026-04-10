# Ananke.Analyzers — Architecture

> Roslyn analyzer — compile-time validation of workflow job name references.

## Role

A .NET Standard 2.0 Roslyn analyzer that validates string arguments in
`Workflow<T>.Job()`, `Then()`, `Decide()`, `Chain()`, and `Fork()` calls.
Catches undefined job name references at compile time rather than runtime.

Bundled into the `Ananke.Orchestration` NuGet package as an analyzer asset.

## Dependencies

- `Microsoft.CodeAnalysis.CSharp` (Roslyn APIs)
- `Microsoft.CodeAnalysis.Analyzers`

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `UndefinedJobNameAnalyzer` | DiagnosticAnalyzer | Reports `ANANKE001` when a `Then`/`Decide`/`Chain` references a job name not registered via `Job()` |

## Target

- .NET Standard 2.0 (required for Roslyn analyzers)
- Not independently packable — distributed inside `Ananke.Orchestration` NuGet
