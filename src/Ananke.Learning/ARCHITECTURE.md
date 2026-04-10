# Ananke.Learning — Architecture

> Empirical memory, episode tracking, reinforcement learning,
> exploration strategies, skill packaging, and entity-scoped memory.

## Role

Provides the intelligence layer for agents that learn from experience.
Tracks episodes (sequences of agent actions + outcomes), propagates rewards,
learns from tag/feature correlations, and can package learned behaviors
as portable skill files.

## Dependencies

- `Ananke.Orchestration` (project)

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Learning` | `IEmpiricalMemory`, `InMemoryEmpiricalMemory`, `IPredictionSource`, `TagOverlapPredictionSource`, `EmpiricalTypes`, `EmpiricalMemoryTools` |
| `Ananke.Learning.Episodes` | `IEpisodeStore`, `InMemoryEpisodeStore`, `EpisodeTypes` (Episode, Step, EpisodeOutcome), `IRewardPropagator`, `MonteCarloRewardPropagator` |
| `Ananke.Learning.Exploration` | `IExplorationStrategy`, `EpsilonGreedyExplorationStrategy`, `UcbExplorationStrategy` |
| `Ananke.Learning.Features` | `ITagImportanceTracker`, `TagImportanceTracker`, `TagImportanceMap` |
| `Ananke.Learning.Offline` | `IOfflineLearner`, `OfflineLearner`, `ISimulationSource`, `IConsolidationSummarizer` |
| `Ananke.Learning.Skills` | `ISkillPackager`, `SkillPackager`, `ISkillPackageFormat`, `JsonSkillPackageFormat` |
| `Ananke.Learning.EntityMemory` | `IEntityMemory`, `IEntityMemoryProvider`, `EntityMemoryProvider`, `EntityScopedConversationMemory`, `EntityScopedEmpiricalMemory`, `EntityScopedEpisodeStore`, `EntityScopedKnowledgeStore` |

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `IEmpiricalMemory` | Interface | Store and query empirical patterns — (tags, action, outcome, reward) tuples with similarity search |
| `IEpisodeStore` | Interface | Persist episodes (multi-step action sequences with start/end/reward) |
| `MonteCarloRewardPropagator` | Class | Backpropagates terminal rewards through episode steps using Monte Carlo returns |
| `IExplorationStrategy` | Interface | Exploration vs exploitation (epsilon-greedy, UCB) |
| `ITagImportanceTracker` | Interface | Tracks which features/tags are most predictive of outcomes |
| `IOfflineLearner` | Interface | Batch learning from stored episodes — consolidation, simulation |
| `ISkillPackager` | Interface | Exports learned behavior as portable JSON skill packages |
| `IEntityMemoryProvider` | Interface | Scopes all memory types (conversation, empirical, episodes, knowledge) to a per-entity partition |

## Learning Loop

```
1. Agent acts → Episode.AddStep(tags, action, observation)
2. Episode completes → Episode.Complete(outcome, reward)
3. MonteCarloRewardPropagator backpropagates reward
4. EmpiricalMemory stores (tags → action → reward) patterns
5. Next decision: IExplorationStrategy + IPredictionSource → pick action
6. Offline: OfflineLearner consolidates + SkillPackager exports
```
