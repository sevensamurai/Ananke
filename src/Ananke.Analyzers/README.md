# Ananke.Analyzers

This repository's own Roslyn rules. `src/Directory.Build.props` runs them on every project here;
they are **never packed**, so a product that references Ananke packages does not get them. The
rules a product gets are in `Ananke.Orchestration.Analyzers`.

## What it checks

| Diagnostic | Severity | Checks |
|---|---|---|
| `ANANKE_ASYNC_001` | Warning | An internal/private async method has an `await` without `ConfigureAwait(false)`. Public methods marked `[AgentJob]` / `[WorkflowEntry]` are exempt. |

`ConfigureAwaitAnalyzer` encodes the library's async convention (see
[`Ananke.Orchestration` ARCHITECTURE § ConfigureAwait Convention](../Ananke.Orchestration/ARCHITECTURE.md#configureawait-convention))
so it is enforced automatically rather than relying on code review. It is a rule for library code:
an ASP.NET Core host or a worker has no reason to follow it.
