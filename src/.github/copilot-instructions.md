# Ananke - Copilot Instructions

## Project

C# .NET 10.0 library for AI agent orchestration. Solution: `src/Ananke.slnx` (37 projects). Build settings shared via `src/Directory.Build.props`.

## Dependency Graph

Ananke.Abstractions (zero deps) -> Ananke.Orchestration -> Ananke.Learning. Check this before adding cross-project references.

## Coding Rules

- File-scoped namespaces (`namespace X;`), matching assembly + folder path
- Never put types from one assembly into another assembly's namespace
- Organize code by vertical slices: group related types into sub-folders with matching sub-namespaces (e.g., `Ananke.Learning.Episodes`, `Ananke.Learning.Skills`). Keep root namespace for core abstractions only. When a project grows beyond ~8 files, introduce vertical folders.
- `sealed record` for immutable data; `required` for mandatory fields
- Primary constructors for dependency injection in classes
- `IReadOnlyList<T>` / `IReadOnlyDictionary<TK, TV>` in public APIs
- XML doc comments on public APIs
- Every new interface must ship with a default implementation
- Reserve the `InMemory` prefix for **store** interfaces that have (or will have) external-storage counterparts (e.g., `InMemoryEmpiricalMemory`, `InMemoryEpisodeStore`). Compute, orchestration, and I/O pipeline implementations use plain names (e.g., `TagImportanceTracker`, `OfflineLearner`, `SkillPackager`).
- No breaking changes to established interfaces without an ADR
- Never reference ADR (Architecture Decision Records) in code (comments, identifiers, doc strings) or public-facing documentation; ADRs are internal governance artifacts only

## Build Rules

- `Directory.Build.props` owns `TargetFramework`, `Nullable`, `ImplicitUsings`, `VersionPrefix` — never repeat these in individual csproj files
- `TreatWarningsAsErrors` is on: zero warnings allowed
- New packable project: set only `IsPackable`, `PackageId`, `Description`, optionally `PackageTags`; add a `README.md` next to the csproj

## Testing

- NUnit + Shouldly
- Run full suite after changes: `dotnet test src/Ananke.slnx`
- Test project naming: `Ananke.<Feature>.Tests`; test class naming: `<ClassUnderTest>Tests`

## Version & Release

- Version lives in `Directory.Build.props` `<VersionPrefix>` — single source of truth
- Add `releases/v{VERSION}.md` before merging (CI enforces this)

## Tooling — Creating or Rewriting Files

- For new files: use `create_file` directly. Do NOT pre-create an empty file then try to edit it.
- For replacing entire file content: use `replace_string_in_file` with old content matched from `get_file`. Read the file first to get exact current content.
