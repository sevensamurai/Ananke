<!-- topic: faq-memory, tags: faq, memory, rag, vector, empirical, learning, skills, episodes, catalog -->
# FAQ — Memory & Learning

← [Back to all FAQs](../faq.md)

---

## Memory & Knowledge (RAG)

### How does Ananke handle long-term memory?

Ananke provides three complementary memory layers:

| Layer | What it stores | Interface |
|---|---|---|
| **Semantic** | Document chunks with vector embeddings (RAG) | `IKnowledgeStore` |
| **Episodic** | Conversation history per session | `IConversationMemory` |
| **Empirical** | Patterns, skills, and heuristics learned from interactions | `IEmpiricalMemory` |

### What is RAG and does Ananke support it?

RAG (Retrieval-Augmented Generation) gives agents access to a searchable knowledge base built
from your own documents. Ananke's ingestion pipeline:

1. **Extract** — parse PDFs and Markdown into normalized text (`Ananke.Documents`)
2. **Chunk** — split with heading-aware sliding windows and configurable overlap
3. **Embed** — generate vector embeddings via `IEmbeddingModel` (OpenAI text-embedding-3)
4. **Store** — index in `IKnowledgeStore` for semantic vector search

Agents search it with `KnowledgeSearchTool` and can ingest new documents via `KnowledgeTools`.

### What vector databases are supported?

| Store | Package | Notes |
|---|---|---|
| In-memory | `Ananke.Orchestration` | Zero-config, for dev and testing |
| Qdrant | `Ananke.Qdrant` | Persistent, distributed, production-ready |

Additional providers can be added by implementing `IKnowledgeStore`.

### What is the Knowledge Catalog?

The knowledge catalog tracks document-level metadata: source, title, LLM-enriched keywords,
categories, and summaries. It supports time-decay reranking — a configurable half-life and
floor weight deprioritize older documents so recent information surfaces first.

---

## Empirical Memory & Agent Learning

### What is empirical memory?

Empirical memory is a third memory layer where agents accumulate structured knowledge from
repeated interactions. There are three kinds:

| Kind | Meaning | Example |
|---|---|---|
| `Pattern` | Observed regularities | "When GC pause exceeds 200ms, downstream timeout spikes follow within 30s" |
| `Skill` | Step-by-step procedures | "How to investigate a latency spike: check GC, then thread pool, then DB connections" |
| `Heuristic` | Rules of thumb | "Prefer async I/O over sync for all outbound calls in this service" |

Each entry carries a **confidence score** that increases on reinforcement and decreases on
contradiction — without ever deleting the entry.

### Can agents learn and improve over time?

Yes. The learning loop works as follows:

1. **Commit** — during agent execution, `EmpiricalMemoryTools` lets the LLM store newly
   discovered patterns, skills, and heuristics via `commit_insight`
2. **Recall** — before acting, the agent searches its memory via `recall_empirical` to
   surface relevant prior knowledge
3. **Reinforce** — when a recalled insight proves correct, the agent (or framework) calls
   `reinforce_empirical` to increase its confidence
4. **Contradict** — when an insight proves wrong, confidence decreases without deletion
5. **Offline sweep** — `IOfflineLearner` runs background cycles between sessions:
   decaying stale beliefs, exploring low-confidence entries via `ISimulationSource`,
   discovering connections across the full memory corpus
6. **Consolidate** — when a pattern reaches a confidence threshold, `IConsolidationSummarizer`
   promotes it into `IKnowledgeStore` as permanent knowledge

Raw LLM capability is the starting point; every deployment gets smarter over time.

### How does recall scoring work?

`RecallAsync` ranks results by a composite score:

```
score = vectorSimilarity × confidence × recencyWeight
```

Recent, high-confidence, relevant entries surface first. Entries that have been contradicted
many times naturally sink to the bottom as their confidence drops.

### What is the offline learner?

`IOfflineLearner` (implemented by `OfflineLearner` in `Ananke.Learning`) runs
background sweeps between active sessions:

- **Decay** — reduces strength of entries that haven't been confirmed recently
- **Curiosity exploration** — walks low-confidence entries to validate or contradict them
- **Simulation** — uses `ISimulationSource` to test hypotheses without real-world cost
  (self-play, Monte Carlo rollouts, scenario replay)
- **Consolidation** — promotes mature, high-confidence patterns to `IKnowledgeStore` via
  `IConsolidationSummarizer`

### What is the episode store?

`IEpisodeStore` (implemented by `InMemoryEpisodeStore`) records completed **episodes** —
ordered trajectories of agent decisions linked to a terminal reward. Each `Episode`
contains a sequence of `EpisodeStep` entries (each pointing to an `EmpiricalEntry`)
and the terminal reward received at the end. Episodes enable:

- **Monte Carlo reward propagation** — discounted returns are computed backward through the
  trajectory and used to reinforce every empirical entry proportional to its causal contribution
- **Skill packaging** — episodes are bundled with exported skill packages so the
  receiving agent has the training trajectories, not just the conclusions

```csharp
var episode = new Episode
{
    Id    = Guid.NewGuid().ToString("N"),
    Steps = [new EpisodeStep { StepIndex = 0, EntryId = entryId }],
    TerminalReward = 1.0f,   // win
    StartedAt      = DateTimeOffset.UtcNow,
    CompletedAt    = DateTimeOffset.UtcNow
};
await episodeStore.CommitAsync(episode);

// Propagate terminal reward backward through all steps
var propagator = new MonteCarloRewardPropagator();
await propagator.PropagateAsync(episode, memory);
```

### What is tag importance tracking?

`ITagImportanceTracker` (`TagImportanceTracker`) analyzes all empirical entries and
computes a `TagImportanceMap` — a normalized weight per semantic tag that reflects its
correlation with positive vs. negative outcomes:

```
importance(tag) = (positive_count - negative_count) / total_count  → normalized to [0, 1]
```

Tags that appear only in entries with positive valence score near 1.0; tags that appear
only in negative entries score near 0.0. The map is:
- Used to boost recall priority for entries with high-importance tags
- Bundled into exported skill packages so the receiving agent inherits learned feature weights
- Used by the offline learner to bias exploration toward high-importance dimensions

### What exploration strategies are available?

`IExplorationStrategy` controls the exploration–exploitation balance during action
selection. Two implementations are provided:

| Strategy | Class | When to use |
|---|---|---|
| **UCB1** | `UcbExplorationStrategy` | Principled exploration with uncertainty estimates. Balances score, visit count, and entry variance. Best for game agents and iterative planners. |
| **ε-greedy with annealing** | `EpsilonGreedyExplorationStrategy` | Simpler. Explores randomly with probability ε, exploits otherwise. ε decays over time so the agent shifts from exploration to exploitation as experience grows. |

```csharp
var ucb = new UcbExplorationStrategy(new ExplorationOptions
{
    ExplorationCoefficient = 1.414f,   // √2 is the standard UCB1 constant
    UseVarianceBonus       = true,     // add entry.Variance to exploration bonus
    VarianceBonusWeight    = 0.5f
});

var epsilon = new EpsilonGreedyExplorationStrategy(new ExplorationOptions
{
    EpsilonInitial = 0.3f,   // 30% random exploration at start
    EpsilonMin     = 0.05f,  // never drop below 5%
    EpsilonDecay   = 0.999f  // anneal slowly per selection
});
```

---

## Skill Package Export/Import

### What is a skill package?

A skill package is a portable, self-contained bundle of everything an agent has learned:

- **Empirical entries** — patterns, skills, and heuristics that passed the quality gates
  (min confidence, min strength, min observation count)
- **Episodes** — the training trajectories that produced those entries, so the
  receiving agent can re-run reward propagation if needed
- **`TagImportanceMap`** — learned feature weights showing which semantic tags
  correlate with positive outcomes in that domain
- **`TrainingManifest`** — provenance metadata: total entries, average reward,
  training duration, creation timestamp, and arbitrary statistics

Packages are streamed as JSON via `ISkillPackageFormat` / `JsonSkillPackageFormat`.

### How do I export a skill package?

```csharp
using Ananke.Learning.Skills;

var packager = new SkillPackager();
var format   = new JsonSkillPackageFormat();

await using var file   = File.OpenWrite("connect4-v1.skill.json");
await using var writer = format.CreateWriter(file);

// Optionally compute tag importance weights to bundle with the package
var tracker     = new TagImportanceTracker();
var importances = await tracker.ComputeAsync(memory);

var result = await packager.ExportAsync(
    new SkillExportOptions
    {
        Name        = "connect4-strategy",
        Domain      = "connect4",
        Version     = "1.0.0",
        Description = "Learned Connect 4 opening and mid-game strategy",
        MinConfidence   = 0.4f,    // only export confident entries
        MinStrength     = 0.3f,    // drop weak entries
        MinObservations = 3,       // at least 3 sightings
        IncludeEpisodes = true     // bundle training trajectories
    },
    memory,
    writer,
    episodes:      episodeStore,
    tagImportances: importances);

Console.WriteLine($"Exported {result.EntriesExported} entries, {result.EpisodesExported} episodes");
```

### How do I import a skill package?

```csharp
await using var file   = File.OpenRead("connect4-v1.skill.json");
var reader = await format.CreateReaderAsync(file);

var result = await packager.ImportAsync(
    reader,
    targetMemory,
    episodes: targetEpisodeStore,
    options: new SkillImportOptions
    {
        TrustScale = 0.7f   // scale down imported confidence — trust but verify
    });

Console.WriteLine($"Imported {result.Added} new, {result.Merged} merged, {result.Skipped} skipped");
```

The importer applies **trust scaling** to every imported entry's confidence and
strength. Set `TrustScale` below 1.0 to let the receiving agent re-validate the
knowledge against its own environment before fully trusting it.

### Can I transfer skills between different agent domains?

Yes, with care. The quality gates on export and trust scaling on import are the
controls. For cross-domain transfer, set a lower `TrustScale` and run offline
learning sweeps so the receiving agent can reinforce, contradict, or decay the
imported entries based on its own experience.

### What merge semantics apply on import?

When an imported entry is semantically similar (above the dedup threshold) to an
existing entry, the packager merges rather than duplicates: evidence is combined,
and confidence is updated according to the import's `TrustScale`. Entries below the
similarity threshold are added as new entries.

---

## External Skill Catalog

### What is the External Skill Catalog?

`Ananke.Skills` provides `ISkillCatalog` — a protocol-agnostic interface for
discovering, caching, and running tools from external registries. The first
implementation, `OpenClawCatalog`, integrates with the
[OpenClaw/ClawHub](https://clawhub.io) registry of CLI-based tools.

This is distinct from the _learned_ skills in `Ananke.Learning`. External skills are
discovered from a registry and run as CLI processes; learned skills are patterns,
heuristics, and procedures accumulated from the agent's own experience.

### How do I discover and add catalog skills to an agent?

```bash
dotnet add package Ananke.Skills
```

```csharp
using Ananke.Skills;
using Ananke.Skills.OpenClaw;

// Create a catalog backed by a local cache directory
var catalog = new OpenClawCatalog(
    cacheDir: Path.Combine(AppContext.BaseDirectory, ".skill-cache"),
    enableVoting: true);   // auto up/down vote on success/failure

// Sync the remote registry once (or on a timer)
await catalog.SyncAsync();

// Discover and add matching skills to an agent's ToolKit in one call
var tools = new ToolKit("research");
await tools.AddFromCatalogAsync(catalog, "airbnb search lodging", limit: 3);

// Use the toolkit in any AgentJob or StreamingChatWorkflow
```

After `SyncAsync()`, all subsequent `SearchAsync()` calls operate entirely offline
from the local cache.

### How does skill scoring and voting work?

`ISkillScoreStore` (implemented by `JsonFileScoreStore`) tracks local up/down votes
for each skill. When `enableVoting: true` is set on `OpenClawCatalog`, successful
tool executions automatically record an up-vote; failed executions record a
down-vote. Scores influence search ranking, and skills with negative net scores are
filtered out of results.

```csharp
// Manual vote
var scoreStore = new JsonFileScoreStore(cacheDir);
await scoreStore.RecordVoteAsync("stveenli/airbnb", VoteDirection.Up);

var score = await scoreStore.GetScoreAsync("stveenli/airbnb");
Console.WriteLine($"Up: {score.UpVotes}, Down: {score.DownVotes}");
```

### Can a C# agent call tools written in Python?

Yes. `Ananke.Skills` bridges the language boundary through subprocesses, not interop.
A Python tool is just a CLI binary from the agent's perspective — the C# agent calls
`CliProcessRunner.RunAsync("uvx", "airbnb-search \"Denver, CO\"")` and gets back stdout.
No P/Invoke, no Python runtime embedded in the .NET process, no FFI.

The key enabler is **`uvx`** from the [uv](https://docs.astral.sh/uv/) package manager.
`uvx` downloads and runs any PyPI package in an isolated cache:

```powershell
# Install uv once (Windows)
winget install astral-sh.uv

# uvx then runs any Python tool on demand — no pip, no venv
uvx airbnb-search "Denver, CO" --checkin 2025-08-01 --checkout 2025-08-03
```

On the Ananke side:

```csharp
var catalog = new OpenClawCatalog(
    cacheDir: ".skill-cache",
    enableVoting: true);
await catalog.SyncAsync();   // populate local cache from OpenClaw registry

var tools = new ToolKit("travel");
await tools.AddFromCatalogAsync(catalog, "airbnb search lodging", limit: 3);
// tools now contains ToolDefinitions backed by Python processes
// — the LLM calls them exactly like any other Ananke tool
```

The Python process runs, returns JSON to stdout, and the C# agent reads it. The LLM
never knows the tool is Python.

> See [uv & uvx Setup for .NET Developers](../guides/uv-setup-for-dotnet-developers.md)
> for a setup walkthrough aimed at C#/.NET developers with no Python background.

### What runtimes do catalog skills support?

The `SkillInstallMethod` enum controls how a skill's binary is launched:

| Method | What it does |
|---|---|
| `Uvx` | Runs the tool with `uvx <package>` — Python tools from PyPI, no install step |
| `Npx` | Runs the tool with `npx <package>` — Node.js tools from npm |
| `Docker` | Runs the tool in a Docker container |
| `Shell` | Runs an arbitrary shell command |

Most tools in the OpenClaw registry use `Uvx`. The C# agent never knows what
language the tool is written in — it receives a string result and continues.

### Can I add my own skills to the catalog?

The `ISkillCatalog` interface is designed to be implemented for any registry.
The `OpenClawCatalog` is the first implementation; future implementations could
target other registries, internal tool directories, or local config files.

---

← [Back to all FAQs](../faq.md) · [Feature Index](../reference/features.md) · [Getting Started](../guides/01-getting-started.md)
