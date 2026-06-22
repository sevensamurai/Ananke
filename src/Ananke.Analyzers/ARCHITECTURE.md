# Ananke.Analyzers — Architecture

> Roslyn analyzers — compile-time validation of workflow job name references
> and async/await `ConfigureAwait` hygiene.

## Role

A .NET Standard 2.0 Roslyn analyzer package with two independent analyzers:

- Validates string arguments in `Workflow<T>.Job()`, `Then()`, `Decide()`, `Chain()`, and
  `Fork()` calls — catches undefined job name references at compile time rather than runtime.
- Enforces the `ConfigureAwait(false)` convention on internal/private async methods.

Bundled into the `Ananke.Orchestration` NuGet package as an analyzer asset.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `UndefinedJobNameAnalyzer` — reports `ANANKE001` when a `Then`/`Decide`/`Chain` call
   references a job name not registered via `Job()` — `src/Ananke.Analyzers/UndefinedJobNameAnalyzer.cs`
2. `ConfigureAwaitAnalyzer` — reports `ANANKE_ASYNC_001` when an internal/private async
   method awaits without `ConfigureAwait(false)` — `src/Ananke.Analyzers/ConfigureAwaitAnalyzer.cs`

---

## Dependencies

- `Microsoft.CodeAnalysis.CSharp` (Roslyn APIs)
- `Microsoft.CodeAnalysis.Analyzers`

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `UndefinedJobNameAnalyzer` | DiagnosticAnalyzer | Reports `ANANKE001` when a `Then`/`Decide`/`Chain` references a job name not registered via `Job()` | `src/Ananke.Analyzers/UndefinedJobNameAnalyzer.cs` |
| `ConfigureAwaitAnalyzer` | DiagnosticAnalyzer | Reports `ANANKE_ASYNC_001` (warning) when an internal/private async method awaits without `ConfigureAwait(false)`. Public methods marked `[AgentJob]` / `[WorkflowEntry]` are exempt — see [`Ananke.Orchestration` ARCHITECTURE § ConfigureAwait Convention](../Ananke.Orchestration/ARCHITECTURE.md#configureawait-convention). | `src/Ananke.Analyzers/ConfigureAwaitAnalyzer.cs` |

## Target

- .NET Standard 2.0 (required for Roslyn analyzers)
- Not independently packable — distributed inside `Ananke.Orchestration` NuGet
