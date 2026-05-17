# Ananke.Learning

[![NuGet](https://img.shields.io/nuget/v/Ananke.Learning.svg)](https://www.nuget.org/packages/Ananke.Learning)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Empirical memory and experience-based learning for .NET AI agents — commit observations, recall patterns and heuristics, reinforce or contradict beliefs, run offline learning cycles, track episodes, and package learned behavior for transfer.

Part of the [Ananke](https://github.com/sevensamurai/Ananke) framework.

## Install

```bash
dotnet add package Ananke.Learning
```

This package depends on `Ananke.Orchestration` and `Ananke.Abstractions`.

## Key concepts

```
Observation ----> CommitAsync ----> EmpiricalEntry (stored in IEmpiricalMemory)
                                         |
Query -----------> RecallAsync ----------|  vector similarity + tag overlap
                                         |
Outcome ---------> ReinforceAsync -------|  prediction-error -> confidence/strength
                                         |
Background ------> LearnAsync -----------+  decay, curiosity, simulation, consolidation
```

An **empirical entry** represents a learned pattern, skill, or heuristic. Entries carry structured semantic descriptions, tags, confidence, observation history, affective signals, optional episode linkage, and optional entity scope. They are committed during agent execution, recalled by similarity, reinforced or contradicted by outcomes, and periodically swept by the offline learner.

## What is included

### Core interfaces

| Type | Description |
|---|---|
| `IEmpiricalMemory` | Mutable store for empirical entries — commit, recall, reinforce, contradict, browse, count, mark consolidated, and pair-recall |
| `IEpisodeStore` | Persistence contract for completed action trajectories and outcomes |
| `IOfflineLearner` | Background learning sweep — decay, curiosity-driven exploration, and consolidation of mature entries |
| `ISimulationSource` | Domain-specific simulator that tests hypotheses and returns reward outcomes |
| `IPredictionSource` | Predicts expected confidence for an entry, enabling prediction-error reinforcement |
| `IConsolidationSummarizer` | Generates a summary when an entry is promoted from empirical memory to long-term knowledge |
| `IEntityMemoryProvider` | Creates entity-scoped memory facades over shared underlying stores |
| `IExplorationStrategy` | Exploration/exploitation strategy for choosing among predicted actions |
| `ITagImportanceTracker` | Tracks predictive importance of tags/features across outcomes |
| `ISkillPackager` | Exports/imports skill packages built from empirical entries and episodes |

### Built-in implementations

| Type | Description |
|---|---|
| `InMemoryEmpiricalMemory` | Thread-safe in-memory `IEmpiricalMemory` with cosine-similarity recall and tag-overlap scoring |
| `OfflineLearner` | Default offline learning pipeline — decay, exploration, simulation, and consolidation |
| `InMemoryEpisodeStore` | In-memory persistence for completed episodes |
| `TagOverlapPredictionSource` | Predicts confidence from the average confidence of entries sharing the most tags |
| `MonteCarloRewardPropagator` | Propagates terminal rewards backward through episode steps |
| `TagImportanceTracker` | Default feature/tag importance tracker |
| `EntityMemoryProvider` | Lazily creates entity-scoped memory facades |
| `SkillPackager` | Default skill export/import implementation |
| `JsonSkillPackageFormat` | JSON-based skill package format |

### Data types

| Type | Description |
|---|---|
| `EmpiricalEntry` | Learned pattern/skill/heuristic with semantic description, tags, confidence, evidence, affective signals, and optional episode/entity linkage |
| `EmpiricalKind` | Taxonomy — `Pattern`, `Skill`, `Heuristic` |
| `SemanticDescription` | Structured semantic summary and weighted tags used for embedding and tag overlap |
| `EmpiricalMatch` | Recall result with the matched entry and composite score |
| `Reinforcement` | Signal applied to an entry — evidence, reward, source, and adjustments |
| `RecallOptions`, `BrowseOptions`, `PairRecallOptions` | Recall and browse configuration |
| `AffectOptions` | Configures prediction-error and affect-driven learning behavior |
| `Episode`, `EpisodeStep`, `EpisodeOutcome` | Episode trajectory model used for credit assignment and skill packaging |
| `OfflineLearnerOptions`, `OfflineLearningResult` | Offline sweep configuration and outcome summary |
| `SkillExportOptions`, `SkillImportOptions`, `SkillExportResult`, `SkillImportResult` | Skill package transfer configuration and results |

### Agent tool integration

| Type | Description |
|---|---|
| `EmpiricalMemoryTools` | `ToolKit` factory — registers `recall_empirical`, `commit_insight`, and `reinforce_empirical` as agent-callable tools |

## Quick start

### Commit and recall

```csharp
using Ananke.Learning.EmpiricalMemory;
using Ananke.Orchestration.Knowledge.Embeddings;

var embedder = new InMemoryEmbedder();
var memory = new InMemoryEmpiricalMemory(embedder);

// Commit an observation
var entry = await memory.CommitAsync(new EmpiricalEntry
{
    Id = Guid.NewGuid().ToString("N"),
    Kind = EmpiricalKind.Pattern,
    Tags = ["strategy", "center-control"],
    Source = "manual-observation",
    Description = new SemanticDescription
    {
        Summary = "Placing pieces in the center column creates more threats",
        SemanticTags = new Dictionary<string, float>
        {
            ["strategy:center-control"] = 1.0f,
            ["domain:connect4"] = 0.8f
        }
    },
    Confidence = 0.5f,
    ObservationCount = 1,
    Evidence = [],
    FirstObserved = DateTimeOffset.UtcNow,
    LastObserved = DateTimeOffset.UtcNow
});

// Recall by similarity
var matches = await memory.RecallAsync(
    "what strategy works for opening moves?",
    new RecallOptions { TopK = 5 });
```

### Reinforce with outcome

```csharp
await memory.ReinforceAsync(entry.Id, new Reinforcement
{
    Reward = 1.0f,
    NewEvidence = ["won the game after using this strategy"],
    Source = "game-outcome"
});
```

### Run offline learning

```csharp
using Ananke.Abstractions.Agents;
using Ananke.Learning.Offline;
using Ananke.Orchestration.Knowledge;

var knowledgeStore = new InMemoryKnowledgeStore(embedder);
var learner = new OfflineLearner(
    memory,
    embedder,
    knowledgeStore: knowledgeStore,
    options: new OfflineLearnerOptions
    {
        ExplorationBatchSize = 5,
        ConsolidationMinObservations = 10,
        ConsolidationMinStrength = 0.8f,
    });

var result = await learner.LearnAsync();

// result.Decayed, result.Explored, result.Consolidated
```

### Entity-scoped memory

```csharp
using Ananke.Learning.EntityMemory;

var provider = new EntityMemoryProvider(
    conversationMemory,
    memory,
    episodeStore,
    knowledgeStore);

var customerMemory = provider.GetOrCreate("customer-123");
var customerPatterns = await customerMemory.Empirical.RecallAsync("billing dispute");
```

### Skill export/import

```csharp
using Ananke.Learning.Skills;

var packager = new SkillPackager();
await using var writer = new JsonSkillPackageFormat().CreateWriter(stream);

var export = await packager.ExportAsync(
    new SkillExportOptions { Name = "support-playbook", Domain = "customer-support" },
    memory,
    writer,
    episodes: episodeStore);
```

### Expose as agent tools

```csharp
var toolkit = EmpiricalMemoryTools.Create(memory);

// Register toolkit.Tools with an AgentJob so the LLM can
// recall patterns, commit new observations, and reinforce entries
// through natural-language tool calls.
```

## Learning lifecycle

1. **Commit** — Agent observes something and commits it as an `EmpiricalEntry` with semantic tags and an embedding.
2. **Recall** — On future decisions, the agent recalls similar entries by vector cosine similarity and tag overlap.
3. **Reinforce** — When outcomes arrive, entries are reinforced (positive or negative). Prediction-error mechanics update confidence, strength, variance, and affective signals.
4. **Contradict** — Entries disproven by evidence have their confidence reduced.
5. **Episode tracking** — Completed action sequences can be persisted and replayed for reward propagation and packaging.
6. **Offline sweep** — A background process runs periodically:
   - **Decay** removes entries whose strength has fallen below threshold.
   - **Exploration** selects curious (high-variance, low-observation) entries and tests them via `ISimulationSource`.
   - **Consolidation** promotes mature, high-confidence entries into `IKnowledgeStore` for long-term retention.
7. **Packaging** — Stable skills and supporting episodes can be exported and imported between agents or environments.

## Package boundaries

- `Ananke.Learning` owns empirical memory, episodes, offline learning, feature tracking, entity-scoped facades, and skill packaging.
- `Ananke.Orchestration.Knowledge` supplies the long-term knowledge store used during consolidation.
- `Ananke.Orchestration` supplies `ToolKit` integration for exposing learning capabilities to agents.

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Orchestration` | Workflow engine, tool integration, and access to the knowledge package |
| `Ananke.Orchestration.OpenAI` | OpenAI embedding model for production vector search |
| `Ananke.Qdrant` | Qdrant-backed `IEmpiricalMemory` and `IKnowledgeStore` for persistent storage |

## Worked example

See the Connect4 learning demo in the repository for an agent that learns strategy through self-play, empirical memory, and offline consolidation.

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
