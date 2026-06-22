# Ananke.Learning — Architecture

> Empirical memory, episode tracking, reinforcement learning,
> exploration strategies, skill packaging, and entity-scoped memory.

## Role

Provides the intelligence layer for agents that learn from experience.
Tracks episodes (sequences of agent actions + outcomes), propagates rewards,
learns from tag/feature correlations, and can package learned behaviors
as portable skill files.

It complements `Ananke.Orchestration.Knowledge`: empirical memory stores evolving, experience-driven beliefs; the knowledge package stores promoted, long-term semantic knowledge.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `IEmpiricalMemory` — store and query empirical patterns, skills, and heuristics with
   reinforcement and contradiction support — `src/Ananke.Learning/EmpiricalMemory/IEmpiricalMemory.cs`
2. `IEpisodeStore` — persists completed episodes and trajectories for credit assignment and
   packaging — `src/Ananke.Learning/Episodes/IEpisodeStore.cs`
3. `IOfflineLearner` — batch learning from stored entries: decay, curiosity-driven exploration,
   and consolidation — `src/Ananke.Learning/Offline/IOfflineLearner.cs`
4. `ISkillPackager` — exports/imports learned behavior as portable skill packages — `src/Ananke.Learning/Skills/ISkillPackager.cs`
5. `IEntityMemoryProvider` — scopes all memory types (conversation, empirical, episodes,
   knowledge) to a per-entity partition — `src/Ananke.Learning/EntityMemory/IEntityMemoryProvider.cs`

---

## Dependencies

- `Ananke.Abstractions` (project)
- `Ananke.Orchestration` (project)

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Learning.EmpiricalMemory` | `IEmpiricalMemory`, `InMemoryEmpiricalMemory`, `IPredictionSource`, `TagOverlapPredictionSource`, `EmpiricalTypes`, `EmpiricalMemoryTools`, graph projection contracts |
| `Ananke.Learning.Episodes` | `IEpisodeStore`, `InMemoryEpisodeStore`, `EpisodeTypes` (`Episode`, `EpisodeStep`), `IRewardPropagator`, `MonteCarloRewardPropagator` |
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

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `IEmpiricalMemory` | Interface | Store and query empirical patterns, skills, and heuristics with reinforcement, contradiction, browse/count, and consolidation support | `src/Ananke.Learning/EmpiricalMemory/IEmpiricalMemory.cs` |
| `IEpisodeStore` | Interface | Persist completed episodes and trajectories for credit assignment and packaging | `src/Ananke.Learning/Episodes/IEpisodeStore.cs` |
| `MonteCarloRewardPropagator` | Class | Backpropagates terminal rewards through episode steps using Monte Carlo returns | `src/Ananke.Learning/Episodes/MonteCarloRewardPropagator.cs` |
| `IExplorationStrategy` | Interface | Exploration vs exploitation (epsilon-greedy, UCB) | `src/Ananke.Learning/Exploration/IExplorationStrategy.cs` |
| `ITagImportanceTracker` | Interface | Tracks which features/tags are most predictive of outcomes | `src/Ananke.Learning/Features/ITagImportanceTracker.cs` |
| `IOfflineLearner` | Interface | Batch learning from stored entries — decay, curiosity-driven exploration, and consolidation | `src/Ananke.Learning/Offline/IOfflineLearner.cs` |
| `ISkillPackager` | Interface | Exports/imports learned behavior as portable skill packages with optional episode payloads | `src/Ananke.Learning/Skills/ISkillPackager.cs` |
| `IEntityMemoryProvider` | Interface | Scopes all memory types (conversation, empirical, episodes, knowledge) to a per-entity partition | `src/Ananke.Learning/EntityMemory/IEntityMemoryProvider.cs` |

## Learning Loop

```
1. Agent acts → EmpiricalMemory.CommitAsync(entry)
2. Outcomes arrive → ReinforceAsync / ContradictAsync adjust confidence, strength, and evidence
3. Episodes are committed → MonteCarloRewardPropagator can backpropagate terminal rewards
4. Recall / PairRecall → prior patterns, skills, and heuristics influence future choices
5. OfflineLearner runs → decay, exploration, simulation, and consolidation into IKnowledgeStore
6. SkillPackager exports/imports stable skills and linked episodes
```
