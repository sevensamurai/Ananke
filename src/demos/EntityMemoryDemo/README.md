# EntityMemoryDemo

Per-entity long-term memory for a furniture shopping companion. A workflow handles each customer visit — on the first visit recommendations are generic; on return visits the customer's learned profile drives personalized recommendations.

## What it demonstrates

| Capability | How |
|---|---|
| **Workflow-driven shopping** | A `Workflow<ShopState>` with four jobs (`load_profile → browse → recommend → learn`) handles every customer visit |
| **Cold-start vs. personalized** | First visit: no profile → generic bestsellers. Return visit: profile loaded → style-matched recommendations |
| **Per-entity memory isolation** | `EntityMemoryProvider` creates scoped facades — Customer-8472's minimalist preferences never leak into Customer-9999's baroque preferences |
| **Entity-scoped empirical memory** | Patterns and heuristics committed through `IEntityMemory.Empirical` automatically carry the entity's ID |
| **Entity-scoped knowledge** | Style profile documents upserted through `IEntityMemory.Knowledge` are tagged with entity metadata and filtered on search |
| **Cross-entity dedup isolation** | Two customers with similar browsing patterns produce separate entries — dedup never merges across entity boundaries |
| **Learning accumulates** | Each visit adds to the profile — visit 3 sees patterns from visit 1 and learns new heuristics |
| **Learned profile display** | Browse all entries by kind (patterns, heuristics, skills) with kind-specific detail fields |

## The workflow

```
  load_profile → browse → recommend → learn → END
```

| Job | What it does |
|-----|-------------|
| **load_profile** | Recalls entity-scoped empirical entries + knowledge docs via `IEntityMemory`. Sets `IsReturning = true` if any profile exists. |
| **browse** | Simulates browsing (items vary by customer). In production this would capture real click/dwell signals. |
| **recommend** | If returning: matches recalled patterns to catalog categories → personalized selection. If new: generic bestsellers. |
| **learn** | Commits observed patterns and heuristics to entity memory. Upserts a knowledge doc with the style profile summary. |

The same workflow instance handles every customer — `EntityMemoryProvider` provides the per-customer personalization.

## Architecture

All entities share a single set of stores. Entity isolation is metadata-based, not physical partitioning:

```
                          EntityMemoryProvider
                         ┌──────────────────────┐
                         │  GetOrCreate("8472")  │──▶ IEntityMemory (customer-8472)
                         │  GetOrCreate("9999")  │──▶ IEntityMemory (customer-9999)
                         └──────────┬───────────┘
                                    │ wraps (decorators)
                ┌───────────────────┼───────────────────┐
                ▼                   ▼                   ▼
    InMemoryEmpiricalMemory   InMemoryKnowledgeStore   InMemoryEpisodeStore
         (shared)                  (shared)                (shared)
```

Each `IEntityMemory` instance exposes standard Ananke interfaces (`IEmpiricalMemory`, `IKnowledgeStore`, `IEpisodeStore`, `IConversationMemory`) with transparent entity-scoping decorators that inject entity IDs on writes and filter by entity on reads.

## State machine integration

For long-lived entities managed by `AbstractStateMachine`, the state machine's `IBaseContext.Id` **is** the entity ID — they're the same concept (opaque string partition keys). Pass `context.Id` directly:

```csharp
// Inside a state machine OnEnter/OnExit handler:
var memory = provider.GetOrCreate(context.Id);
var prefs = await memory.Empirical.RecallAsync("preferences",
    new RecallOptions { IncludeGlobal = true });
```

## Production backends

The demo uses in-memory stores. For production, swap the shared infrastructure:

```csharp
// Dev/test (this demo):
var empirical = new InMemoryEmpiricalMemory(embedder);
var knowledge = new InMemoryKnowledgeStore(embedder);

// Production:
var empirical = new QdrantEmpiricalMemory(qdrantClient, embedder, "empirical");
var knowledge = new QdrantKnowledgeStore(qdrantClient, embedder, "knowledge");

// Same provider, same decorators — entity scoping works identically
var provider = new EntityMemoryProvider(conversations, empirical, knowledge, episodes);
```

All entities share a single Qdrant collection (or Redis instance). Entity isolation is handled by metadata filtering in the decorators, not by creating separate collections per entity.

## Running

```bash
cd src
dotnet run --project demos/EntityMemoryDemo
```

No LLM, no Docker, no external services. The demo uses `InMemoryEmbedder` for deterministic vectors.

## Demo visits

| Visit | Customer | What happens |
|-------|----------|-------------|
| **1** | customer-8472 | First visit. No profile → cold-start generic recommendations. Learns: minimalist pattern + sustainable heuristic + style profile doc. |
| **2** | customer-9999 | First visit. Different preferences → learns baroque pattern + style profile doc. No interference with 8472. |
| **3** | customer-8472 | **Returns.** Profile loaded (minimalist + sustainable recalled). Personalized recommendations: sustainable + minimalist items. Learns: art deco heuristic. |
| **4** | — | Full learned profile for Customer-8472: patterns, heuristics, knowledge docs. |
