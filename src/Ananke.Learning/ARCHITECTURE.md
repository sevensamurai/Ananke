# Ananke.Learning — Architecture

> Empirical memory, episode tracking, reinforcement learning,
> exploration strategies, skill packaging, and entity-scoped memory.

## Role

Provides the intelligence layer for agents that learn from experience.
Tracks episodes (sequences of agent actions + outcomes), propagates rewards,
learns from tag/feature correlations, and can package learned behaviors
as portable skill files.

It complements `Ananke.Orchestration.Knowledge`: empirical memory stores evolving, experience-driven beliefs; the knowledge package stores promoted, long-term semantic knowledge.

## Dependencies

- `Ananke.Abstractions` (project)
- `Ananke.Orchestration` (project)

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Learning.EmpiricalMemory` | `IEmpiricalMemory`, `InMemoryEmpiricalMemory`, `IPredictionSource`, `TagOverlapPredictionSource`, `EmpiricalTypes`, `EmpiricalMemoryTools`, graph projection contracts |
| `Ananke.Learning.Episodes` | `IEpisodeStore`, `InMemoryEpisodeStore`, `EpisodeTypes` (Episode, Step, EpisodeOutcome), `IRewardPropagator`, `MonteCarloRewardPropagator` |
| `Ananke.Learning.Exploration` | `IExplorationStrategy`, `EpsilonGreedyExplorationStrategy`, `UcbExplorationStrategy` |
| `Ananke.Learning.Features` | `ITagImportanceTracker`, `TagImportanceTracker`, `TagImportanceMap` |
| `Ananke.Learning.Offline` | `IOfflineLearner`, `OfflineLearner`, `ISimulationSource`, `IConsolidationSummarizer` |
| `Ananke.Learning.Skills` | `ISkillPackager`, `SkillPackager`, `ISkillPackageFormat`, `JsonSkillPackageFormat` |
| `Ananke.Learning.EntityMemory` | `IEntityMemory`, `IEntityMemoryProvider`, `EntityMemoryProvider`, `EntityScopedConversationMemory`, `EntityScopedEmpiricalMemory`, `EntityScopedEpisodeStore`, `EntityScopedKnowledgeStore` |
| `Ananke.Learning.Ingestion` | `IExternalKnowledgeSource`, `ExternalKnowledgeSyncer<TEvent>` — pre-materialise domain-specific external knowledge into `IKnowledgeStore` from event-driven sources |
| `Ananke.Learning.Knowledge.Analytics` | `CommunityConsolidator`, `GraphTagImportanceTracker` — graph-based analytics over the knowledge graph (community consolidation, centrality-weighted tag importance) |
| `Ananke.Learning.Knowledge.Builders` | `DocumentStructureBuilder`, `EpisodeTrajectoryBuilder`, `TagCoOccurrenceBuilder` — projectors that populate an `IKnowledgeGraph` from learning data |
| `Ananke.Learning.Knowledge.Reporting` | `KnowledgeReportExporter` — serialises knowledge graph analytics to portable report formats |
| `Ananke.Learning.Knowledge.Retrieval` | `GraphExpandedPredictionSource` — `IPredictionSource` that expands recall via graph neighbourhood traversal |

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `IEmpiricalMemory` | Interface | Store and query empirical patterns, skills, and heuristics with reinforcement, contradiction, browse/count, and consolidation support |
| `IEpisodeStore` | Interface | Persist completed episodes and trajectories for credit assignment and packaging |
| `MonteCarloRewardPropagator` | Class | Backpropagates terminal rewards through episode steps using Monte Carlo returns |
| `IExplorationStrategy` | Interface | Exploration vs exploitation (epsilon-greedy, UCB) |
| `ITagImportanceTracker` | Interface | Tracks which features/tags are most predictive of outcomes |
| `IOfflineLearner` | Interface | Batch learning from stored entries — decay, curiosity-driven exploration, and consolidation |
| `ISkillPackager` | Interface | Exports/imports learned behavior as portable skill packages with optional episode payloads |
| `IEntityMemoryProvider` | Interface | Scopes all memory types (conversation, empirical, episodes, knowledge) to a per-entity partition |

## Learning Loop

```
1. Agent acts → EmpiricalMemory.CommitAsync(entry)
2. Outcomes arrive → ReinforceAsync / ContradictAsync adjust confidence, strength, and evidence
3. Episodes are committed → MonteCarloRewardPropagator can backpropagate terminal rewards
4. Recall / PairRecall → prior patterns, skills, and heuristics influence future choices
5. OfflineLearner runs → decay, exploration, simulation, and consolidation into IKnowledgeStore
6. SkillPackager exports/imports stable skills and linked episodes
```
