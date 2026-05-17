# Ananke.Skills

[![NuGet](https://img.shields.io/nuget/v/Ananke.Skills.svg)](https://www.nuget.org/packages/Ananke.Skills)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

External skill catalog for Ananke. Discover CLI-based skills from a catalog, rank them with local reliability scores, resolve them into `ToolDefinition` entries, and add them to a `ToolKit` for agent use.

This package currently centers on **OpenClaw / ClawHub-style** skill discovery and local CLI execution. It stays protocol-agnostic by bridging skills through process execution rather than requiring MCP or a custom runtime protocol.

## Install

```bash
dotnet add package Ananke.Skills
```

This package depends on `Ananke.Orchestration` and uses its `ToolKit` / `ToolDefinition` model.

## What this package provides

| Area | Key types |
|---|---|
| Catalog abstraction | `ISkillCatalog`, `SkillDescriptor`, `SkillParameter`, `SkillInstallMethod` |
| Local scoring | `ISkillScoreStore`, `SkillScore`, `VoteDirection`, `JsonFileScoreStore` |
| CLI execution | `CliProcessRunner`, `CliProcessResult` |
| Catalog implementation | `OpenClawCatalog` |
| ToolKit integration | `ToolKitSkillExtensions` |
| Semantic tool-memory bridge | `SkillCatalogMemorySync` |

## Core concepts

### `ISkillCatalog`

The package-level contract for skill discovery and resolution:

- `SearchAsync(...)` finds matching skills from a local cache
- `ResolveAsync(...)` turns a `SkillDescriptor` into a runnable `ToolDefinition`
- `SyncAsync()` refreshes local catalog metadata from the backing source

### `SkillDescriptor`

Lightweight metadata about an external skill:

- stable catalog ID
- display/package name
- human-readable description
- tags for relevance ranking
- install method (`uvx`, `npx`, and future runners)
- optional structured parameters for CLI argument generation

### Local scoring

`ISkillScoreStore` tracks local up/down votes per skill. These scores are deployment-local and affect catalog ranking. Negative scores can be used to suppress unreliable skills from agent exposure.

### CLI bridge

`CliProcessRunner` executes external commands with:

- timeout enforcement
- stdout/stderr capture
- output-size limits
- cancellation support

This lets skills be exposed as normal Ananke tools without embedding another protocol stack in the agent runtime.

## Quick start

### Create a catalog and seed local scores

```csharp
using Ananke.Skills;
using Ananke.Skills.OpenClaw;

var scoreStore = new JsonFileScoreStore(Path.Combine(AppContext.BaseDirectory, "skills", "scores.json"));

var catalog = new OpenClawCatalog(
    cacheDir: Path.Combine(AppContext.BaseDirectory, "skills"),
    scoreStore: scoreStore,
    enableVoting: true);

await catalog.SyncAsync();
```

### Search and resolve skills manually

```csharp
var matches = await catalog.SearchAsync("airbnb search lodging", limit: 3);

var tool = await catalog.ResolveAsync(matches[0]);
```

### Populate a `ToolKit` from natural-language intent

```csharp
using Ananke.Orchestration.Tools;

var toolkit = new ToolKit("external-skills");

await toolkit.AddFromCatalogAsync(
    catalog,
    query: "airbnb search lodging",
    limit: 5);
```

Each resolved skill becomes a normal `ToolDefinition` that an `AgentJob` can expose to a model.

## OpenClaw catalog behavior

`OpenClawCatalog` is the built-in `ISkillCatalog` implementation.

Current behavior:

- maintains a local `catalog.json` cache
- searches offline between syncs
- ranks matches by relevance plus local vote score
- resolves supported install methods into CLI-backed `ToolDefinition` entries
- can automatically record up/down votes based on execution success when `enableVoting` is enabled

Currently supported install methods:

- `Uvx`
- `Npx`

## Tool memory projection

`SkillCatalogMemorySync` decorates an `ISkillCatalog` and projects synced skills into `IToolMemory` as `ToolMemoryEntry` records. This connects the external skill catalog to Ananke's semantic tool-routing pipeline.

Use it when you want skills to be discoverable by the tool gate even before a static `ToolKit` has been manually populated.

## Operational notes

- `OpenClawCatalog.SearchAsync(...)` works from the local cache after sync
- `CliProcessRunner` truncates large output and enforces execution timeouts
- `JsonFileScoreStore` is suitable for single-process and demo scenarios
- Skill execution requires the underlying runner binary to be installed (`uvx`, `npx`, etc.)

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Orchestration` | `ToolKit`, `ToolDefinition`, and agent orchestration |
| `Ananke.Abstractions` | `IToolMemory`, `ToolMemoryEntry`, and tool-health contracts |
| `Ananke` | Meta-package for the broader orchestration stack |

## Documentation

Full docs, demos, and package guidance: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
