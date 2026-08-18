# Ananke.Analyzers

Roslyn analyzers for the Ananke framework. **Not a standalone NuGet package** — bundled
as an analyzer asset inside `Ananke.Orchestration`, so any project referencing
`Ananke.Orchestration` gets these checks automatically with no extra install step.

## What it checks

| Diagnostic | Severity | Checks |
|---|---|---|
| `ANANKE001` | Error | `Workflow<T>.Job()` / `Then()` / `Decide()` / `Chain()` / `Fork()` reference a job name that was never registered via `.Job(...)` — catches typo'd or stale job names at compile time instead of a runtime `InvalidOperationException`. |
| `ANANKE_ASYNC_001` | Warning | An internal/private async method has an `await` without `ConfigureAwait(false)`. Public methods marked `[AgentJob]` / `[WorkflowEntry]` are exempt. |
| `ANNKE001` | Error | A reference to an `[Obsolete]`-marked constant in `Ananke.Abstractions.Agents.Models` (a **Deprecated** model — still callable, but superseded). Compiler-emitted, not from a custom analyzer. Legitimate references (translation tables that must keep resolving a deprecated-but-functional model) are `#pragma warning disable/restore ANNKE001`-wrapped at the site; test fixtures are suppressed via `src/.editorconfig`'s `[tests/**/*.cs]` section. See `docs/reference/model-deprecations.md`. |
| `ANNKE002` | Error | A string literal equal to a **Deprecated** model identifier (`DeprecatedModelLiteralAnalyzer`, reading the single source of truth `model-lifecycle.json`). Has a registered code fix (replace with the recommended `Models.*` constant) but **deliberately no `FixAll` support** — a solution-wide sweep broke test semantics and mapper behavior once already; apply the fix one site at a time via the IDE, never in bulk. Legitimate references pragma-wrapped or editorconfig-suppressed same as `ANNKE001`. |
| `ANNKE003` | Error | A string literal equal to a **Retired** model identifier — the provider no longer serves it, so the call fails regardless. Currently never fires: retired models are deleted from `Models.cs` outright rather than kept-and-flagged. |

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
