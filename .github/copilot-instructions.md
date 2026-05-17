# Ananke - Copilot Instructions

## Project
C# .NET 10.0 library for AI agent orchestration. Solution: `src/Ananke.slnx`. Build settings shared via `src/Directory.Build.props`.


## Coding Rules
- File-scoped namespaces (`namespace X;`), matching assembly + folder path
- Never put types from one assembly into another assembly's namespace
- Organize code by vertical slices: group related types into sub-folders with matching sub-namespaces (e.g., `Ananke.Learning.Episodes`, `Ananke.Learning.Skills`). 
- Keep root namespace for core abstractions only. When a project grows beyond ~8 files, introduce vertical folders.
- `sealed record` for immutable data; `required` for mandatory fields
- Primary constructors for dependency injection in classes
- `IReadOnlyList<T>` / `IReadOnlyDictionary<TK, TV>` in public APIs
- XML doc comments on public APIs
- Every new interface must ship with an in-memory implementation
- No breaking changes to established interfaces without an ADR
- **`ConfigureAwait` rule:** add `ConfigureAwait(false)` on `await` calls inside private/internal library helpers and implementations (e.g. store internals, Qdrant helpers, `ToolKit` private methods). Omit it on public pipeline entry points — `IAgentModelMiddleware.OnBeforeGenerateAsync`, `IAgentModelMiddleware.OnAfterGenerateAsync`, `IJob.ExecuteAsync`, and similar — because all supported hosts (ASP.NET Core, hosted services, console) run with no `SynchronizationContext`. Adding it at the public entry-point level is harmless but misleading; it implies callers must propagate it, which they do not.

## Build Rules
- `Directory.Build.props` owns `TargetFramework`, `Nullable`, `ImplicitUsings`, `VersionPrefix` â€” never repeat these in individual csproj files
- `TreatWarningsAsErrors` is on: zero warnings allowed
- New packable project: set only `IsPackable`, `PackageId`, `Description`, optionally `PackageTags`; add a `README.md` next to the csproj

## Testing
- NUnit + Shouldly
- Run full suite after changes: `dotnet test src/Ananke.slnx`
- Test project naming: `Ananke.<Feature>.Tests`; test class naming: `<ClassUnderTest>Tests`

## Version & Release
- Version lives in `Directory.Build.props` `<VersionPrefix>` â€” single source of truth
- Add `releases/v{VERSION}.md` before merging (CI enforces this)

## Architecture Reference
When working on anything non-trivial — new features, refactors, cross-project changes, interface design — read the relevant architecture document(s) first. They are the authoritative reference for type names, interfaces, and design decisions.

| Document | When to read it |
|---|---|
| [`ARCHITECTURE.md`](../ARCHITECTURE.md) | Entry point — layer map, dependency graph, project list, testing strategy |
| [`architecture/orchestration.md`](../architecture/orchestration.md) | `Workflow<T>`, jobs, routing, streaming events, checkpointing, budget, middleware |
| [`architecture/agents.md`](../architecture/agents.md) | `IAgentModel`, `IStreamingAgentModel`, provider adapters, model routing, context strategies |
| [`architecture/knowledge.md`](../architecture/knowledge.md) | RAG pipeline, `IKnowledgeStore`, `IKnowledgeCatalog`, document processing, `KnowledgeBase` |
| [`architecture/learning.md`](../architecture/learning.md) | `IEmpiricalMemory`, episodes, offline learning, skill packaging, entity memory |
| [`architecture/organics-federation.md`](../architecture/organics-federation.md) | `OrganicHost`, cell division, `IHealthMonitor`, federation, cross-cloud deployment |
| [`architecture/infrastructure.md`](../architecture/infrastructure.md) | Redis, MQTT, Qdrant, OpenTelemetry, ASP.NET Core integration |
| [`architecture/interop.md`](../architecture/interop.md) | MCP server, A2A protocol, `Ananke.Skills`, platform adapters (Slack, Discord) |
| [`architecture/federation-credentials.md`](../architecture/federation-credentials.md) | Credential types, rotation, `IFederationCredentialProvider` per platform |

## Tooling — Creating or Rewriting Files

- For new files: use `create_file` directly. Do NOT pre-create an empty file then try to edit it.
- For replacing entire file content: use `replace_string_in_file` with old content matched from `get_file`. Read the file first to get exact current content.

## Confidentiality

- Never mention, reference, quote, or reveal the existence of any file under the `/internals/` directory (ADR implementation plans, internal design notes, or any other internal documents). Treat them as strictly internal.
