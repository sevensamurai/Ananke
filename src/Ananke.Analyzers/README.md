# Ananke.Analyzers

Roslyn analyzers for the Ananke framework. **Not a standalone NuGet package** — bundled
as an analyzer asset inside `Ananke.Orchestration`, so any project referencing
`Ananke.Orchestration` gets these checks automatically with no extra install step.

## What it checks

| Diagnostic | Severity | Checks |
|---|---|---|
| `ANANKE001` | Error | `Workflow<T>.Job()` / `Then()` / `Decide()` / `Chain()` / `Fork()` reference a job name that was never registered via `.Job(...)` — catches typo'd or stale job names at compile time instead of a runtime `InvalidOperationException`. |
| `ANANKE_ASYNC_001` | Warning | An internal/private async method has an `await` without `ConfigureAwait(false)`. Public methods marked `[AgentJob]` / `[WorkflowEntry]` are exempt. |

## Why a bundled analyzer instead of a guideline

Job-name references are plain strings at the API surface — the compiler can't catch a
typo'd name on its own. `UndefinedJobNameAnalyzer` closes that gap by walking the
`Workflow<T>` builder calls at compile time. `ConfigureAwaitAnalyzer` encodes the
project's async convention (see
[`Ananke.Orchestration` ARCHITECTURE § ConfigureAwait Convention](../Ananke.Orchestration/ARCHITECTURE.md#configureawait-convention))
so it is enforced automatically rather than relying on code review.

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
