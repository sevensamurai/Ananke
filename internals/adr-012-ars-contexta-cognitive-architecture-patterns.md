# ADR-012 — Ars Contexta Cognitive Architecture Patterns

**Status:** Partially accepted — adopted for `IKnowledgeStore` document organization; rejected for `IEmpiricalMemory`  
**Date:** 2025-07-13  
**Authors:** Team  
**References:** [Ars Contexta](https://github.com/agenticnotetaking/arscontexta), Guides [06](../guides/06-memory.md), [15](../guides/15-empirical-memory.md), [15a](../guides/15a-empirical-memory-tuning.md)

---

## Context

Ananke already provides three memory layers for agents:

| Layer | Interface | What it stores |
|---|---|---|
| Semantic | `IKnowledgeStore` | Document chunks — "what the docs say" |
| Episodic | `IConversationMemory` | Conversation turns — "what was said" |
| Empirical | `IEmpiricalMemory` | Patterns, skills, heuristics — "what the agent has learned" |

The [Ars Contexta](https://github.com/agenticnotetaking/arscontexta) project — a
Claude Code plugin that generates personalized knowledge systems from conversation —
introduces several architectural patterns backed by 249 interconnected research claims.
This ADR analyzes three of those patterns for their potential value in Ananke's
empirical and long-term memory subsystems:

1. **The 6 Rs Processing Pipeline** — a meta-cognitive loop for knowledge refinement
2. **Three-Space Architecture** — separation of identity, knowledge, and operations
3. **Research-Linked Derivation** — evidence-grounded structural decisions

---

## Critical Assessment: Mental Model Replication vs. Empirical Learning

Before detailing the patterns, it is important to characterize **what Ars Contexta
actually optimizes for** and how that diverges from Ananke's goals.

Ars Contexta is fundamentally a **digital twin / mental model builder**. Its setup
process interviews the user about "how you think and work" and then derives a
knowledge architecture that faithfully mirrors that person's existing cognitive style.
The 6 Rs pipeline, Reweave in particular, then continuously reinforces that model by
densifying connections between entries that share structural affinity.

This is valuable for its stated purpose — giving an AI agent a persistent
representation of a specific human's approach. But it is **architecturally opposed**
to what Ananke's empirical memory tries to achieve:

| | Ars Contexta | Ananke Empirical Memory |
|---|---|---|
| **Goal** | Faithfully replicate and reinforce an existing mental model | Discover ground truth from repeated observation |
| **Bias posture** | Preserves the user's biases as features ("how you think") | Treats bias as a failure mode to be corrected |
| **Connectivity growth** | Unconditional — Reweave links anything semantically related | Conditional — `ReinforceAsync` requires outcome evidence |
| **Contradiction** | No mechanism — notes are only enriched, never weakened | First-class — `ContradictAsync` degrades confidence |
| **Decay** | None — all notes persist at equal standing | Built-in — `BaseDecayRate`, `VarianceDecayRate`, `DeletionThreshold` |
| **Exploration** | None — the system deepens existing structure | Active — `OfflineLearner` with curiosity-driven selection |

Ars Contexta assumes **the user's mental model is correct and worth preserving**.
Ananke's empirical memory assumes **the agent's initial beliefs may be wrong and
must be tested against evidence**. These are fundamentally different epistemological
commitments.

### Where the approach *is* valid: long-term document and procedure organization

The bias-amplification critique applies specifically to **empirical memory** — entries
that represent agent-generated hypotheses about the world. But Ananke's other memory
layer, `IKnowledgeStore`, holds a fundamentally different kind of content:
**authoritative documents and procedures** — onboarding policies, API references,
runbooks, compliance checklists.

For this content, unconditional Reweave is *safe* because:

| Property | Why bias amplification doesn't apply |
|---|---|
| **Source material is authoritative** | Documents and procedures are authored by humans and reviewed before ingestion. They don't need to "survive contradiction" — they *are* the ground truth. |
| **Links represent editorial relationships** | Connecting a runbook section to a related API doc is a navigational convenience, not a hypothesis. Getting it wrong costs retrieval precision, not epistemological integrity. |
| **No confidence dynamics** | `KnowledgeChunk` has no confidence score to corrupt. A bad link at worst surfaces an irrelevant chunk, which the agent can ignore. |
| **Humans control the input** | The ingestion pipeline (`DocumentProcessor`) only indexes what a human or admin submits. There's no self-reinforcing feedback loop. |

This means Ars Contexta's core patterns — cross-document linking, MOC-style indexes,
dual retrieval — can be adopted for `IKnowledgeStore` without the risks identified
for `IEmpiricalMemory`. The key constraint: **the approach is a document organization
tool, not a learning system.**

---

## The 6 Rs Processing Pipeline

### Origin

The 6 Rs extend the Cornell Note-Taking System's original 5 Rs (Record, Reduce,
Recite, Reflect, Review) with a sixth meta-cognitive phase. Cornell was designed in
1950 by Walter Pauk at Cornell University as a structured method for students to
process lecture material. The key insight was that **raw capture without structured
processing produces low-retention, low-utility notes**. Ars Contexta adapts this for
agents, replacing human study habits with automated pipeline phases.

### The six phases

| Phase | What happens | Cognitive function | Ananke analog |
|---|---|---|---|
| **Record** | Zero-friction capture into an inbox. No categorization, no judgment — just raw material. | Working memory intake | Agent conversation turns; tool outputs; raw observations |
| **Reduce** | Extract discrete insights from raw capture. Apply domain-native categories. Separate signal from noise. | Selective attention, encoding | `DocumentProcessor` chunking; `CommitAsync` creating `EmpiricalEntry` from observation |
| **Reflect** | Find connections between the new insight and existing knowledge. Update navigation maps (MOCs). | Elaborative encoding, schema integration | `RecallAsync` finding related entries; semantic similarity linking |
| **Reweave** | **Backward pass** — revisit *older* entries and update them with connections discovered from the new insight. | Memory reconsolidation | **No direct analog today** — this is the most novel primitive |
| **Verify** | Check structural integrity: schema compliance, description quality, health metrics. | Metacognition, error monitoring | Validation logic in stores; confidence scoring |
| **Rethink** | Challenge system-level assumptions. Are categories still valid? Is the methodology drifting? | Meta-learning, paradigm revision | `OfflineLearner` exploration; contradiction detection |

### Why the backward pass (Reweave) matters

Most knowledge systems — including Ananke's current memory layers — only move
forward: new data arrives, gets processed, gets stored. Old entries remain as they
were at commit time. Reweave reverses this:

```
New insight arrives
  → Forward pass: connect new → existing (Reflect)
  → Backward pass: update existing ← new (Reweave)
```

This mirrors **memory reconsolidation** in neuroscience. When a human recalls a
memory, that memory becomes temporarily labile — open to modification with new
information before being re-stored. The result is that memories become richer and
more interconnected over time rather than remaining isolated snapshots.

**Research backing:**
- Nader, Schiller & LeDoux (2000) — *Reconsolidation*: retrieved memories return to
  a labile state and can be updated before re-storage.
- Bjork & Bjork (1992) — *Desirable difficulties*: effortful retrieval and
  re-encoding strengthens long-term retention more than passive review.
- Roediger & Butler (2011) — *Testing effect*: the act of retrieving information
  (even to check connections) strengthens the retrieved memory itself.

**Potential for Ananke's empirical memory:** When an agent commits a new
`EmpiricalEntry`, a Reweave phase could search for older entries whose `Condition`
or `Effect` fields are semantically related and append a cross-reference to their
`Evidence` list. Over time, entries that are truly important would accumulate dense
evidence trails without any single observation needing to be comprehensive. The
`ReinforceAsync` / `ContradictAsync` pattern already adjusts confidence — Reweave
would additionally enrich the *structural connectivity* of the knowledge graph.

### Reweave as a bias amplifier — the fundamental problem

The reconsolidation metaphor is seductive but incomplete. In neuroscience,
reconsolidation is **not** simply "retrieve and enrich." It is "retrieve, destabilize,
and re-store" — and the destabilization step is where correction happens. Ars
Contexta's Reweave skips destabilization entirely. It only adds links. It never
removes them, weakens them, or questions whether the connection is valid.

This creates a **confirmation bias feedback loop:**

```
Entry A exists (possibly wrong)
  → New Entry B arrives, semantically similar
  → Reweave links A ↔ B
  → A now has more connections → surfaces more often in retrieval
  → A's increased visibility makes it more likely to be linked again
  → A becomes structurally entrenched regardless of whether it is true
```

In graph theory terms, Reweave produces **preferential attachment** (Barabási &
Albert, 1999): well-connected nodes attract more connections, not because they are
more correct, but because they are more visible. This is exactly the mechanism that
creates filter bubbles in social networks and echo chambers in recommendation systems.

Ananke's empirical memory has explicit countermeasures against this:

| Mechanism | What it prevents |
|---|---|
| `ContradictAsync` | Entries that fail prediction lose confidence — structural connectivity doesn't save them |
| `BaseDecayRate` / `VarianceDecayRate` | Unconfirmed entries fade regardless of how many links they have |
| `DeletionThreshold` | Entries below minimum strength are removed — no permanent residents |
| `CuriosityThreshold` | `OfflineLearner` preferentially explores high-variance (uncertain) entries, not high-connectivity ones |
| `ExplorationRandomFraction` | ε-greedy exploration ensures the agent doesn't only revisit familiar territory |
| `ContradictionPenaltyWeight` | Intrinsic reward system actively penalizes entries that contradict evidence |

**Unconditional Reweave would undermine all of these.** A Reweave phase that adds
links without checking whether the linked entries have survived contradiction,
maintained confidence, and demonstrated predictive value would create structural
anchors that resist the very correction mechanisms Ananke was designed around.

### Conditional Reweave — if adopted at all

If a backward-linking mechanism were ever added to Ananke, it would need to be
**gated on empirical standing**, not just semantic similarity:

```
Reweave candidate:
  ✓ Semantic similarity > threshold
  ✓ Both entries have confidence > MinConfidence
  ✓ Neither entry is in contradiction state
  ✓ Both entries have been reinforced at least once (not just committed)
  ✓ Link weight is proportional to min(confidence_A, confidence_B)
  ✗ Links decay alongside their weakest endpoint
```

This would be **Reweave with empirical gating** — structurally inspired by Ars
Contexta but epistemologically aligned with Ananke. The link itself becomes a
hypothesis that must survive the same scrutiny as the entries it connects.

### Fresh context per phase

Ars Contexta runs each pipeline phase in a **separate subagent with a fresh context
window**. The rationale is empirically grounded in LLM attention research:

- **Lost in the Middle** (Liu et al., 2023): LLMs perform significantly worse at
  recalling information placed in the middle of long contexts compared to the
  beginning or end. As an agent's context fills during a session, attention degrades.
- **Attention sink** effects: early tokens receive disproportionate attention weight,
  crowding out mid-context information.

By spawning a fresh context per phase, each phase operates in what Ars Contexta
calls the "smart zone" — full attention capacity with minimal interference.

```
/ralph 5
  ├── Read queue, find next unblocked task
  ├── Spawn subagent (fresh context)
  │   └── Runs skill, updates task file, returns handoff
  ├── Parse handoff, capture learnings
  ├── Advance phase in queue
  └── Repeat for 5 tasks
```

**Relevance to Ananke:** The `OfflineLearner` already runs background cycles
independent of the active conversation. Extending this to a multi-phase pipeline
where each phase gets its own orchestration context (via `StreamingChatWorkflow`
subflows) would preserve the attention-quality benefits Ars Contexta demonstrates.

---

## Three-Space Architecture

### The separation

Ars Contexta organizes all agent-persistent state into three spaces with fundamentally
different change rates and purposes:

```
vault/
├── self/       ← Identity, methodology, goals        (slow: tens of files)
├── notes/      ← Knowledge graph — the core content   (steady: 10-50/week)
└── ops/        ← Queue state, sessions, maintenance   (fluctuating: ephemeral)
```

| Space | What it holds | Change rate | Cognitive parallel |
|---|---|---|---|
| **`self/`** | Agent identity, methodology, operating principles, goals | Slow — updated only when fundamental assumptions change | **Semantic memory (self-model)** — who I am, how I think, what I value |
| **`notes/`** | Domain knowledge as atomic, interlinked notes connected by wiki links and organized through Maps of Content (MOCs) | Steady — grows with every learning session | **Semantic + episodic memory (knowledge)** — what I know and when I learned it |
| **`ops/`** | Queue files, session logs, maintenance signals, task state | Fluctuating — high write frequency, low retention value | **Working memory (operational state)** — what I'm doing right now |

The names are customizable (`notes/` might become `claims/`, `decisions/`, or
`reflections/` depending on the domain), but the **three-way separation is invariant**.

### Why this decomposition matters

The separation maps directly to how different categories of knowledge should behave:

**1. Different change rates demand different storage strategies.**
Identity/methodology should be versioned and reviewed before changes. Knowledge
should be append-friendly with backward linking. Operational state should be cheap
to create and safe to discard.

**2. Different retrieval patterns.**
- `self/` is loaded at session start (small, high-value, always relevant).
- `notes/` is searched on demand via graph traversal or semantic similarity.
- `ops/` is read only by the orchestration layer, never by the reasoning agent.

**3. Prevents operational noise from polluting the knowledge graph.**
Without separation, session logs, queue state, and maintenance signals end up in
the same search index as domain knowledge — reducing retrieval precision.

### Research backing

- **Tulving (1972, 1985) — Episodic vs. semantic memory:** The foundational
  distinction between "remembering" (contextual, time-stamped) and "knowing"
  (decontextualized, factual). The three-space model adds a third category:
  procedural/operational, which is neither remembering nor knowing but *doing*.

- **Extended Mind Thesis — Clark & Chalmers (1998):** Cognitive processes extend
  beyond the brain into external structures *when those structures are reliably
  coupled to the cognitive agent*. The three-space architecture creates that reliable
  coupling: the agent can depend on `self/` being stable, `notes/` being searchable,
  and `ops/` being current — each with appropriate guarantees.

- **Context-switching cost — Leroy (2009), Mark et al. (2008):** Switching between
  unrelated information types increases cognitive overhead. When an agent loads
  context, mixing identity files with queue state and knowledge notes forces
  unnecessary attention allocation. Spatial separation reduces this load.

- **PARA method — Tiago Forte:** The Projects/Areas/Resources/Archives framework
  separates information by *actionability*. Three-space is a similar decomposition
  optimized for agents rather than humans: `ops/` ≈ Projects (active), `notes/` ≈
  Resources (reference), `self/` ≈ Areas (ongoing responsibilities).

### Mapping to Ananke's current architecture

| Three-Space | Ananke equivalent | Gap |
|---|---|---|
| `self/` (identity) | System prompt; `AffectOptions`; `OfflineLearnerOptions` | No persistent, evolving identity store. System prompts are static. |
| `notes/` (knowledge) | `IKnowledgeStore` + `IEmpiricalMemory` | Good coverage. Missing: inter-entry linking (graph structure). |
| `ops/` (operational) | `IConversationMemory`; internal workflow state | Conversation memory mixes operational and episodic. No explicit session-state persistence across restarts. |

The most immediate opportunity is in **inter-entry linking** within empirical memory.
Currently, `EmpiricalEntry` objects are independent — they share tags but have no
explicit graph edges. Adding a `RelatedEntries` field (populated during a Reweave
phase) would move Ananke's knowledge layer toward the traversable graph structure
that makes Ars Contexta's `notes/` space powerful.

---

## Research-Linked Derivation

### The research graph

Ars Contexta's `methodology/` directory contains 249 interconnected research claims
from cognitive science, knowledge management, and agent architecture. Every structural
decision in the system traces to one or more of these claims via `cognitive_grounding`
annotations in the kernel primitives.

Example from their kernel:

```yaml
# kernel.yaml — MOC hierarchy primitive
moc_hierarchy:
  description: "Maps of Content at hub, domain, and topic levels"
  cognitive_grounding:
    - claim: "Context-switching between unrelated topics incurs measurable cognitive cost"
      source: "Leroy 2009"
    - claim: "Hierarchical organization reduces search time in large knowledge bases"
      source: "Miller 1956 (chunking); Rosch 1975 (prototype theory)"
```

### Key research traditions synthesized

| Tradition | Core insight | How it's applied |
|---|---|---|
| **Zettelkasten** (Luhmann) | Atomic notes with explicit links create emergent structure. The value is in the *connections*, not the individual notes. | Notes are atomic (one idea per file). Links are first-class citizens. Structure emerges from link density, not from pre-imposed hierarchy. |
| **Cornell Note-Taking** (Pauk, 1950) | Raw capture without structured review produces poor retention. The 5 Rs (Record, Reduce, Recite, Reflect, Review) force progressive deepening. | The 6 Rs pipeline. Each phase corresponds to a deeper level of cognitive processing. |
| **Evergreen Notes** (Matuschak) | Notes should be concept-oriented, densely linked, and continuously refined — living documents, not archival snapshots. | Notes gain links over time via Reweave. Descriptions evolve. Nothing is "done." |
| **Spreading Activation** (Collins & Loftus, 1975) | Memory retrieval works by activating a node and propagating activation through weighted links to related nodes. | Wiki-link graph + MOC traversal = structural retrieval path complementing vector search. Two retrieval modalities: graph traversal (structural) and embedding similarity (semantic). |
| **Generation Effect** (Slamecka & Graf, 1978) | Information that is *generated* (actively produced) is remembered better than information that is *read* (passively received). | The system doesn't copy templates — it *derives* structure from conversation. The derivation process itself produces better understanding. |
| **Small-World Network Topology** (Watts & Strogatz, 1998) | Networks with high clustering and short path lengths support efficient information retrieval. Most real knowledge networks have this structure. | MOC hubs create short paths between clusters. The goal is a knowledge graph where any two notes are reachable within 2-3 hops. |
| **Desirable Difficulties** (Bjork, 1994) | Learning conditions that introduce productive challenges (spacing, interleaving, retrieval practice) produce stronger long-term retention. | Reweave forces retrieval of old notes during new-note processing. The 6 Rs pipeline introduces spacing between capture and deep processing. |
| **Memory Reconsolidation** (Nader et al., 2000) | Retrieved memories become labile and can be updated before re-storage. This is the mechanism by which memories integrate new information. | Reweave explicitly implements reconsolidation: retrieve an old entry, update it with new connections, re-store it. |

### Dual retrieval: graph traversal + semantic search

A particularly valuable insight from Ars Contexta is the combination of two
retrieval modalities:

```
Query → Semantic search (vector similarity)     → ranked results
Query → Graph traversal (wiki links from seed)  → associated results
        ─────────────────────────────────────
        Merged, deduplicated, re-ranked
```

- **Semantic search** finds entries that are *about* similar topics even when
  vocabulary differs. This is what `IKnowledgeStore.SearchAsync` and
  `IEmpiricalMemory.RecallAsync` already do.

- **Graph traversal** finds entries that are *structurally connected* — entries that
  were linked during Reflect or Reweave phases. These might use completely different
  vocabulary but are known to be causally or thematically related because a previous
  processing phase established the link.

The two modalities have complementary failure modes: semantic search misses entries
that are related but use different terminology *and* have different embedding
signatures (e.g., a GC-tuning heuristic related to a timeout pattern). Graph
traversal misses entries that are semantically related but were never explicitly
connected. Together, they approximate the **spreading activation** retrieval model
from cognitive science.

---

## Relevance to Ananke's Empirical Memory

### What Ananke already does well

Ananke's empirical memory system is already more sophisticated than Ars Contexta's
in several respects:

- **Affect-weighted recall** (`Valence`, `Intensity`, `Strength`) — entries have
  emotional/importance coloring that influences retrieval priority. Ars Contexta has
  no equivalent.
- **Confidence dynamics** (`ReinforceAsync`, `ContradictAsync`, decay) — entries
  gain or lose confidence based on outcomes. Ars Contexta's notes are static once
  written.
- **Offline learning** (`OfflineLearner`) — background exploration with curiosity-
  driven selection, intrinsic reward, and consolidation. This is significantly more
  advanced than anything in Ars Contexta.
- **Typed knowledge** (`EmpiricalKind`: Pattern, Skill, Heuristic) — structured
  entry types with domain-specific fields. Ars Contexta uses untyped markdown.

### Scoped applicability: which patterns go where

The patterns have fundamentally different risk profiles depending on which memory
layer they target:

| Pattern | `IKnowledgeStore` (documents) | `IEmpiricalMemory` (agent learnings) |
|---|---|---|
| **Reweave (backward linking)** | ✅ Safe — source material is authoritative; links are navigational | ❌ Dangerous — creates preferential attachment and bias reinforcement |
| **Graph-augmented search** | ✅ Safe — read-only expansion, worst case is irrelevant results | ❌ Risky — without empirical gating, surfaces entrenched-but-wrong entries |
| **MOC-style indexes** | ✅ Useful — cluster-based catalog summaries for large document sets | ⚠️ Conditional — only if generated from `OfflineLearner` consolidation, not link density |
| **Three-space session persistence** | ✅ Adopt — `ISessionState` for cross-restart continuity | ✅ Adopt — same benefit, orthogonal to bias concern |
| **Fresh context per phase** | ✅ Adopt — multi-step ingestion pipelines | ✅ Adopt — `OfflineLearner` multi-phase cycles |
| **Research annotations** | ✅ Adopt — editorial enrichment of guides | ✅ Adopt — same benefit |

The key principle: **Ars Contexta patterns are document organization tools, not
learning systems.** When applied to `IKnowledgeStore` — where humans control what
goes in and the source material is the ground truth — they improve navigability
without epistemological risk. When applied to `IEmpiricalMemory` — where the agent
generates and recalls its own hypotheses — they amplify whatever the agent already
believes, including errors.

---

## Implementation Path: Optional Document Linking for `IKnowledgeStore`

The patterns identified above can be adopted for long-term document organization via
two mechanisms that match Ananke's existing extension model: a **decorator** (like
`CatalogAwareKnowledgeStore`) and a **factory** (like `DocumentExtractorFactory`).
Both are opt-in — the base `IKnowledgeStore` contract remains unchanged.

### Option A: Decorator — `LinkedKnowledgeStore`

A decorator over `IKnowledgeStore` that maintains a cross-document link graph and
expands search results through graph traversal. Follows the same pattern as
`CatalogAwareKnowledgeStore`.

```csharp
// New interface — the link graph is a separate concern from the vector store
public interface IDocumentLinkGraph
{
    Task AddLinkAsync(string sourceChunkId, string targetChunkId,
        string relationship, float weight = 1.0f, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentLink>> GetLinksAsync(
        string chunkId, int maxHops = 1, CancellationToken ct = default);

    Task RemoveLinksAsync(string chunkId, CancellationToken ct = default);
}

public sealed record DocumentLink(
    string SourceId, string TargetId, string Relationship, float Weight);

// Decorator: wraps any IKnowledgeStore to add graph-expanded search
public sealed class LinkedKnowledgeStore : IKnowledgeStore
{
    private readonly IKnowledgeStore _inner;
    private readonly IDocumentLinkGraph _graph;
    private readonly LinkedSearchOptions _linkOptions;

    public LinkedKnowledgeStore(
        IKnowledgeStore inner,
        IDocumentLinkGraph graph,
        LinkedSearchOptions? linkOptions = null) { /* ... */ }

    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        // 1. Standard vector search
        var results = await _inner.SearchAsync(query, options, ct);

        if (!_linkOptions.ExpandGraph || results.Count == 0)
            return results;

        // 2. Graph expansion: for each top result, traverse 1-hop links
        var expanded = new Dictionary<string, KnowledgeChunk>();
        foreach (var chunk in results)
            expanded.TryAdd(chunk.Id, chunk);

        foreach (var chunk in results.Take(_linkOptions.ExpansionSeeds))
        {
            var links = await _graph.GetLinksAsync(
                chunk.Id, _linkOptions.MaxHops, ct);

            foreach (var link in links)
            {
                if (expanded.ContainsKey(link.TargetId))
                    continue;

                // Fetch the linked chunk from the inner store
                var linked = await _inner.SearchAsync(
                    "", new SearchOptions
                    {
                        TopK = 1,
                        Filter = new KnowledgeFilter { ["id"] = link.TargetId }
                    }, ct);

                if (linked.Count > 0)
                {
                    // Score = original chunk score × link weight × decay
                    var graphScore = chunk.Score * link.Weight
                        * _linkOptions.GraphScoreDiscount;
                    expanded.TryAdd(link.TargetId,
                        linked[0] with { Score = graphScore });
                }
            }
        }

        // 3. Merge and re-rank
        return expanded.Values
            .OrderByDescending(c => c.Score)
            .Take(options?.TopK ?? 5)
            .ToList();
    }

    // UpsertAsync and DeleteAsync delegate to inner;
    // DeleteAsync also cleans the link graph
}

public sealed record LinkedSearchOptions
{
    public bool ExpandGraph { get; init; } = true;
    public int ExpansionSeeds { get; init; } = 3;
    public int MaxHops { get; init; } = 1;
    public float GraphScoreDiscount { get; init; } = 0.8f;
}
```

**Composability:** The decorator stacks with `CatalogAwareKnowledgeStore`:

```csharp
// inner → catalog-aware → linked = full pipeline
var inner = new InMemoryKnowledgeStore(embedder);
var catalogAware = new CatalogAwareKnowledgeStore(inner, catalog, extractor);
var linked = new LinkedKnowledgeStore(catalogAware, linkGraph);
```

### Option B: Factory — `DocumentLinkExtractor`

An LLM-based link extractor that runs post-ingestion, following the same pattern as
`CatalogKeywordExtractor`. It reads newly upserted chunks, finds semantically related
existing chunks, and proposes links.

```csharp
public sealed class DocumentLinkExtractor
{
    private readonly IAgentModel _model;
    private readonly IKnowledgeStore _store;
    private readonly IDocumentLinkGraph _graph;
    private readonly float _similarityThreshold;

    public DocumentLinkExtractor(
        IAgentModel model,
        IKnowledgeStore store,
        IDocumentLinkGraph graph,
        float similarityThreshold = 0.7f) { /* ... */ }

    /// <summary>
    /// For each chunk in the newly ingested source, finds related chunks
    /// across other sources and creates links with LLM-classified
    /// relationship types ("references", "contradicts", "extends",
    /// "prerequisite", "example-of").
    /// </summary>
    public async Task LinkSourceAsync(
        string sourceId, CancellationToken ct = default)
    {
        // 1. Get all chunks for this source
        // 2. For each chunk, search for related chunks in other sources
        // 3. Ask LLM to classify the relationship
        // 4. Store links in the graph
    }
}
```

### Option C: Plugin registration via DI

For users who want the full pipeline, a DI extension method that wires everything up:

```csharp
public static class KnowledgeLinkingExtensions
{
    /// <summary>
    /// Adds optional cross-document linking to the knowledge pipeline.
    /// When enabled, newly ingested documents are automatically linked
    /// to existing related documents, and search results are expanded
    /// through the link graph.
    /// </summary>
    public static IServiceCollection AddKnowledgeLinking(
        this IServiceCollection services,
        Action<KnowledgeLinkingOptions>? configure = null)
    {
        var options = new KnowledgeLinkingOptions();
        configure?.Invoke(options);

        services.AddSingleton<IDocumentLinkGraph, InMemoryDocumentLinkGraph>();

        if (options.AutoLinkOnIngest)
            services.AddSingleton<DocumentLinkExtractor>();

        // Decorate the registered IKnowledgeStore
        services.Decorate<IKnowledgeStore>((inner, sp) =>
            new LinkedKnowledgeStore(
                inner,
                sp.GetRequiredService<IDocumentLinkGraph>(),
                options.SearchOptions));

        return services;
    }
}

public sealed class KnowledgeLinkingOptions
{
    public bool AutoLinkOnIngest { get; set; } = true;
    public LinkedSearchOptions SearchOptions { get; set; } = new();
    public float SimilarityThreshold { get; set; } = 0.7f;
}
```

### Why this is safe and the empirical equivalent is not

| Concern | `IKnowledgeStore` (documents) | `IEmpiricalMemory` (agent learnings) |
|---|---|---|
| Link target authoritativeness | Authored by humans, reviewed before ingestion | Generated by the agent, may be wrong |
| Feedback loop | None — ingestion is human-initiated | Self-reinforcing — agent commits and recalls its own entries |
| Worst case of a bad link | Irrelevant chunk surfaces in search results | Wrong pattern becomes structurally entrenched, resists contradiction |
| Correction mechanism | Human re-indexes or removes the document | Must be automated via decay, contradiction, exploration |
| Preferential attachment risk | Low — link count doesn't affect ingestion decisions | High — more-linked entries surface more, get linked more |

---

## Decision

**Adopt for document organization (`IKnowledgeStore`). Reject for empirical memory
(`IEmpiricalMemory`).** The same architectural patterns have fundamentally different
risk profiles depending on which memory layer they target.

Ars Contexta is a well-researched system for **replicating and preserving a human
mental model as a persistent agent context.** Its patterns are valuable when the
source material is authoritative — documents, procedures, runbooks — and the goal
is navigability, not truth-discovery.

Ananke's empirical memory has a fundamentally different epistemological commitment:
**discovering ground truth through observation, reinforcement, contradiction, and
decay.** Unconditional Reweave — the most novel pattern in Ars Contexta — would
create structural entrenchment that resists the correction mechanisms empirical
memory depends on.

### Adopt for `IKnowledgeStore` (document organization)

| Pattern | Action | Mechanism |
|---|---|---|
| Cross-document linking (Reweave) | **Adopt** — optional decorator | `LinkedKnowledgeStore` wrapping any `IKnowledgeStore` |
| Graph-augmented search | **Adopt** — via decorator | `IDocumentLinkGraph` traversal merged with vector results |
| LLM-classified relationships | **Adopt** — optional factory | `DocumentLinkExtractor` (post-ingestion, like `CatalogKeywordExtractor`) |
| Three-space separation | **Adopt** — add `ISessionState` | Separate operational state from knowledge stores |
| Fresh context per phase | **Adopt** — apply to `OfflineLearner` | Empirically supported attention-quality improvement |
| MOC-style indexes | **Consider** — catalog enrichment | Extend `IKnowledgeCatalog` with cluster-based summaries |
| Research annotations in docs | **Adopt** — editorial | Enrich guides 15 and 15a with cognitive grounding |

### Reject for `IEmpiricalMemory` (agent-learned knowledge)

| Pattern | Action | Rationale |
|---|---|---|
| Unconditional Reweave | **Reject** | Creates preferential attachment → confirmation bias → structural resistance to contradiction |
| Graph-augmented recall | **Reject** | Without empirical-standing gates, expanding recall through links would surface entrenched-but-wrong entries |
| Identity-preserving `self/` space | **Reject** | Ananke agents should adapt to evidence, not preserve a fixed mental model |

### If empirical inter-entry linking is ever revisited

Any future proposal for inter-entry linking in `IEmpiricalMemory` must satisfy:

1. **Links are gated on empirical standing** — both endpoints must have
   `Confidence > MinConfidence` and at least one reinforcement.
2. **Links decay** — link weight tracks `min(confidence_A, confidence_B)` and
   disappears when either endpoint is deleted by `DeletionThreshold`.
3. **Contradiction propagates** — when an entry is contradicted, its outbound
   links are weakened proportionally.
4. **No preferential attachment** — link creation must not use existing link
   count as a factor. Only semantic similarity + empirical standing.
5. **Exploration is not biased by connectivity** — `OfflineLearner` curiosity
   selection must remain variance-driven, not degree-driven.

---

## References

- Clark, A. & Chalmers, D. (1998). *The Extended Mind.* Analysis, 58(1), 7-19.
- Collins, A.M. & Loftus, E.F. (1975). *A Spreading-Activation Theory of Semantic Processing.* Psychological Review, 82(6), 407-428.
- Bjork, R.A. (1994). *Memory and Metamemory Considerations in the Training of Human Beings.* In Metcalfe & Shimamura (Eds.), Metacognition.
- Bjork, R.A. & Bjork, E.L. (1992). *A New Theory of Disuse and an Old Theory of Stimulus Fluctuation.* In Healy et al. (Eds.), From Learning Processes to Cognitive Processes.
- Forte, T. (2022). *Building a Second Brain.* Atria Books.
- Leroy, S. (2009). *Why Is It So Hard to Do My Work?* Organization Science, 20(2), 339-352.
- Liu, N.F. et al. (2023). *Lost in the Middle: How Language Models Use Long Contexts.* arXiv:2307.03172.
- Luhmann, N. (1992). *Communicating with Slip Boxes.* In Kieserling (Ed.), Universität als Milieu.
- Mark, G., Gudith, D. & Klocke, U. (2008). *The Cost of Interrupted Work: More Speed and Stress.* CHI '08.
- Matuschak, A. (2019). *Evergreen Notes.* notes.andymatuschak.org.
- Miller, G.A. (1956). *The Magical Number Seven, Plus or Minus Two.* Psychological Review, 63(2), 81-97.
- Nader, K., Schiller, D. & LeDoux, J.E. (2000). *Fear Memories Require Protein Synthesis in the Amygdala for Reconsolidation After Retrieval.* Nature, 406, 722-726.
- Pauk, W. (1950). *How to Study in College (Cornell Note-Taking System).* Cornell University.
- Roediger, H.L. & Butler, A.C. (2011). *The Critical Role of Retrieval Practice in Long-Term Retention.* Trends in Cognitive Sciences, 15(1), 20-27.
- Rosch, E. (1975). *Cognitive Representations of Semantic Categories.* Journal of Experimental Psychology: General, 104(3), 192-233.
- Slamecka, N.J. & Graf, P. (1978). *The Generation Effect: Delineation of a Phenomenon.* Journal of Experimental Psychology: Human Learning and Memory, 4(6), 592-604.
- Tulving, E. (1972). *Episodic and Semantic Memory.* In Tulving & Donaldson (Eds.), Organization of Memory.
- Tulving, E. (1985). *Memory and Consciousness.* Canadian Psychology, 26(1), 1-12.
- Watts, D.J. & Strogatz, S.H. (1998). *Collective Dynamics of 'Small-World' Networks.* Nature, 393, 440-442.
- Barabási, A.-L. & Albert, R. (1999). *Emergence of Scaling in Random Networks.* Science, 286(5439), 509-512.
- Nickerson, R.S. (1998). *Confirmation Bias: A Ubiquitous Phenomenon in Many Guises.* Review of General Psychology, 2(2), 175-220.
- Pariser, E. (2011). *The Filter Bubble: How the New Personalized Web Is Changing What We Read and How We Think.* Penguin.
