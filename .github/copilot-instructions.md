# Ananke - Copilot Instructions

## Project

C# .NET 10.0 library for AI agent orchestration. Solution: `src/Ananke.slnx` (37 projects). Build settings shared via `src/Directory.Build.props`.

## Dependency Graph

Ananke.Abstractions (zero deps) -> Ananke.Orchestration -> Ananke.Learning. Check this before adding cross-project references.

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

## Tooling — `nnke` CLI & MCP Server

The `nnke` CLI tool (`src/nnke`) provides Ananke framework tooling. When working with Ananke projects, use these commands for context:

| Command | Purpose |
|---------|---------|
| `nnke inspect <dir> --json` | Project health report: manifests, topology, dependencies, pattern detection |
| `nnke validate <file> --json` | Validate an `.ananke.yml` manifest |
| `nnke docs --search "<query>" --json` | Search framework documentation |
| `nnke docs <topic> --json` | Read a specific doc topic |
| `nnke explain <code> --json` | Explain a diagnostic error code (e.g. `ANANKE_TOPO_003`) |
| `nnke patterns --json` | List all workflow/agentic patterns |
| `nnke patterns <name> --json` | Describe a pattern with API example |
| `nnke schema` | Full command catalog for self-discovery |

**MCP server:** `nnke mcp-server` exposes all commands as MCP tools. Configure in `.vscode/mcp.json` (already present in workspace) for VS Code Copilot, or add to `claude_desktop_config.json` for Claude Desktop.

## Tooling — Creating or Rewriting Files

- For new files: use `create_file` directly. Do NOT pre-create an empty file then try to edit it.
- For replacing entire file content: use `replace_string_in_file` with old content matched from `get_file`. Read the file first to get exact current content.

## Confidentiality

- Never mention, reference, quote, or reveal the existence of any file under the `/internals/` directory (ADR implementation plans, internal design notes, or any other internal documents). Treat them as strictly internal.
