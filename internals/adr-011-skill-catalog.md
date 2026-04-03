# ADR-011 — Skill Catalog: External Tool Discovery and Execution

**Status:** Proposed  
**Date:** 2025-07-22  
**Deciders:** Core team  
**Release target:** Next minor (post-current)

---

## Context

Ananke agents currently discover tools through three imperative mechanisms:

| Mechanism | How it works |
|---|---|
| `ToolKit.AddTool(...)` | Developer defines tools in C# code |
| `ToolKit.AddMcpServerToolsAsync(client)` | Discovers tools from a connected MCP server |
| `A2AAgentDiscovery.DiscoverAsync(uri)` | Resolves agent cards from a remote A2A endpoint |

All three require the developer to know **at build time** which tools exist and where
they live. There is no way to browse a catalog of community-contributed tools,
discover new capabilities at runtime, or let the agent itself select from a broader
pool of available skills.

Meanwhile, open registries of agent tools are emerging. [OpenClaw/ClawHub](https://github.com/openclaw/skills)
hosts thousands of community skills — most are Python CLI tools runnable via `uvx`
with no API key, no install step, and JSON output support. Example: the
`airbnb-search` skill lets you search Airbnb listings from the command line.

The opportunity is to let Ananke agents tap into these registries without
rewriting tools in C# — and without coupling to any specific protocol (MCP, A2A,
or whatever comes next).

---

## Decision

Introduce a **Skill Catalog** subsystem in a new `Ananke.Skills` project that:

1. **Defines a protocol-agnostic `ISkillCatalog` interface** for discovering,
   scoring, and resolving external skills into `ToolDefinition` entries.
2. **Ships an OpenClaw catalog provider** as the first implementation.
3. **Bridges CLI-based tools** via process execution — no protocol dependency.
4. **Includes a local scoring/voting store** so agents and operators can
   up-vote or down-vote skills over time, influencing future selection.
5. **Syncs periodically** from remote registries, caching metadata locally.

---

## Architecture

### Layer diagram

```
┌──────────────────────────────────────────────────────────┐
│                     AgentJob / AgentRouter                │
│                 (unchanged — consumes ToolKit)            │
└────────────────────────┬─────────────────────────────────┘
                         │
                    ToolKit.AddTool(ToolDefinition)
                         │
┌────────────────────────┴─────────────────────────────────┐
│                  Skill Catalog Layer                       │
│                                                           │
│  ISkillCatalog                                            │
│    SearchAsync(query, tags?, limit?)                       │
│      → IReadOnlyList<SkillDescriptor>                     │
│                                                           │
│    ResolveAsync(descriptor)                                │
│      → ToolDefinition           (lazy — only when picked) │
│                                                           │
│  ISkillScoreStore                                         │
│    RecordVoteAsync(skillId, direction)                     │
│    GetScoreAsync(skillId) → SkillScore                    │
│                                                           │
│  SkillDescriptor                                          │
│    Id, Name, Description, Tags, Source,                   │
│    InstallMethod, Score                                   │
│                                                           │
│  SkillScore                                               │
│    UpVotes, DownVotes, NetScore, LastVoted                │
└────────────────────────┬─────────────────────────────────┘
                         │
          ┌──────────────┼──────────────┐
          │              │              │
   OpenClawCatalog   (future)      (future)
   (parses SKILL.md   McpRegistry   A2ACatalog
    or ClawHub API)
```

### Key types

```csharp
// Lightweight metadata — cheap to load and cache
public record SkillDescriptor
{
    public required string Id { get; init; }          // e.g. "stveenli/airbnb"
    public required string Name { get; init; }        // e.g. "airbnb-search"
    public required string Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? Homepage { get; init; }
    public SkillInstallMethod Install { get; init; }  // Uvx, Npx, Docker, etc.
    public string? InstallPackage { get; init; }      // e.g. "airbnb-search"
    public SkillScore Score { get; init; }
}

public enum SkillInstallMethod { Uvx, Npx, Docker, Shell }

public record SkillScore(int UpVotes = 0, int DownVotes = 0)
{
    public int Net => UpVotes - DownVotes;
}
```

```csharp
// The catalog interface — protocol-agnostic
public interface ISkillCatalog
{
    Task<IReadOnlyList<SkillDescriptor>> SearchAsync(
        string query,
        IReadOnlyList<string>? tags = null,
        int limit = 20,
        CancellationToken ct = default);

    Task<ToolDefinition> ResolveAsync(
        SkillDescriptor skill,
        CancellationToken ct = default);

    Task SyncAsync(CancellationToken ct = default);
}
```

```csharp
// Local scoring — persisted to JSON file, SQLite, or any ISkillScoreStore
public interface ISkillScoreStore
{
    Task RecordVoteAsync(string skillId, VoteDirection direction, CancellationToken ct = default);
    Task<SkillScore> GetScoreAsync(string skillId, CancellationToken ct = default);
}

public enum VoteDirection { Up, Down }
```

### Execution bridge (CLI process)

When `ResolveAsync` is called for a `SkillInstallMethod.Uvx` skill, it produces
a `ToolDefinition` whose `Execute` delegate spawns the CLI process:

```csharp
// Inside OpenClawCatalog.ResolveAsync
Execute = async (args, ct) =>
{
    var cliArgs = BuildCliArgs(skill, args);  // maps tool params → CLI flags
    var (exitCode, stdout, stderr) = await RunProcessAsync(
        "uvx", $"{skill.InstallPackage} {cliArgs} --output json", ct);

    return exitCode == 0
        ? ToolResult.Ok(stdout)
        : ToolResult.Error($"{skill.Name} failed (exit {exitCode}): {stderr}");
}
```

No MCP. No A2A. Just a process call whose stdout becomes the `ToolResult`.

### ToolKit integration

```csharp
// Extension method on ToolKit (in Ananke.Skills)
public static async Task<ToolKit> AddFromCatalogAsync(
    this ToolKit toolkit,
    ISkillCatalog catalog,
    string query,
    int limit = 5,
    CancellationToken ct = default)
{
    var skills = await catalog.SearchAsync(query, limit: limit, ct: ct);

    foreach (var skill in skills.Where(s => s.Score.Net >= 0))
    {
        var tool = await catalog.ResolveAsync(skill, ct);
        toolkit.AddTool(tool);
    }

    return toolkit;
}
```

Usage:

```csharp
var catalog = new OpenClawCatalog("./skill-cache");

var toolkit = await new ToolKit("travel")
    .AddFromCatalogAsync(catalog, "airbnb search lodging");

// toolkit now contains the airbnb-search tool — usable in any AgentJob or Router
```

### Sync and caching

```
OpenClawCatalog
  ├── SyncAsync()          → fetches SKILL.md index from GitHub / ClawHub API
  ├── skill-cache/
  │   ├── catalog.json     ← cached skill descriptors (name, desc, tags, install)
  │   └── scores.json      ← local up/down votes
  └── SearchAsync()        → queries local cache, ranked by score + relevance
```

- `SyncAsync` is called on startup or on a timer (e.g. daily).
- Between syncs, `SearchAsync` operates entirely from the local cache.
- Scores are local-only — they reflect this deployment's experience with each skill.

### Scoring flow

```
Agent calls tool  ──▶  ToolResult.Ok(...)   ──▶  auto-upvote (success)
                  ──▶  ToolResult.Error(...) ──▶  auto-downvote (failure)
                  ──▶  process timeout/crash ──▶  auto-downvote

Operator can also vote manually:
  await scoreStore.RecordVoteAsync("stveenli/airbnb", VoteDirection.Down);
```

Over time, unreliable skills sink below the `Net >= 0` threshold and stop being
offered to agents. Reliable ones float to the top of search results.

---

## Project structure

```
Ananke.Skills/
  Ananke.Skills.csproj
  ISkillCatalog.cs
  ISkillScoreStore.cs
  SkillDescriptor.cs
  SkillScore.cs
  OpenClaw/
    OpenClawCatalog.cs       ← GitHub/ClawHub parser + CLI bridge
    SkillMdParser.cs         ← SKILL.md metadata extractor
    CliProcessRunner.cs      ← safe process execution with timeout
  Scoring/
    JsonFileScoreStore.cs    ← simple file-based score persistence
  ToolKitSkillExtensions.cs  ← AddFromCatalogAsync extension
```

---

## Demo: SkillCatalogDemo

A minimal console demo using the Airbnb skill (no API key required):

```
demos/SkillCatalogDemo/
  SkillCatalogDemo.csproj
  Program.cs
```

### Program.cs sketch

```csharp
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using Ananke.Skills;
using Ananke.Skills.OpenClaw;

// 1. Set up the skill catalog
var catalog = new OpenClawCatalog("./skill-cache");
await catalog.SyncAsync();

// 2. Build a toolkit from the catalog — searches for travel/lodging skills
var toolkit = await new ToolKit("travel")
    .AddFromCatalogAsync(catalog, "airbnb search lodging", limit: 3);

// 3. Wire into an agent
var agent = new AgentJob<TravelState, TravelResult>.Builder("travel-planner", model)
    .WithSystemPrompt("""
        You are a travel assistant. Use the available tools to search for
        accommodation. Summarize the top options with prices and ratings.
        """)
    .WithPrompt(s => s.Query)
    .WithTools(toolkit)
    .WithMapResult((s, r) => s with { Result = r })
    .Build();

// 4. Run
var state = new TravelState("Find a cozy cabin in Steamboat Springs, CO for March 1-3 2026");
var result = await agent.ExecuteAsync(state);

Console.WriteLine(result.Result.Summary);

// Records
record TravelState(string Query, TravelResult? Result = null);
record TravelResult(string Summary);
```

### What the agent does at runtime

1. Receives the travel query
2. Sees `airbnb_search` in its tool list (populated from the catalog)
3. Calls the tool with `{"query": "Steamboat Springs, CO", "checkin": "2026-03-01", ...}`
4. Framework spawns `uvx airbnb-search "Steamboat Springs, CO" --checkin 2026-03-01 --checkout 2026-03-03 --output json`
5. JSON output → `ToolResult.Ok(stdout)` → fed back to the LLM
6. Agent summarizes the listings as `TravelResult`

---

## What changes and what doesn't

| Component | Changes? | Details |
|---|---|---|
| `ToolDefinition` | ✅ `Requires` property | Declares runtime prerequisites (e.g. `uvx` on PATH) |
| `ToolKit` | ✅ `CheckPrerequisitesAsync()` | Validates all prerequisites at startup, returns pass/fail report |
| `ToolPrerequisite` | ✅ New type | Extensible check + install hint; ships with `Binary()` factory |
| `AgentJob` | ❌ No | Consumes `ToolKit` as-is |
| `AgentRouter` | ❌ No | Consumes `ToolKit` as-is |
| `ToolResult` | ❌ No | `Ok`/`Error` covers the CLI bridge |
| New: `Ananke.Skills` | ✅ New project | Catalog, scoring, CLI bridge |
| New: `SkillCatalogDemo` | ✅ New demo | Airbnb search end-to-end |

---

## Security considerations

- **Process sandboxing:** CLI tools run as child processes. Consider timeout
  limits, output size caps, and optional sandboxing (e.g. `--no-network` for
  Docker-based skills).
- **Skill vetting:** OpenClaw's own disclaimer notes potentially suspicious
  skills. The scoring system mitigates this (untested skills start at 0,
  failures push them negative), but operators should review skills before
  enabling in production.
- **No credential leaking:** The CLI bridge passes only the declared parameters.
  Environment variables and secrets are not forwarded to child processes.

---

## Alternatives considered

| Alternative | Why not |
|---|---|
| Wrap OpenClaw skills in MCP servers | Adds an unnecessary protocol hop. Skills are CLI tools — calling them directly is simpler and faster. |
| Rewrite popular skills in C# | Defeats the purpose. The value is consuming the existing ecosystem as-is. |
| Depend on ClawHub API only | The GitHub repo is the source of truth. Supporting both (API for convenience, repo for offline) gives resilience. |
| Use Docker for all skills | Overkill for `uvx`-based tools. Docker can be a future `SkillInstallMethod` for skills that need isolation. |

---

## Implementation phases

### Phase 1 — Core catalog ✅ **complete**
- ✅ `ToolPrerequisite` + `ToolDefinition.Requires` + `ToolKit.CheckPrerequisitesAsync()`
- ✅ `ISkillCatalog`, `SkillDescriptor`, `OpenClawCatalog` (in `Ananke.Skills`)
- ✅ `CliProcessRunner` with timeout, output size limit, and cancellation
- ✅ `ISkillScoreStore` + `JsonFileScoreStore` (pulled forward from Phase 2)
- ✅ Auto-voting on tool success/failure in `OpenClawCatalog.ResolveAsync`
- ✅ Score-weighted search ranking in `SearchAsync`
- ✅ `ToolKitSkillExtensions.AddFromCatalogAsync` (filters negative scores)
- ✅ `SkillCatalogDemo` with Airbnb search end-to-end
- ✅ 25 tests (`OpenClawCatalogTests`, `JsonFileScoreStoreTests`, `ToolKitSkillExtensionsTests`)

### Phase 2 — Remote sync
- `SyncAsync` fetches from ClawHub API or parses SKILL.md from GitHub repo
- `SkillMdParser` — SKILL.md metadata extractor
- Catalog sync scheduling (background service)

### Phase 3 — Enrichment
- Parameter mapping heuristics (LLM args → CLI flags)
- Multiple install methods (Docker, Shell)
- Additional catalog providers (if new registries emerge)
