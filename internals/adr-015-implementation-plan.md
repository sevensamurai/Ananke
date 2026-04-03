# ADR-015: Implementation Plan — Memory & Knowledge Namespace Reorganization

| Field          | Value                                                              |
|----------------|---------------------------------------------------------------------|
| **Status**     | Proposed                                                            |
| **Date**       | 2025-07-28                                                          |
| **Relates to** | ADR-015 (memory & knowledge namespace reorganization)               |

---

## Phase Overview

```
Phase 1 ─ Normalize Abstractions Namespaces    ┐
                                               │  Phase 1 MUST complete before Phase 2
Phase 2 ─ Create Ananke.Learning Project       │  (Learning imports Abstractions types).
                                               │
Phase 3 ─ Restructure Knowledge Folder         │  Phase 3 is independent of 1 and 2
                                               │  but best done after Phase 2 so that
Phase 4 ─ Update Providers & Demos             │  Learning project references resolve.
                                               │
Phase 5 ─ CI/CD & NuGet Setup                  ┘  Phase 4 after 1+2+3. Phase 5 last.
```

All phases are **breaking changes** — this is intentional and acceptable
pre-1.0. Each phase should be a single commit (or squashed PR) for clean
`git bisect` and easy revert.

---

## Phase 1 — Normalize Abstractions Namespaces

**Goal:** Every type in `Ananke.Abstractions` uses `Ananke.Abstractions.*`
namespaces. Eliminate the `Ananke.Orchestration.*` namespaces from this assembly.

### File-by-file changes

| File | Old namespace | New namespace |
|------|--------------|---------------|
| `Ananke.Abstractions/Memory/IConversationMemory.cs` | `Ananke.Orchestration.Memory` | `Ananke.Abstractions.Memory` |
| `Ananke.Abstractions/Agents/AgentMessage.cs` | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| `Ananke.Abstractions/Agents/ContentPart.cs` | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| `Ananke.Abstractions/Agents/AgentToolCall.cs` | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| `Ananke.Abstractions/Agents/AgentToolResult.cs` | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| `Ananke.Abstractions/Agents/ToolDefinition.cs` | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| `Ananke.Abstractions/Agents/ToolParameterDefinition.cs` | `Ananke.Orchestration.Agents` | `Ananke.Abstractions.Agents` |
| `Ananke.Abstractions/IBaseContext.cs` | `Ananke.Orchestration` | `Ananke.Abstractions` |
| `Ananke.Abstractions/IInterruptSink.cs` | `Ananke.Orchestration` | `Ananke.Abstractions` |

**Using-statement impact in Abstractions itself:**
- `IConversationMemory.cs` imports `Ananke.Orchestration.Agents` for `AgentMessage` — change to `Ananke.Abstractions.Agents`
- Files in `Agents/` that cross-reference each other — namespace is the same, no import changes needed

### Cascade: Update all consumers of Abstractions types

Every file in every project that has `using Ananke.Orchestration.Agents` or
`using Ananke.Orchestration.Memory` (for the Abstractions-defined types) must
update. The systematic approach:

**Step 1:** Change namespaces in all Abstractions source files.

**Step 2:** Build `Ananke.Abstractions` — should succeed (self-contained).

**Step 3:** In `Ananke.Orchestration`, find-and-replace:
- Orchestration files that use `AgentMessage`, `ContentPart`, `ToolDefinition`, etc. currently don't need a `using` because the types were in `Ananke.Orchestration.Agents` which matches the Orchestration root namespace. After the change, these files need an explicit `using Ananke.Abstractions.Agents;`.
- Files that use `IConversationMemory` need `using Ananke.Abstractions.Memory;`.

**Step 4:** Update `Ananke.Orchestration.Agents` namespace: files in
`Ananke.Orchestration/Agents/` that define Orchestration-level agent types
(e.g., `AgentJob`, `ChatCompletionClient`) keep `Ananke.Orchestration.Agents`
but now need `using Ananke.Abstractions.Agents;` for the shared types.

**Step 5:** Update all provider projects similarly:
- `Ananke.Qdrant` — add `using Ananke.Abstractions.Agents;` where needed
- `Ananke.Redis` — update `using Ananke.Orchestration.Memory` → `using Ananke.Abstractions.Memory` in `RedisConversationMemory.cs`
- `Ananke.Orchestration.OpenAI/Anthropic/Google` — update agent type imports
- All demo projects

**Step 6:** Build entire solution — verify zero errors.

### Risk mitigation

This is a high-touch refactoring (potentially 60+ files). Use IDE refactoring
tools (rename namespace) where possible. The compiler is the safety net — a
missed `using` statement is a build error, not a runtime bug.

---

## Phase 2 — Create Ananke.Learning Project

**Goal:** Extract all empirical memory and learning types into a new
`Ananke.Learning` project with namespace `Ananke.Learning`.

### New project setup

Create `Ananke.Learning/Ananke.Learning.csproj`:

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

### File moves

| Source | Destination | New namespace |
|--------|-------------|---------------|
| `Ananke.Orchestration/Memory/IEmpiricalMemory.cs` | `Ananke.Learning/IEmpiricalMemory.cs` | `Ananke.Learning` |
| `Ananke.Orchestration/Memory/EmpiricalTypes.cs` | `Ananke.Learning/EmpiricalTypes.cs` | `Ananke.Learning` |
| `Ananke.Orchestration/Memory/InMemoryEmpiricalMemory.cs` | `Ananke.Learning/InMemoryEmpiricalMemory.cs` | `Ananke.Learning` |
| `Ananke.Orchestration/Memory/IOfflineLearner.cs` | `Ananke.Learning/IOfflineLearner.cs` | `Ananke.Learning` |
| `Ananke.Orchestration/Memory/InMemoryOfflineLearner.cs` | `Ananke.Learning/InMemoryOfflineLearner.cs` | `Ananke.Learning` |
| `Ananke.Orchestration/Memory/ISimulationSource.cs` | `Ananke.Learning/ISimulationSource.cs` | `Ananke.Learning` |
| `Ananke.Orchestration/Memory/IPredictionSource.cs` | `Ananke.Learning/IPredictionSource.cs` | `Ananke.Learning` |
| `Ananke.Orchestration/Memory/TagOverlapPredictionSource.cs` | `Ananke.Learning/TagOverlapPredictionSource.cs` | `Ananke.Learning` |
| `Ananke.Orchestration/Memory/IConsolidationSummarizer.cs` | `Ananke.Learning/IConsolidationSummarizer.cs` | `Ananke.Learning` |
| `Ananke.Orchestration/Memory/TemplateConsolidationSummarizer.cs` | `Ananke.Learning/TemplateConsolidationSummarizer.cs` | `Ananke.Learning` |
| `Ananke.Orchestration/Memory/EmpiricalMemoryTools.cs` | `Ananke.Learning/EmpiricalMemoryTools.cs` | `Ananke.Learning` |

**Files that stay in `Ananke.Orchestration/Memory/`:**

| File | Reason |
|------|--------|
| `InMemoryConversationMemory.cs` | Implements `IConversationMemory` (Abstractions) — general-purpose, not learning |

After the move, `Ananke.Orchestration/Memory/` contains only
`InMemoryConversationMemory.cs`. The folder and namespace
`Ananke.Orchestration.Memory` still exists but is minimal.

### Namespace updates in moved files

Each moved file changes its namespace declaration:

```csharp
// Before
namespace Ananke.Orchestration.Memory;

// After
namespace Ananke.Learning;
```

Internal cross-references between moved files need no `using` changes (they're
all in `Ananke.Learning` now). References to Knowledge types need:

```csharp
using Ananke.Orchestration.Knowledge;              // IKnowledgeStore, KnowledgeDocument
using Ananke.Orchestration.Knowledge.Embeddings;    // IEmbeddingModel (after Phase 3)
```

### InMemoryConversationMemory update

This file stays in Orchestration but imported `Ananke.Orchestration.Memory`
types (now `Ananke.Abstractions.Memory` after Phase 1). Verify it has:

```csharp
using Ananke.Abstractions.Memory;    // IConversationMemory
using Ananke.Abstractions.Agents;    // AgentMessage
```

### Add to solution

Add the new project to the solution file and verify it builds.

---

## Phase 3 — Restructure Knowledge Folder

**Goal:** Split the 24-file `Knowledge/` folder into sub-folders with matching
sub-namespaces. No files leave the `Ananke.Orchestration` project.

### Folder structure

```
Ananke.Orchestration/Knowledge/
  ├── IKnowledgeStore.cs                    Ananke.Orchestration.Knowledge
  ├── KnowledgeTypes.cs                     Ananke.Orchestration.Knowledge
  ├── InMemoryKnowledgeStore.cs             Ananke.Orchestration.Knowledge
  ├── KnowledgeBase.cs                      Ananke.Orchestration.Knowledge
  ├── TimeDecay.cs                          Ananke.Orchestration.Knowledge
  ├── ProcessingResult.cs                   Ananke.Orchestration.Knowledge
  │
  ├── Embeddings/
  │     ├── IEmbeddingModel.cs              Ananke.Orchestration.Knowledge.Embeddings
  │     └── InMemoryEmbedder.cs             Ananke.Orchestration.Knowledge.Embeddings
  │
  ├── Documents/
  │     ├── DocumentProcessor.cs            Ananke.Orchestration.Knowledge.Documents
  │     ├── DocumentSummarizer.cs           Ananke.Orchestration.Knowledge.Documents
  │     ├── IDocumentExtractor.cs           Ananke.Orchestration.Knowledge.Documents
  │     ├── IDocumentChunker.cs             Ananke.Orchestration.Knowledge.Documents
  │     └── SlidingWindowChunker.cs         Ananke.Orchestration.Knowledge.Documents
  │
  ├── Catalog/
  │     ├── IKnowledgeCatalog.cs            Ananke.Orchestration.Knowledge.Catalog
  │     ├── InMemoryKnowledgeCatalog.cs     Ananke.Orchestration.Knowledge.Catalog
  │     ├── CatalogTypes.cs                 Ananke.Orchestration.Knowledge.Catalog
  │     ├── CatalogAwareKnowledgeStore.cs   Ananke.Orchestration.Knowledge.Catalog
  │     ├── CatalogKeywordExtractor.cs      Ananke.Orchestration.Knowledge.Catalog
  │     └── KnowledgeCatalogTools.cs        Ananke.Orchestration.Knowledge.Catalog
  │
  ├── Linking/
  │     ├── LinkedKnowledgeStore.cs         Ananke.Orchestration.Knowledge.Linking
  │     ├── InMemoryDocumentLinkGraph.cs    Ananke.Orchestration.Knowledge.Linking
  │     ├── DocumentLinkExtractor.cs        Ananke.Orchestration.Knowledge.Linking
  │     ├── DocumentLinkTypes.cs            Ananke.Orchestration.Knowledge.Linking
  │     └── KnowledgeLinkingExtensions.cs   Ananke.Orchestration.Knowledge.Linking
  │
  └── Tools/
        ├── KnowledgeTools.cs               Ananke.Orchestration.Knowledge.Tools
        └── KnowledgeSearchTool.cs          Ananke.Orchestration.Knowledge.Tools
```

### File-by-file moves

**To `Knowledge/Embeddings/` — namespace `Ananke.Orchestration.Knowledge.Embeddings`:**

| File | Notes |
|------|-------|
| `IEmbeddingModel.cs` | Used by InMemoryEmpiricalMemory (now in Ananke.Learning), QdrantEmpiricalMemory, InMemoryKnowledgeStore, OpenAIEmbeddingModel |
| `InMemoryEmbedder.cs` | Test/demo implementation of IEmbeddingModel |

**To `Knowledge/Documents/` — namespace `Ananke.Orchestration.Knowledge.Documents`:**

| File | Notes |
|------|-------|
| `DocumentProcessor.cs` | Orchestrates extraction → chunking → embedding → storage |
| `DocumentSummarizer.cs` | LLM-based document summarization |
| `IDocumentExtractor.cs` | Extracts text from file formats |
| `IDocumentChunker.cs` | Splits text into chunks |
| `SlidingWindowChunker.cs` | Sliding window implementation |

**To `Knowledge/Catalog/` — namespace `Ananke.Orchestration.Knowledge.Catalog`:**

| File | Notes |
|------|-------|
| `IKnowledgeCatalog.cs` | Catalog interface |
| `InMemoryKnowledgeCatalog.cs` | In-memory implementation |
| `CatalogTypes.cs` | Catalog data types |
| `CatalogAwareKnowledgeStore.cs` | Store that auto-registers in catalog |
| `CatalogKeywordExtractor.cs` | Extracts keywords for catalog metadata |
| `KnowledgeCatalogTools.cs` | Agent tool wrappers for catalog |

**To `Knowledge/Linking/` — namespace `Ananke.Orchestration.Knowledge.Linking`:**

| File | Notes |
|------|-------|
| `LinkedKnowledgeStore.cs` | Knowledge store with link tracking |
| `InMemoryDocumentLinkGraph.cs` | In-memory link graph |
| `DocumentLinkExtractor.cs` | Extracts links between documents |
| `DocumentLinkTypes.cs` | Link data types |
| `KnowledgeLinkingExtensions.cs` | Extension methods for linking |

**To `Knowledge/Tools/` — namespace `Ananke.Orchestration.Knowledge.Tools`:**

| File | Notes |
|------|-------|
| `KnowledgeTools.cs` | Agent tool registrations |
| `KnowledgeSearchTool.cs` | Search tool implementation |

**Stay in `Knowledge/` root — namespace `Ananke.Orchestration.Knowledge`:**

| File | Notes |
|------|-------|
| `IKnowledgeStore.cs` | Core contract |
| `KnowledgeTypes.cs` | Core data types (KnowledgeDocument, KnowledgeChunk, etc.) |
| `InMemoryKnowledgeStore.cs` | Core in-memory implementation |
| `KnowledgeBase.cs` | Facade combining store + search |
| `TimeDecay.cs` | Utility for time-based relevance decay |
| `ProcessingResult.cs` | Result type for document processing |

### Cascade: Update internal cross-references

Files that move to sub-namespaces need `using` statements for the root
namespace types they reference. Example:

```csharp
// DocumentProcessor.cs (moved to Documents sub-namespace)
using Ananke.Orchestration.Knowledge;               // KnowledgeDocument, IKnowledgeStore
using Ananke.Orchestration.Knowledge.Embeddings;     // IEmbeddingModel
```

Files in the root namespace that use sub-namespace types need the reverse:

```csharp
// KnowledgeBase.cs (stays in root)
using Ananke.Orchestration.Knowledge.Embeddings;     // if it uses IEmbeddingModel
```

### External consumers

| Consumer | Change |
|----------|--------|
| `Ananke.Learning` (new) | `using Ananke.Orchestration.Knowledge.Embeddings;` for `IEmbeddingModel` |
| `Ananke.Qdrant` | Add `using Ananke.Orchestration.Knowledge.Embeddings;` (for `IEmbeddingModel`) plus any sub-namespace imports for catalog types |
| `Ananke.Orchestration.OpenAI` | `using Ananke.Orchestration.Knowledge.Embeddings;` (implements `IEmbeddingModel`) |
| `Ananke.Orchestration.Anthropic` | Similar if it has embedding support |
| `Ananke.Orchestration.Google` | Similar if it has embedding support |

---

## Phase 4 — Update Providers and Demos

**Goal:** Update all downstream projects to reference `Ananke.Learning` and
use the new namespaces.

### Ananke.Qdrant

**`Ananke.Qdrant.csproj`:**

```xml
<ItemGroup>
  <ProjectReference Include="..\Ananke.Orchestration\Ananke.Orchestration.csproj" />
  <ProjectReference Include="..\Ananke.Learning\Ananke.Learning.csproj" />
</ItemGroup>
```

**`QdrantEmpiricalMemory.cs`:**
```csharp
// Before
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.Knowledge;

// After
using Ananke.Learning;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
```

**`QdrantKnowledgeStore.cs`:**
```csharp
// Before
using Ananke.Orchestration.Knowledge;

// After
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;  // if it references IEmbeddingModel
```

**`QdrantKnowledgeCatalog.cs`:**
```csharp
// May need
using Ananke.Orchestration.Knowledge.Catalog;
```

### Ananke.Redis

**`RedisConversationMemory.cs`:**
```csharp
// Before
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.Agents;

// After
using Ananke.Abstractions.Memory;
using Ananke.Abstractions.Agents;
```

No `Ananke.Learning` reference needed — Redis doesn't implement empirical memory.

### Ananke.Orchestration.OpenAI (and other LLM providers)

**`OpenAIEmbeddingModel.cs`:**
```csharp
// Before
using Ananke.Orchestration.Knowledge;

// After
using Ananke.Orchestration.Knowledge.Embeddings;
```

### Connect4Demo

**`Connect4Demo.csproj`:**

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Ananke.Orchestration\Ananke.Orchestration.csproj" />
  <ProjectReference Include="..\..\Ananke.Learning\Ananke.Learning.csproj" />
</ItemGroup>
```

**All `.cs` files:**
```csharp
// Before
using Ananke.Orchestration.Memory;

// After
using Ananke.Learning;
```

### LongTermMemoryDemo / BasicAgentDemo

Review each demo's `Program.cs` for empirical memory usage. If they only use
conversation memory and knowledge, they need the Abstractions namespace
updates (Phase 1) but not `Ananke.Learning`.

---

## Phase 5 — CI/CD and NuGet Setup

**Goal:** Ensure the new `Ananke.Learning` package is built, packed, and
published alongside the existing packages.

### Changes to publish workflow

Update `.github/workflows/publish.yml` to include `Ananke.Learning` in the
build and publish steps. The workflow likely already handles multiple projects
or uses a glob pattern — verify and add `Ananke.Learning/Ananke.Learning.csproj`
if needed.

### NuGet metadata

Ensure `Ananke.Learning.csproj` has appropriate metadata in `Directory.Build.props`
or inline:

```xml
<PropertyGroup>
  <Authors>Ananke contributors</Authors>
  <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
  <PackageProjectUrl>https://github.com/sevensamurai/Ananke</PackageProjectUrl>
  <RepositoryUrl>https://github.com/sevensamurai/Ananke</RepositoryUrl>
  <PackageTags>ai;agents;learning;empirical-memory;reinforcement;skills</PackageTags>
</PropertyGroup>
```

(If `Directory.Build.props` already sets these, no duplication needed.)

### Verification

```bash
dotnet build Ananke.sln
dotnet pack Ananke.Learning/Ananke.Learning.csproj --configuration Release
dotnet pack Ananke.Orchestration/Ananke.Orchestration.csproj --configuration Release
dotnet pack Ananke.Qdrant/Ananke.Qdrant.csproj --configuration Release
```

Verify all packages resolve their inter-project references correctly when
packed.

---

## Dependency Graph (After Reorganization)

```
Ananke.Abstractions  (0 deps)
│
│  namespaces: Ananke.Abstractions
│              Ananke.Abstractions.Agents
│              Ananke.Abstractions.Memory
│              Ananke.Abstractions.Distributed
│              Ananke.Abstractions.Channels
│              Ananke.Abstractions.Config
│              Ananke.Abstractions.Tracing
│
├─── Ananke.Orchestration  (Abstractions + Polly, MS.DI, MS.Logging)
│    │
│    │  namespaces: Ananke.Orchestration.Memory           (InMemoryConversationMemory only)
│    │              Ananke.Orchestration.Knowledge
│    │              Ananke.Orchestration.Knowledge.Embeddings
│    │              Ananke.Orchestration.Knowledge.Documents
│    │              Ananke.Orchestration.Knowledge.Catalog
│    │              Ananke.Orchestration.Knowledge.Linking
│    │              Ananke.Orchestration.Knowledge.Tools
│    │              Ananke.Orchestration.Checkpointing
│    │              Ananke.Orchestration.Agents
│    │              Ananke.Orchestration.Jobs
│    │              ... (routing, tools, patterns, streaming, etc.)
│    │
│    ├─── Ananke.Learning  (Orchestration)
│    │    │
│    │    │  namespaces: Ananke.Learning
│    │    │              Ananke.Learning.Episodes            (ADR-014)
│    │    │              Ananke.Learning.CreditAssignment    (ADR-014)
│    │    │              Ananke.Learning.Exploration         (ADR-014)
│    │    │              Ananke.Learning.Features            (ADR-014)
│    │    │              Ananke.Learning.Packaging           (ADR-014)
│    │    │
│    │    ├─── Ananke.Qdrant  (also refs Orchestration)
│    │    └─── demos/Connect4Demo
│    │
│    ├─── Ananke.Skills  (Orchestration)
│    ├─── Ananke.Orchestration.OpenAI  (Orchestration)
│    ├─── Ananke.Orchestration.Anthropic  (Orchestration)
│    ├─── Ananke.Orchestration.Google  (Orchestration)
│    └─── demos that don't use learning
│
├─── Ananke.Redis  (Abstractions + Orchestration)
│
└─── Other leaf packages (A2A, MCP, MQTT, AspNetCore, etc.)
```

---

## Validation Checklist

| Step | Verification |
|------|-------------|
| Phase 1 complete | `dotnet build Ananke.Abstractions` succeeds; no type uses `Ananke.Orchestration.*` namespace in Abstractions assembly |
| Phase 2 complete | `dotnet build Ananke.Learning` succeeds; `Ananke.Orchestration/Memory/` contains only `InMemoryConversationMemory.cs` |
| Phase 3 complete | `dotnet build Ananke.Orchestration` succeeds; Knowledge sub-folders match the plan |
| Phase 4 complete | `dotnet build Ananke.sln` succeeds; all demos run without runtime errors |
| Phase 5 complete | `dotnet pack` succeeds for all packable projects; NuGet packages have correct dependency declarations |
| Full validation | All existing unit tests pass; Connect4Demo trains and plays correctly |

---

## Summary

| Phase | Files touched | Risk | Effort |
|-------|---------------|------|--------|
| 1 — Abstractions namespaces | ~70+ (namespace + using updates) | Medium (high touch, compiler-safe) | Medium |
| 2 — Create Ananke.Learning | ~15 (move + namespace change) | Low (new project, clean separation) | Low-Medium |
| 3 — Knowledge sub-folders | ~24 (move + namespace change) | Low (internal to Orchestration) | Low-Medium |
| 4 — Update providers/demos | ~15 (using + csproj changes) | Low (mechanical) | Low |
| 5 — CI/CD setup | ~2 (workflow + csproj metadata) | Low | Low |

**Total estimated effort:** Medium. Phase 1 is the most labor-intensive due
to the number of files that import Abstractions types. Phases 2-5 are
straightforward file moves and `using` statement updates.

**Recommended execution order:** Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5,
ideally in a single PR to avoid intermediate broken states on the main branch.
