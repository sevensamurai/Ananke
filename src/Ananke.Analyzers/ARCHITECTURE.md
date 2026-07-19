# Ananke.Analyzers — Architecture

> Roslyn analyzers — compile-time validation of workflow job name references,
> async/await `ConfigureAwait` hygiene, ambient-clock reads, and deprecated model literals.

## Role

A .NET Standard 2.0 Roslyn analyzer package with four independent analyzers:

- Validates string arguments in `Workflow<T>.Job()`, `Then()`, `Decide()`, `Chain()`, and
  `Fork()` calls — catches undefined job name references at compile time rather than runtime.
- Enforces the `ConfigureAwait(false)` convention on internal/private async methods.
- Flags direct `DateTime`/`DateTimeOffset` `Now`/`UtcNow` reads in production code — enforces
  `TimeProvider` injection instead of an ambient clock.
- Flags string literals equal to a deprecated or retired model identifier, offering a code fix
  to the recommended `Models.*` constant.

Bundled into the `Ananke.Orchestration` NuGet package as an analyzer asset. The code fix for the
last item lives in a separate `Ananke.Analyzers.CodeFixes` assembly (Roslyn's `RS1038` forbids a
`DiagnosticAnalyzer` assembly from referencing `Microsoft.CodeAnalysis.Workspaces`, which the
code fix needs) — also bundled as an analyzer asset.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `UndefinedJobNameAnalyzer` — reports `ANANKE001` when a `Then`/`Decide`/`Chain` call
   references a job name not registered via `Job()` — `src/Ananke.Analyzers/UndefinedJobNameAnalyzer.cs`
2. `ConfigureAwaitAnalyzer` — reports `ANANKE_ASYNC_001` when an internal/private async
   method awaits without `ConfigureAwait(false)` — `src/Ananke.Analyzers/ConfigureAwaitAnalyzer.cs`
3. `AmbientClockAnalyzer` — reports `ANANKE_TIME_001` when code reads
   `DateTime`/`DateTimeOffset` `Now`/`UtcNow` directly instead of through an injected
   `TimeProvider` — `src/Ananke.Analyzers/AmbientClockAnalyzer.cs`
4. `DeprecatedModelLiteralAnalyzer` — reports `ANNKE002`/`ANNKE003` when a string literal equals
   a deprecated/retired model identifier from `model-lifecycle.json` —
   `src/Ananke.Analyzers/DeprecatedModelLiteralAnalyzer.cs`. Its code fix,
   `DeprecatedModelLiteralCodeFixProvider`, lives in the sibling `Ananke.Analyzers.CodeFixes`
   project and deliberately has no `FixAll` support.

---

## Dependencies

- `Microsoft.CodeAnalysis.CSharp` (Roslyn APIs)
- `Microsoft.CodeAnalysis.Analyzers`

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `UndefinedJobNameAnalyzer` | DiagnosticAnalyzer | Reports `ANANKE001` when a `Then`/`Decide`/`Chain` references a job name not registered via `Job()` | `src/Ananke.Analyzers/UndefinedJobNameAnalyzer.cs` |
| `ConfigureAwaitAnalyzer` | DiagnosticAnalyzer | Reports `ANANKE_ASYNC_001` (warning) when an internal/private async method awaits without `ConfigureAwait(false)`. Public methods marked `[AgentJob]` / `[WorkflowEntry]` are exempt — see [`Ananke.Orchestration` ARCHITECTURE § ConfigureAwait Convention](../Ananke.Orchestration/ARCHITECTURE.md#configureawait-convention). | `src/Ananke.Analyzers/ConfigureAwaitAnalyzer.cs` |
| `AmbientClockAnalyzer` | DiagnosticAnalyzer | Reports `ANANKE_TIME_001` (warning) on a semantically-confirmed static `DateTime.Now`/`UtcNow` or `DateTimeOffset.Now`/`UtcNow` member access anywhere in a method body or default-value expression. Suppressed for test/demo/CLI projects and a legacy per-file list in `src/.editorconfig`. | `src/Ananke.Analyzers/AmbientClockAnalyzer.cs` |
| `DeprecatedModelLiteralAnalyzer` | DiagnosticAnalyzer | Reports `ANNKE002` (Deprecated) or `ANNKE003` (Retired) on a string literal equal to a model identifier read from `model-lifecycle.json`. Both, plus the compiler-emitted `ANNKE001` (`[Obsolete]` constant reference), are build errors (`TreatWarningsAsErrors`) — legitimate sites are `#pragma`-wrapped or editorconfig-suppressed, not left as warnings. | `src/Ananke.Analyzers/DeprecatedModelLiteralAnalyzer.cs` |
| `DeprecatedModelLiteralCodeFixProvider` | CodeFixProvider | Offers to replace a flagged literal with its resolved `Models.*` constant, resolved via semantic search rather than a second hardcoded id-to-constant table. `GetFixAllProvider()` returns `null` — a solution-wide bulk sweep broke test semantics and mapper behavior once (paired literals, identity/passthrough tables); the fix must be applied one site at a time via the IDE. Lives in a separate assembly from the analyzer (`RS1038`). | `src/Ananke.Analyzers.CodeFixes/DeprecatedModelLiteralCodeFixProvider.cs` |

## Target

- .NET Standard 2.0 (required for Roslyn analyzers)
- Not independently packable — distributed inside `Ananke.Orchestration` NuGet
