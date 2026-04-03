# Ananke.Learning

[![NuGet](https://img.shields.io/nuget/v/Ananke.Learning.svg)](https://www.nuget.org/packages/Ananke.Learning)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Empirical memory and skill learning for .NET AI agents — commit observations, recall by semantic similarity, reinforce with prediction-error feedback, and run offline learning sweeps that decay weak beliefs, explore curiosity-driven hypotheses, and consolidate mature patterns into long-term knowledge.

Part of the [Ananke](https://github.com/sevensamurai/Ananke) framework.

## Install

```bash
dotnet add package Ananke.Learning
```

This package depends on `Ananke.Orchestration` (which transitively includes `Ananke.Abstractions`).

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

An **empirical entry** represents a single learned observation — a pattern, skill, or heuristic — with semantic tags, an embedding vector, confidence, strength, and affective signals (valence/intensity). Entries are committed during agent execution, recalled by similarity, reinforced when outcomes arrive, and periodically swept by the offline learner.

## What is included

### Core interfaces

| Type | Description |
|---|---|
| `IEmpiricalMemory` | Mutable store for empirical entries — commit, recall, reinforce, contradict, browse, decay |
| `IOfflineLearner` | Background learning sweep — decays weak entries, explores curious hypotheses via simulation, consolidates mature patterns into knowledge |
| `ISimulationSource` | Domain-specific simulator that tests hypotheses and returns reward outcomes |
| `IPredictionSource` | Predicts expected confidence for an entry, enabling prediction-error reinforcement |
| `IConsolidationSummarizer` | Generates a summary when an entry is promoted from empirical memory to long-term knowledge |

### In-memory implementations

| Type | Description |
|---|---|
| `InMemoryEmpiricalMemory` | Thread-safe in-memory `IEmpiricalMemory` with cosine-similarity recall and tag-overlap scoring |
| `InMemoryOfflineLearner` | Full offline learning pipeline — decay, exploration, simulation, consolidation |
| `TagOverlapPredictionSource` | Predicts confidence from the average confidence of entries sharing the most tags |
| `TemplateConsolidationSummarizer` | Formats a consolidation summary from a string template |

### Data types

| Type | Description |
|---|---|
| `EmpiricalEntry` | A learned observation with identity, semantic description, embedding, confidence, strength, variance, valence, intensity, and evidence trail |
| `EmpiricalKind` | Taxonomy — `Pattern` (what is observed), `Skill` (how to act), `Heuristic` (rules of thumb) |
| `SemanticDescription` | Structured description with weighted tags, text summary, and embedding source text |
| `EmpiricalMatch` | A recall result with the matched entry and composite similarity score |
| `Reinforcement` | Signal applied to an entry — reward, evidence source, and optional affect |
| `RecallOptions` | Controls recall — top-K, tag filter, kind filter, minimum confidence, and affect boost |
| `AffectOptions` | Configures how valence and intensity influence recall priority |
| `SimulationOutcome` | Result of a simulated hypothesis test — reward and summary |
| `OfflineLearnerOptions` | Configures the offline sweep — decay rate, exploration batch size, consolidation thresholds |
| `OfflineLearningResult` | Statistics from a completed learning sweep |

### Agent tool integration

| Type | Description |
|---|---|
| `EmpiricalMemoryTools` | `ToolKit` factory — registers `recall`, `commit`, and `reinforce` as agent-callable tools so LLMs can interact with empirical memory directly |

## Quick start

### Commit and recall

```csharp
using Ananke.Learning;
using Ananke.Orchestration.Knowledge.Embeddings;

var embedder = new InMemoryEmbedder();
var memory = new InMemoryEmpiricalMemory(embedder);

// Commit an observation
var entry = await memory.CommitAsync(new EmpiricalEntry
{
    Kind = EmpiricalKind.Pattern,
    Description = "Center control leads to more wins",
    Semantic = new SemanticDescription(
        Tags: [new("strategy", "center-control", 1.0f)],
        Summary: "Placing pieces in the center column creates more threats",
        EmbeddingSourceText: "center column control strategy"),
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
    Evidence = "won the game after using this strategy",
});
```

### Run offline learning

```csharp
using Ananke.Orchestration.Knowledge;

var knowledgeStore = new InMemoryKnowledgeStore(embedder);
var learner = new InMemoryOfflineLearner();

var result = await learner.LearnAsync(
    memory,
    knowledgeStore,
    new OfflineLearnerOptions
    {
        DecayRate = 0.02f,
        ExplorationBatchSize = 5,
        ConsolidationMinObservations = 10,
        ConsolidationMinStrength = 0.7f,
    });

// result.Decayed, result.Explored, result.Consolidated
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
5. **Offline sweep** — A background process runs periodically:
   - **Decay** removes entries whose strength has fallen below threshold.
   - **Exploration** selects curious (high-variance, low-observation) entries and tests them via `ISimulationSource`.
   - **Consolidation** promotes mature, high-confidence entries into `IKnowledgeStore` for long-term retention.

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Orchestration` | Workflow engine, knowledge store, embedding model |
| `Ananke.Orchestration.OpenAI` | OpenAI embedding model for production vector search |
| `Ananke.Qdrant` | Qdrant-backed `IEmpiricalMemory` and `IKnowledgeStore` for persistent storage |

## Worked example

See the **[Connect4Demo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/Connect4Demo)** — an agent that learns Connect Four strategy through self-play, empirical memory, and offline consolidation.

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
