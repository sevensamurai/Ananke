# Ananke.Skills — Architecture

> External skill catalog — discover, score, and install community-contributed
> CLI tools as `ToolDefinition` entries for agent use.

## Role

Provides a skill catalog system where agents can discover tools from external
registries (e.g. OpenClaw/ClawHub), score them based on past performance,
and install them as CLI processes that are callable via `ToolKit`.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `ISkillCatalog` — the abstraction for browsing/searching/resolving external skills — `src/Ananke.Skills/ISkillCatalog.cs`
2. `OpenClawCatalog` — the `ISkillCatalog` implementation backed by the OpenClaw/ClawHub registry — `src/Ananke.Skills/OpenClaw/OpenClawCatalog.cs`
3. `ToolKitSkillExtensions` — `toolkit.AddFromCatalogAsync(catalog, query, limit:)`, the entry point that searches the catalog and resolves matching skills into `ToolDefinition` entries — `src/Ananke.Skills/ToolKitSkillExtensions.cs`
4. `CliProcessRunner` — executes CLI-based skills as child processes once installed — `src/Ananke.Skills/CliProcessRunner.cs`

---

## Dependencies

- `Ananke.Orchestration` (project)

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Skills` | `ISkillCatalog`, `ISkillScoreStore`, `JsonFileScoreStore`, `SkillDescriptor`, `SkillInstallMethod`, `SkillScore`, `CliProcessRunner`, `ToolKitSkillExtensions`, `SkillCatalogMemorySync` |
| `Ananke.Skills.OpenClaw` | `OpenClawCatalog` — `ISkillCatalog` implementation backed by the OpenClaw/ClawHub registry |

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `ISkillCatalog` | Interface | Browse/search/resolve external skills | `src/Ananke.Skills/ISkillCatalog.cs` |
| `OpenClawCatalog` | Class | `ISkillCatalog` implementation for the OpenClaw registry | `src/Ananke.Skills/OpenClaw/OpenClawCatalog.cs` |
| `SkillDescriptor` | Record | Metadata about an installable skill (name, version, args, install method) | `src/Ananke.Skills/SkillDescriptor.cs` |
| `ISkillScoreStore` | Interface | Persist skill quality scores based on usage outcomes | `src/Ananke.Skills/ISkillScoreStore.cs` |
| `JsonFileScoreStore` | Class | `ISkillScoreStore` backed by a local JSON file | `src/Ananke.Skills/JsonFileScoreStore.cs` |
| `SkillScore` | Record | Aggregated quality score (success rate, latency, usage count) | `src/Ananke.Skills/SkillScore.cs` |
| `CliProcessRunner` | Class | Executes CLI-based skills as child processes | `src/Ananke.Skills/CliProcessRunner.cs` |
| `ToolKitSkillExtensions` | Static class | `toolkit.AddFromCatalogAsync(catalog, query, limit:)` — searches the catalog and resolves matching skills into `ToolDefinition` entries | `src/Ananke.Skills/ToolKitSkillExtensions.cs` |
| `SkillInstallMethod` | Enum | How to launch a skill's binary: `Uvx` (Python via PyPI), `Npx` (Node.js via npm), `Docker`, `Shell` | `src/Ananke.Skills/SkillInstallMethod.cs` |
| `SkillCatalogMemorySync` | Class | Synchronises discovered skill catalog entries into an `IToolMemory` so the smart router can recall and score them | `src/Ananke.Skills/SkillCatalogMemorySync.cs` |
