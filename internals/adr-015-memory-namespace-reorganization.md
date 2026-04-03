# ADR-015 — Memory & Knowledge Namespace Reorganization

| Field          | Value                                                                                   |
|----------------|-----------------------------------------------------------------------------------------|
| **Status**     | Proposed                                                                                |
| **Date**       | 2025-07-28                                                                              |
| **Authors**    | —                                                                                       |
| **Deciders**   | Ananke maintainers                                                                      |
| **Tags**       | namespaces, project-structure, breaking-change, memory, knowledge, learning, packaging   |
| **Relates to** | ADR-007 (empirical memory), ADR-008 (affective signals), ADR-014 (skill learning), `Ananke.Orchestration`, `Ananke.Abstractions` |

---

## Context

Before implementing ADR-014 (Episodes, Credit Assignment, Exploration, Skill
Packaging), the namespace and project layout needs restructuring. ADR-014 will
add ~15 new types to the memory/learning surface. Adding them to the existing
layout would compound several problems that already exist. Reorganizing now
avoids moving types twice and gives the new types clean homes from the start.

**Breaking compatibility is explicitly acceptable** for this change — the
library is pre-1.0 and consumers are expected to update `using` statements.

### Current layout

```
Ananke.Abstractions/                        (0 external deps)
  Memory/
    IConversationMemory.cs                  namespace: Ananke.Orchestration.Memory  ← !!
  Tracing/
    IWorkflowTracer.cs                      namespace: Ananke.Abstractions.Tracing
  Agents/
    AgentMessage.cs, ContentPart.cs, ...    namespace: Ananke.Orchestration.Agents
  Distributed/                              namespace: Ananke.Abstractions.Distributed
  Channels/                                 namespace: Ananke.Abstractions.Channels
  Config/                                   namespace: Ananke.Abstractions.Config

Ananke.Orchestration/                       (depends on Abstractions)
  Memory/       (12 files)                  namespace: Ananke.Orchestration.Memory
    IEmpiricalMemory, EmpiricalTypes, InMemoryEmpiricalMemory,
    InMemoryConversationMemory, IOfflineLearner, InMemoryOfflineLearner,
    ISimulationSource, IPredictionSource, TagOverlapPredictionSource,
    IConsolidationSummarizer, TemplateConsolidationSummarizer,
    EmpiricalMemoryTools
  Knowledge/    (24 files)                  namespace: Ananke.Orchestration.Knowledge
    IKnowledgeStore, KnowledgeTypes, IEmbeddingModel,
    InMemoryKnowledgeStore, InMemoryEmbedder, KnowledgeBase,
    IKnowledgeCatalog, InMemoryKnowledgeCatalog, CatalogTypes,
    CatalogAwareKnowledgeStore, CatalogKeywordExtractor,
    DocumentProcessor, DocumentSummarizer, IDocumentExtractor,
    IDocumentChunker, SlidingWindowChunker, LinkedKnowledgeStore,
    InMemoryDocumentLinkGraph, DocumentLinkExtractor, DocumentLinkTypes,
    KnowledgeLinkingExtensions, TimeDecay, KnowledgeTools,
    KnowledgeSearchTool, KnowledgeCatalogTools, ProcessingResult
  Checkpointing/ (4 files)                 namespace: Ananke.Orchestration.Checkpointing
    ICheckpointStore, InMemoryCheckpointStore, FileCheckpointStore, Checkpoint
```

### Identified issues

| # | Issue | Impact |
|---|-------|--------|
| 1 | **Abstractions namespace inconsistency** — Some files in `Ananke.Abstractions` use `Ananke.Orchestration.*` namespaces (Memory/IConversationMemory, Agents/*), while others use `Ananke.Abstractions.*` (Tracing, Distributed, Channels, Config). | Consumers cannot predict the namespace from the assembly name. Two competing conventions coexist in the same project. |
| 2 | **IEmbeddingModel placement** — Lives in `Ananke.Orchestration.Knowledge` but is used by Memory types (`InMemoryEmpiricalMemory`, `QdrantEmpiricalMemory`). | Conceptual cross-dependency: Memory depends on a Knowledge type for a shared infrastructure concern. |
| 3 | **Knowledge folder overloaded** — 24+ files with 6 distinct concerns: core store, catalog/discovery, document processing, document linking, search tools, embeddings. | Hard to navigate. New contributors cannot find the right file. |
| 4 | **ADR-014 will grow Memory to ~27 types** — Episodes, credit assignment, exploration strategies, feature importance, policy, skill packaging. | A single `Memory/` folder and namespace becomes a grab-bag of loosely-related types. |
| 5 | **Naming inconsistency** — `ICheckpointStore`, `IKnowledgeStore` but `IConversationMemory`, `IEmpiricalMemory`. Four persistence layers, two naming conventions. | Minor but adds cognitive load. Not worth changing alone but worth normalizing during a reorganization. |
| 6 | **IConsolidationSummarizer bridges two concerns** — Lives in Memory namespace, imports Knowledge types. It's inherently cross-cutting between empirical memory and the knowledge layer. | Current placement is acceptable but a reorganization can make the bridge role explicit. |

---

## Analysis

### Option A: Reorganize within existing projects (namespace-only)

Restructure folders and namespaces inside `Ananke.Abstractions` and
`Ananke.Orchestration` without adding new projects.

**Layout after reorganization:**

```
Ananke.Abstractions/
  Agents/       → namespace: Ananke.Abstractions.Agents
  Memory/       → namespace: Ananke.Abstractions.Memory        (IConversationMemory)
  Tracing/      → namespace: Ananke.Abstractions.Tracing       (unchanged)
  Distributed/  → namespace: Ananke.Abstractions.Distributed   (unchanged)
  Channels/     → namespace: Ananke.Abstractions.Channels      (unchanged)
  Config/       → namespace: Ananke.Abstractions.Config         (unchanged)

Ananke.Orchestration/
  Memory/                → namespace: Ananke.Orchestration.Memory
  Learning/              → namespace: Ananke.Orchestration.Learning  (NEW — ADR-014 types)
  Knowledge/             → namespace: Ananke.Orchestration.Knowledge
  Knowledge/Documents/   → namespace: Ananke.Orchestration.Knowledge.Documents
  Knowledge/Catalog/     → namespace: Ananke.Orchestration.Knowledge.Catalog
  Knowledge/Linking/     → namespace: Ananke.Orchestration.Knowledge.Linking
  Embeddings/            → namespace: Ananke.Orchestration.Embeddings  (IEmbeddingModel)
  Checkpointing/         → namespace: Ananke.Orchestration.Checkpointing (unchanged)
```

**Pros:**
- No new NuGet packages — simpler dependency graph, fewer assemblies
- Smallest blast radius for consumers — only `using` statements change
- All orchestration logic stays in one compilable unit

**Cons:**
- `Ananke.Orchestration` continues growing (now ~97 files, will become ~115+)
- Consumers who only want learning get all of Orchestration's transitive deps (Polly, etc.)
- Harder to version learning independently if it stabilizes on a different cadence

### Option B: Extract `Ananke.Learning` as a new package

Move all empirical memory, learning, and skill packaging types into a new
`Ananke.Learning` project. The existing memory types (IConversationMemory and
its implementations) stay in their current locations since conversational
memory is a general-purpose concern, not a learning concern.

**Dependency graph:**

```
Ananke.Abstractions  (0 deps)
        │
        ├─── Ananke.Orchestration  (Abstractions + Polly, MS.DI, MS.Logging)
        │         │
        │         ├─── Ananke.Learning  (Orchestration — needs IEmbeddingModel, Knowledge types)
        │         │         │
        │         │         ├─── Ananke.Qdrant  (already depends on Orchestration; adds Learning)
        │         │         └─── demos/Connect4Demo
        │         │
        │         ├─── Ananke.Skills (Orchestration)
        │         ├─── Ananke.Orchestration.OpenAI/Anthropic/Google
        │         └─── demos that don't use learning
        │
        └─── Ananke.Redis (Abstractions + Orchestration)
```

**What moves to `Ananke.Learning`:**

| Current location | Type | Why it's a learning concern |
|---|---|---|
| `Orchestration/Memory/IEmpiricalMemory.cs` | `IEmpiricalMemory` | Core learning contract |
| `Orchestration/Memory/EmpiricalTypes.cs` | `EmpiricalEntry`, `SemanticDescription`, etc. | Learning data model |
| `Orchestration/Memory/InMemoryEmpiricalMemory.cs` | `InMemoryEmpiricalMemory` | In-memory impl |
| `Orchestration/Memory/IOfflineLearner.cs` | `IOfflineLearner`, `OfflineLearnerOptions` | Learning infrastructure |
| `Orchestration/Memory/InMemoryOfflineLearner.cs` | `InMemoryOfflineLearner` | Learning impl |
| `Orchestration/Memory/ISimulationSource.cs` | `ISimulationSource`, `SimulationOutcome` | Learning infrastructure |
| `Orchestration/Memory/IPredictionSource.cs` | `IPredictionSource` | Learning infrastructure |
| `Orchestration/Memory/TagOverlapPredictionSource.cs` | `TagOverlapPredictionSource` | Learning impl |
| `Orchestration/Memory/IConsolidationSummarizer.cs` | `IConsolidationSummarizer` | Memory→Knowledge bridge |
| `Orchestration/Memory/TemplateConsolidationSummarizer.cs` | `TemplateConsolidationSummarizer` | Consolidation impl |
| `Orchestration/Memory/EmpiricalMemoryTools.cs` | `EmpiricalMemoryTools` | Agent tool wrappers |
| *(new — ADR-014)* | Episodes, credit assignment, exploration, etc. | All new learning types |

**What stays in `Ananke.Orchestration`:**

| Location | Type | Why it stays |
|---|---|---|
| `Memory/InMemoryConversationMemory.cs` | `InMemoryConversationMemory` | General-purpose conversation, not learning |
| `Knowledge/*` | All knowledge types | Semantic storage is a general concern |
| `Checkpointing/*` | All checkpoint types | Workflow persistence, not learning |
| `Agents/*`, `Jobs/*`, etc. | All orchestration types | Core workflow engine |

**Pros:**
- Clean separation of concerns — learning is a distinct domain with its own cadence
- Consumers who don't need learning don't pay for it (smaller dependency closure)
- ADR-014's ~15 new types have a natural home without bloating Orchestration
- Independent versioning — learning can iterate faster without bumping Orchestration
- Simpler reasoning about what depends on what
- `Ananke.Learning` is a natural package name that communicates intent

**Cons:**
- New NuGet package to publish and maintain
- `Ananke.Learning` depends on `Ananke.Orchestration` (for `IEmbeddingModel`, knowledge types)
- Qdrant provider needs a reference to both Orchestration and Learning
- Slightly more complex CI/CD (one more package in the publish matrix)

### Option C: Extract `Ananke.Learning` + move `IEmbeddingModel` to Abstractions

Same as Option B but also moves `IEmbeddingModel` down to `Ananke.Abstractions`
so that `Ananke.Learning` can depend on Abstractions alone.

**Rejected** — `IEmbeddingModel` returns `ReadOnlyMemory<float>` and has batch
semantics tightly coupled to how `IKnowledgeStore` and `IEmpiricalMemory` use
it. Promoting it to Abstractions adds embedding concepts to a package that
currently has no knowledge of vectors or ML. It's better to keep it in
Orchestration and have Learning depend on Orchestration.

### Option D: Extract `Ananke.Learning` + `Ananke.Knowledge` as separate packages

Split both learning AND knowledge out of Orchestration.

**Rejected** — Knowledge types are deeply intertwined with the orchestration
workflow engine (KnowledgeBase as a facade, KnowledgeTools for agent integration,
DocumentProcessor pipelines). Extracting both creates excessive inter-package
coupling. One extraction (Learning) is the right granularity.

---

## Decision

**Adopt Option B: Extract `Ananke.Learning` as a new package, with namespace
normalization in Abstractions and Knowledge folder restructuring in Orchestration.**

This is a three-part decision:

### Part 1: Normalize Abstractions namespaces

All types in `Ananke.Abstractions` get `Ananke.Abstractions.*` namespaces,
eliminating the current split between `Ananke.Orchestration.*` and
`Ananke.Abstractions.*` within the same assembly.

| File | Old namespace | New namespace |
|------|--------------|---------------|
| `Abstractions/Memory/IConversationMemory.cs` | `Ananke.Orchestration.Memory` | `Ananke.Abstractions.Memory` |
| `Abstractions/Agents/AgentMessage.cs` | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| `Abstractions/Agents/ContentPart.cs` | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| `Abstractions/Agents/AgentToolCall.cs` | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| `Abstractions/Agents/AgentToolResult.cs` | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| Other `Abstractions/Agents/*` files | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| `Abstractions/IBaseContext.cs` | `Ananke.Orchestration` | `Ananke.Abstractions` |
| `Abstractions/IInterruptSink.cs` | `Ananke.Orchestration` | `Ananke.Abstractions` |

Files already using `Ananke.Abstractions.*` namespaces (Tracing, Distributed,
Channels, Config, Extensions) remain unchanged.

**Consumer impact:** Every file that imports `Ananke.Orchestration.Agents` or
`Ananke.Orchestration.Memory` for types defined in Abstractions needs to update
the `using` statement. This affects Orchestration heavily (most files import
`AgentMessage`) and all providers.

### Part 2: Create `Ananke.Learning` project

A new project `Ananke.Learning/Ananke.Learning.csproj` containing all empirical
memory and learning infrastructure.

**Namespace:** `Ananke.Learning` (root), with sub-namespaces as the surface grows:

| Namespace | Contents |
|-----------|----------|
| `Ananke.Learning` | `IEmpiricalMemory`, `EmpiricalEntry`, `EmpiricalMatch`, `SemanticDescription`, `Reinforcement`, `RecallOptions`, `EmpiricalKind`, `InMemoryEmpiricalMemory` |
| `Ananke.Learning` | `IOfflineLearner`, `OfflineLearnerOptions`, `OfflineLearningResult`, `InMemoryOfflineLearner` |
| `Ananke.Learning` | `ISimulationSource`, `SimulationOutcome`, `IPredictionSource`, `TagOverlapPredictionSource` |
| `Ananke.Learning` | `IConsolidationSummarizer`, `TemplateConsolidationSummarizer` |
| `Ananke.Learning` | `EmpiricalMemoryTools` |
| `Ananke.Learning.Episodes` | *(ADR-014)* `Episode`, `EpisodeStep`, `IEpisodeStore`, `InMemoryEpisodeStore` |
| `Ananke.Learning.CreditAssignment` | *(ADR-014)* `IRewardPropagator`, `MonteCarloRewardPropagator` |
| `Ananke.Learning.Exploration` | *(ADR-014)* `IExplorationStrategy`, `ActionCandidate`, `UcbExplorationStrategy`, `EpsilonGreedyExplorationStrategy` |
| `Ananke.Learning.Features` | *(ADR-014)* `TagImportanceMap`, related types |
| `Ananke.Learning.Packaging` | *(ADR-014)* `LearnedSkillPackage`, `TrainingManifest`, `ISkillPackager`, `ISkillPackageFormat`, etc. |

**Note:** All existing types start in the root `Ananke.Learning` namespace for
simplicity. Sub-namespaces are introduced only for ADR-014 additions, which form
distinct functional groups. This avoids excessive namespace fragmentation for the
current ~12 types while providing clean homes for the ~15 new ones.

**Project references:**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <PackageId>Ananke.Learning</PackageId>
    <Description>Empirical memory and skill learning for Ananke — pattern recognition, reinforcement learning, episodes, credit assignment, exploration strategies, and portable skill packaging.</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Ananke.Orchestration\Ananke.Orchestration.csproj" />
  </ItemGroup>
</Project>
```

**Consumer updates:**

| Consumer | Change |
|----------|--------|
| `Ananke.Qdrant` | Add `ProjectReference` to `Ananke.Learning`; update `using Ananke.Orchestration.Memory` → `using Ananke.Learning` in `QdrantEmpiricalMemory.cs` |
| `Ananke.Redis` | No change (RedisConversationMemory uses `IConversationMemory`, not empirical) |
| `Ananke.Skills` | No change (no direct dependency on empirical memory) |
| `Connect4Demo` | Add `ProjectReference` to `Ananke.Learning`; update `using` statements |
| `LongTermMemoryDemo` | Review — may need Learning reference if it uses empirical memory |

### Part 3: Restructure Knowledge folder in Orchestration

Split the 24-file `Knowledge/` folder into sub-folders reflecting the distinct
concerns. Namespaces follow the folder structure:

| Sub-folder | Namespace | Files |
|------------|-----------|-------|
| `Knowledge/` | `Ananke.Orchestration.Knowledge` | `IKnowledgeStore`, `KnowledgeTypes`, `InMemoryKnowledgeStore`, `KnowledgeBase`, `TimeDecay`, `ProcessingResult` |
| `Knowledge/Embeddings/` | `Ananke.Orchestration.Knowledge.Embeddings` | `IEmbeddingModel`, `InMemoryEmbedder` |
| `Knowledge/Documents/` | `Ananke.Orchestration.Knowledge.Documents` | `DocumentProcessor`, `DocumentSummarizer`, `IDocumentExtractor`, `IDocumentChunker`, `SlidingWindowChunker` |
| `Knowledge/Catalog/` | `Ananke.Orchestration.Knowledge.Catalog` | `IKnowledgeCatalog`, `InMemoryKnowledgeCatalog`, `CatalogTypes`, `CatalogAwareKnowledgeStore`, `CatalogKeywordExtractor`, `KnowledgeCatalogTools` |
| `Knowledge/Linking/` | `Ananke.Orchestration.Knowledge.Linking` | `LinkedKnowledgeStore`, `InMemoryDocumentLinkGraph`, `DocumentLinkExtractor`, `DocumentLinkTypes`, `KnowledgeLinkingExtensions` |
| `Knowledge/Tools/` | `Ananke.Orchestration.Knowledge.Tools` | `KnowledgeTools`, `KnowledgeSearchTool` |

**IEmbeddingModel** moves to `Knowledge/Embeddings/` with a new namespace
`Ananke.Orchestration.Knowledge.Embeddings`. This makes its role explicit
(embedding is a knowledge infrastructure concern) while keeping it in
Orchestration where both Knowledge and Learning (via project reference) can
use it.

---

## Proposed Changes

### Abstractions namespace normalization

```csharp
// Before (IConversationMemory.cs)
namespace Ananke.Orchestration.Memory;

// After
namespace Ananke.Abstractions.Memory;
```

```csharp
// Before (AgentMessage.cs, ContentPart.cs, etc.)
namespace Ananke.Orchestration.Agents;

// After
namespace Ananke.Abstractions.Agents;
```

```csharp
// Before (IBaseContext.cs, IInterruptSink.cs)
namespace Ananke.Orchestration;

// After
namespace Ananke.Abstractions;
```

### New Ananke.Learning project

```csharp
// IEmpiricalMemory.cs — moved from Ananke.Orchestration/Memory/
namespace Ananke.Learning;

public interface IEmpiricalMemory
{
    // ... unchanged contract ...
}
```

```csharp
// EmpiricalTypes.cs — moved from Ananke.Orchestration/Memory/
namespace Ananke.Learning;

public sealed record EmpiricalEntry { /* ... */ }
public sealed record SemanticDescription { /* ... */ }
// etc.
```

### Knowledge sub-namespace example

```csharp
// IEmbeddingModel.cs — moved to Knowledge/Embeddings/
namespace Ananke.Orchestration.Knowledge.Embeddings;

public interface IEmbeddingModel
{
    Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);
}
```

### Consumer using-statement updates

```csharp
// Before (any file using AgentMessage from Abstractions)
using Ananke.Orchestration.Agents;

// After
using Ananke.Abstractions.Agents;
```

```csharp
// Before (QdrantEmpiricalMemory.cs)
using Ananke.Orchestration.Memory;

// After
using Ananke.Learning;
```

---

## Consequences

### Positive

- **Consistent namespace convention** — `Ananke.Abstractions.*` for all types
  in the Abstractions assembly; no more guessing which convention a file uses.
- **Clean home for learning types** — ADR-014's ~15 new types land in a
  dedicated package with clear sub-namespaces instead of bloating
  `Ananke.Orchestration.Memory`.
- **Independent versioning** — Learning can iterate rapidly (experiments,
  algorithm changes) without forcing Orchestration version bumps.
- **Smaller consumer footprint** — Applications that don't need learning skip
  the `Ananke.Learning` package entirely.
- **Navigable Knowledge folder** — Sub-folders group 24 files into 5-6
  cohesive units; new contributors find the right file faster.
- **IEmbeddingModel placement is explicit** — In `Knowledge.Embeddings`,
  clearly infrastructure that both Knowledge and Learning consume.
- **Pre-1.0 timing** — Breaking changes now are cheap; after 1.0 they require
  major version bumps and migration guides.

### Negative

- **Breaking change for all consumers** — Every `using` statement for
  Abstractions types (Agents, Memory) changes. Orchestration itself has
  ~60+ files to update.
- **New NuGet package** — One more package in the publish matrix, one more
  version to track. Mitigated by the existing CI/CD pipeline which already
  handles multiple packages.
- **Qdrant provider gains a dependency** — `Ananke.Qdrant` needs references
  to both `Ananke.Orchestration` and `Ananke.Learning`. Transitive dependency
  through Orchestration doesn't help since Learning depends on Orchestration,
  not the other way around.
- **Knowledge sub-namespaces are verbose** — `Ananke.Orchestration.Knowledge.Embeddings`
  is longer than `Ananke.Orchestration.Knowledge`. Mitigated by the fact that
  most consumers only import the root `Knowledge` namespace; sub-namespaces
  are for specialists (document processing, linking).

### Neutral

- **IConsolidationSummarizer** moves to `Ananke.Learning` since it bridges
  empirical memory to knowledge. It imports `Ananke.Orchestration.Knowledge`
  types, which is natural since Learning depends on Orchestration.
- **Naming inconsistency** (`*Memory` vs `*Store`) — This ADR does **not**
  rename `IConversationMemory` or `IEmpiricalMemory` to `*Store`. The current
  names are established and the inconsistency is cosmetic. Renaming can be
  a future consideration if the naming creates actual confusion.
- **`InMemoryConversationMemory`** stays in `Ananke.Orchestration.Memory` —
  it implements an Abstractions interface but lives in Orchestration as the
  default in-memory implementation, which is the existing pattern.

---

## Related ADRs

| ADR | Relationship |
|-----|-------------|
| ADR-007 | Established empirical memory — types moving to `Ananke.Learning` |
| ADR-008 | Added affective signals to empirical types — moves with them |
| ADR-011 | Skill catalog — future bridge when learned skills register in catalog |
| ADR-014 | Skill learning types — will be created directly in `Ananke.Learning` sub-namespaces |
