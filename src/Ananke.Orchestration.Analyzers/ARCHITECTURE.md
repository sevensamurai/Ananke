# Ananke.Orchestration.Analyzers — Architecture

> Roslyn analyzers a product gets — compile-time validation of workflow job name references and
> of deprecated and retired model literals.

## Role

A .NET Standard 2.0 Roslyn analyzer assembly with two analyzers:

- Validates string arguments in `Workflow<T>.Job()`, `Then()`, `Decide()`, `Chain()`, and
  `Fork()` calls — catches undefined job name references at compile time rather than runtime.
- Flags string literals equal to a deprecated or retired model identifier, offering a code fix
  to the recommended `Models.*` constant.

Bundled into the `Ananke.Orchestration` NuGet package as an analyzer asset. The code fix lives in
a separate `Ananke.Analyzers.CodeFixes` assembly (Roslyn's `RS1038` forbids a `DiagnosticAnalyzer`
assembly from referencing `Microsoft.CodeAnalysis.Workspaces`, which the code fix needs), packed
beside it.

This repository's own rule, `ANANKE_ASYNC_001` (`ConfigureAwaitAnalyzer`), is in
`Ananke.Analyzers`, which is never packed.

---

## Start Here

1. `UndefinedJobNameAnalyzer` — reports `ANANKE001` when a `Then`/`Decide`/`Chain` call
   references a job name not registered via `Job()` — `src/Ananke.Orchestration.Analyzers/UndefinedJobNameAnalyzer.cs`
2. `DeprecatedModelLiteralAnalyzer` — reports `ANNKE002`/`ANNKE003` when a string literal equals
   a deprecated/retired model identifier from `model-lifecycle.json` —
   `src/Ananke.Orchestration.Analyzers/DeprecatedModelLiteralAnalyzer.cs`. Its code fix,
   `DeprecatedModelLiteralCodeFixProvider`, lives in the sibling `Ananke.Analyzers.CodeFixes`
   project and deliberately has no `FixAll` support.

---

## Dependencies

- `Microsoft.CodeAnalysis.CSharp` (Roslyn APIs)
- `Microsoft.CodeAnalysis.Analyzers`

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `UndefinedJobNameAnalyzer` | DiagnosticAnalyzer | Reports `ANANKE001` when a `Then`/`Decide`/`Chain` references a job name not registered via `Job()` | `src/Ananke.Orchestration.Analyzers/UndefinedJobNameAnalyzer.cs` |
| `DeprecatedModelLiteralAnalyzer` | DiagnosticAnalyzer | Reports `ANNKE002` (Deprecated) or `ANNKE003` (Retired) on a string literal equal to a model identifier read from `model-lifecycle.json`: the copy embedded in this assembly, or a file of that name given as `AdditionalFiles`, which replaces it. | `src/Ananke.Orchestration.Analyzers/DeprecatedModelLiteralAnalyzer.cs` |
| `DeprecatedModelLiteralCodeFixProvider` | CodeFixProvider | Offers to replace a flagged literal with its resolved `Models.*` constant, resolved via semantic search rather than a second hardcoded id-to-constant table. `GetFixAllProvider()` returns `null` — a solution-wide bulk sweep broke test semantics and mapper behavior once (paired literals, identity/passthrough tables); the fix must be applied one site at a time via the IDE. Lives in a separate assembly from the analyzer (`RS1038`). | `src/Ananke.Analyzers.CodeFixes/DeprecatedModelLiteralCodeFixProvider.cs` |

## Target

- .NET Standard 2.0 (required for Roslyn analyzers)
- Not independently packable — distributed inside `Ananke.Orchestration` NuGet
