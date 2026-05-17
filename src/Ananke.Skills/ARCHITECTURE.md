# Ananke.Skills — Architecture

> External skill catalog — discover, score, and install community-contributed
> CLI tools as `ToolDefinition` entries for agent use.

## Role

Provides a skill catalog system where agents can discover tools from external
registries (e.g. OpenClaw/ClawHub), score them based on past performance,
and install them as CLI processes that are callable via `ToolKit`.

## Dependencies

- `Ananke.Orchestration` (project)

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Skills` | `ISkillCatalog`, `ISkillScoreStore`, `JsonFileScoreStore`, `SkillDescriptor`, `SkillInstallMethod`, `SkillScore`, `CliProcessRunner`, `ToolKitSkillExtensions`, `SkillCatalogMemorySync` |
| `Ananke.Skills.OpenClaw` | `OpenClawCatalog` — `ISkillCatalog` implementation backed by the OpenClaw/ClawHub registry |

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `ISkillCatalog` | Interface | Browse/search/resolve external skills |
| `OpenClawCatalog` | Class | `ISkillCatalog` implementation for the OpenClaw registry |
| `SkillDescriptor` | Record | Metadata about an installable skill (name, version, args, install method) |
| `ISkillScoreStore` | Interface | Persist skill quality scores based on usage outcomes |
| `JsonFileScoreStore` | Class | `ISkillScoreStore` backed by a local JSON file |
| `SkillScore` | Record | Aggregated quality score (success rate, latency, usage count) |
| `CliProcessRunner` | Class | Executes CLI-based skills as child processes |
| `ToolKitSkillExtensions` | Static class | `toolkit.AddSkill(descriptor)` — installs a skill as a `ToolDefinition` |
| `SkillInstallMethod` | Enum | How to install a skill: `Npm`, `Pip`, `DotnetTool`, `Binary` |
| `SkillCatalogMemorySync` | Class | Synchronises discovered skill catalog entries into an `IToolMemory` so the smart router can recall and score them |
