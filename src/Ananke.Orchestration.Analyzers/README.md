# Ananke.Orchestration.Analyzers

Roslyn analyzers for products built on Ananke. **Not a standalone NuGet package** — bundled as an
analyzer asset inside `Ananke.Orchestration`, with its code fix (`Ananke.Analyzers.CodeFixes`), so
any project referencing `Ananke.Orchestration` gets these checks with no extra install step.

## What it checks

| Diagnostic | Severity | Checks |
|---|---|---|
| `ANANKE001` | Warning | `Workflow<T>.Job()` / `Then()` / `Decide()` / `Chain()` / `Fork()` reference a job name that was never registered via `.Job(...)` — catches typo'd or stale job names at compile time instead of a runtime `InvalidOperationException`. |
| `ANNKE002` | Warning | A string literal equal to a **Deprecated** model identifier (`DeprecatedModelLiteralAnalyzer`, reading `model-lifecycle.json`). Has a code fix (replace with the recommended `Models.*` constant) but **deliberately no `FixAll` support** — a solution-wide sweep broke test semantics and mapper behavior once already; apply the fix one site at a time via the IDE, never in bulk. |
| `ANNKE003` | Error | A string literal equal to a **Retired** model identifier — the provider no longer serves it, so the call fails regardless. |

`ANNKE001`, a reference to an `[Obsolete]`-marked constant in `Ananke.Abstractions.Agents.Models`,
is emitted by the compiler, not by this assembly. See `docs/reference/model-deprecations.md`.

## The model lifecycle file

`model-lifecycle.json` is embedded in this assembly, so `ANNKE002` and `ANNKE003` work in a product
with nothing to configure. A file named `model-lifecycle.json` given to the compilation as
`AdditionalFiles` is read in place of the embedded one, not merged with it:

```xml
<ItemGroup>
  <AdditionalFiles Include="model-lifecycle.json" />
</ItemGroup>
```

In this repository, `src/Directory.Build.props` passes the working copy from
`Ananke.Abstractions/Agents/` this way, and treats warnings as errors, so all three fail the build.

## This repository's own rules

`ANANKE_ASYNC_001` (`ConfigureAwait(false)` in internal and private async methods) is in
`Ananke.Analyzers`, which runs on this repository's code only and is never packed: it is a rule for
library code, not for a product's.

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
