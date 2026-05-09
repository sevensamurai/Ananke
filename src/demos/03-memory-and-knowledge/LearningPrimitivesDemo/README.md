# LearningPrimitivesDemo — Skills, Routing & Knowledge Graph

Three selectable scenarios demonstrating `Ananke.Learning`, `Ananke.Skills`, and the knowledge-graph substrate.

---

## Quick Start

```bash
cd demos/03-memory-and-knowledge/LearningPrimitivesDemo
dotnet run                                   # skills scenario (default)
dotnet run -- --scenario routing             # routing evolution scenario
dotnet run -- --scenario knowledge-graph     # knowledge graph, multi-hop retrieval & PageRank
```

---

## Scenarios

### Knowledge graph scenario (`--scenario knowledge-graph`)

Runs entirely offline — no API key, no Qdrant, no external services required.

Seeds an `InMemoryEmpiricalMemory` with 31 fixture entries across three implicit topics (`gc-pauses`, `db-deadlocks`, `network-flapping`) plus one bridge entry that links two topics through a shared tag. Shows how the knowledge-graph substrate surfaces relationships that flat retrieval misses.

| Step | What happens |
|---|---|
| **Seed** | 31 entries committed — 10 per topic (varied valence) + 1 bridge entry tagged with a cross-topic bridge tag |
| **Graph build** | `TagCoOccurrenceBuilder` projects all entries into an `InMemoryKnowledgeGraph` — entry ↔ tag nodes, `tagged` + `co_occurs` edges |
| **Multi-hop comparison** | `TagOverlapPredictionSource` (flat) vs. `GraphExpandedPredictionSource` (2-hop) — the graph source recovers Topic C entries via the bridge tag, flat retrieval cannot |
| **Importance comparison** | `GraphTagImportanceTracker` (PageRank) vs. `TagImportanceTracker` (frequency) — bridge tag ranks higher under PageRank despite low raw frequency |
| **Export** | `KnowledgeReportExporter` writes `memory-graph.json` + `MEMORY_REPORT.md` to `./out/learning-knowledge-graph/`; first 20 lines echoed to console |

**Prerequisites:** none — all embeddings are deterministic fakes.

---

### Skills scenario (default)

Demonstrates the full OpenClaw skill pipeline: register an external CLI skill, search the catalog by capability description, and let an agent call it.

| Step | What happens |
|---|---|
| **Catalog seed** | `OpenClawCatalog` is initialised with a `JsonFileScoreStore` and a `cowsay` `SkillDescriptor` (uvx install method) |
| **Toolkit assembly** | `ToolKit.AddFromCatalogAsync` searches the catalog for `"ascii art text"` and loads up to 3 matching skills |
| **Agent call** | An `OpenAI`-backed agent receives the toolkit and is prompted to generate ASCII art — it calls `cowsay` via the skill bridge |
| **Score persistence** | Tool call outcomes are written back to `scores.json` so the catalog improves across runs |

**Prerequisites:**
- `uv` installed: `winget install astral-sh.uv`
- `OpenAI:ApiKey` in `secrets.json`

---

### Routing scenario (`--scenario routing`)

Simulates post-division routing evolution for a bookstore mesh (Hybrid Routing, Option D).

A `bookstore-general` workflow has been divided into `bookstore-catalog` and `bookstore-orders`. The demo shows how routing evolves in two phases:

**Phase 1 — Qdrant vector routing**

The division emits a routing artifact (tool descriptions per child). A `QdrantDomainRouter` indexes these descriptions and classifies incoming prompts by vector similarity.

**Phase 2 — Adaptive UCB routing**

`RoutingAffinityTracker` observes routing outcomes and refines assignments using Upper Confidence Bound explore/exploit. Over multiple rounds, neural pathway formation emerges — frequently correct routes get stronger, incorrect ones weaken.

All embeddings are deterministic fakes (`FakeEmbeddingModel`) — no API key required. Requires a running Qdrant instance:

```bash
docker run -p 6334:6334 qdrant/qdrant
```

**Prerequisites:**
- Qdrant running on `localhost:6334`
- No API keys needed

---

## Project Structure

| File | Purpose |
|---|---|
| `Program.cs` | Entry point; parses `--scenario` and delegates to the selected scenario |
| `Knowledge/KnowledgeGraphScenario.cs` | Tag co-occurrence graph, multi-hop retrieval, PageRank importance, report export |
| `Knowledge/TopicFixture.cs` | 31 fixture entries across 3 topics + 1 bridge entry |
| `Skills/SkillsScenario.cs` | OpenClaw catalog, toolkit assembly, agent call |
| `Routing/RoutingScenario.cs` | Qdrant vector router, UCB affinity tracker, routing evolution |
| `Routing/FakeEmbeddingModel.cs` | Deterministic embedding stub for offline use |

---

## Key Concepts

- **`TagCoOccurrenceBuilder`** — projects empirical entries into a typed knowledge graph (entry ↔ tag nodes, `co_occurs` edges)
- **`GraphExpandedPredictionSource`** — k-hop BFS expansion over the tag graph for multi-hop retrieval
- **`GraphTagImportanceTracker`** — PageRank-based tag importance (bridge tags promoted over raw frequency)
- **`KnowledgeReportExporter`** — writes `memory-graph.json` + `MEMORY_REPORT.md` to a target directory
- **`OpenClawCatalog`** — discovers, installs, and invokes external CLI skills as Ananke tools
- **`SkillDescriptor`** — describes a skill's parameters and install method (uvx, npx, etc.)
- **`JsonFileScoreStore`** — persists tool performance scores between runs
- **`ToolKit.AddFromCatalogAsync`** — semantic search over the catalog to assemble a toolkit
- **`QdrantDomainRouter`** — classifies requests to child workflows by vector similarity
- **`RoutingAffinityTracker`** — UCB-based adaptive routing that learns from outcomes
