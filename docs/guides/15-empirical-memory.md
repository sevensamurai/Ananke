<!-- topic: empirical-memory, tags: empirical-memory, patterns, skills, heuristics, learning, confidence, qdrant -->
# 15 — Empirical Memory

Give agents a third memory layer that accumulates **patterns, skills, and heuristics
learned from repeated interactions** — alongside the semantic knowledge store
(Guide 06) and episodic conversation memory.

**Demo:** [Connect4Demo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/Connect4Demo)

---

## Why a third memory layer?

| Layer | What it stores | How it's populated |
|---|---|---|
| Semantic (`IKnowledgeStore`) | Document chunks — "what the docs say" | Ingestion pipeline, agent tools |
| Episodic (`IConversationMemory`) | Conversation turns — "what was said" | Automatically by `StreamingChatWorkflow` |
| **Empirical (`IEmpiricalMemory`)** | **Observations, procedures, rules of thumb — "what the agent has learned"** | **Agent tools or post-session analysis** |

Empirical memory is **mutable** by design: entries gain confidence when confirmed and
lose it when contradicted, without ever being deleted.

---

## Core Types

```csharp
// The three kinds of empirical knowledge
public enum EmpiricalKind
{
    Pattern,    // "when X happens, Y follows"
    Skill,      // "how to do X" — ordered steps, tools, expected outcome
    Heuristic   // "prefer X over Y in situation Z"
}

// A single entry — kind determines which optional fields are populated
var entry = new EmpiricalEntry
{
    Id = Guid.NewGuid().ToString("N"),
    Kind = EmpiricalKind.Pattern,
    Description = "GC pause > 200ms causes downstream timeout spikes",
    Condition = "ServiceA GC pause exceeds 200ms",
    Effect = "ServiceB timeout rate spikes within 30 seconds",
    Tags = ["gc", "performance", "timeout"],
    Source = "incident-analysis",
    Confidence = 0.7f,
    ObservationCount = 3,
    Evidence = ["incident-42", "incident-67"],
    FirstObserved = DateTimeOffset.UtcNow,
    LastObserved = DateTimeOffset.UtcNow
};
```

---

## `IEmpiricalMemory` Interface

```csharp
// Store a new entry — or reinforce an existing similar one (semantic dedup)
EmpiricalEntry committed = await memory.CommitAsync(entry);

// Search by situation — returns entries ranked by relevance × confidence × recency
IReadOnlyList<EmpiricalMatch> matches = await memory.RecallAsync(
    "ServiceA is experiencing high latency",
    new RecallOptions { TopK = 5, MinConfidence = 0.3f });

// Confirm a recalled entry was correct — increments confidence
await memory.ReinforceAsync(matches[0].Entry.Id, new Reinforcement
{
    NewEvidence = ["incident-89: pattern confirmed"],
    ConfidenceAdjustment = 0.1f,
    Source = "human-confirmed"
});

// Mark an entry as wrong — reduces confidence without deleting
await memory.ContradictAsync(entryId, reason: "pattern did not repeat in staging");
```

**Composite scoring** — `RecallAsync` ranks results by:
```
score = vectorSimilarity × confidence × recencyWeight
```
Recent, high-confidence, relevant entries surface first. Contradicted entries naturally
sink to the bottom as their confidence drops.

---

## Giving Agents Empirical Memory Tools

`EmpiricalMemoryTools.Create` wraps an `IEmpiricalMemory` in a `ToolKit` with three
agent-callable tools:

| Tool | What the agent does |
|---|---|
| `recall_empirical` | Search memory before acting — "what do I already know about this?" |
| `commit_insight` | Store a newly discovered pattern, skill, or heuristic |
| `reinforce_empirical` | Confirm a recalled entry that proved correct |

```csharp
var memoryTools = EmpiricalMemoryTools.Create(memory);

await StreamingChatWorkflow.Create("analyst", model)
    .WithSystemPrompt("""
        You are an incident analyst. Before investigating, recall relevant patterns.
        After resolving, commit what you learned and reinforce patterns that held.
        """)
    .WithTools(memoryTools)
    .OnTextDelta(delta => Console.Write(delta))
    .RunAsync(messages);
```

The agent decides autonomously when to recall, commit, and reinforce — no orchestration
code required.

---

## Backends

### `InMemoryEmpiricalMemory` — dev, test, single-process

```csharp
var embedder = new InMemoryEmbedder();           // or OpenAIEmbeddingModel
var memory = new InMemoryEmpiricalMemory(embedder,
    dedupThreshold: 0.9f,                        // cosine similarity for dedup
    decayOptions: new TimeDecayOptions { HalfLifeDays = 90, FloorWeight = 0.3f });
```

Suitable for tests and demos. Brute-force cosine similarity — linear scan over all entries.

### `QdrantEmpiricalMemory` — persistent, distributed

```csharp
var memory = new QdrantEmpiricalMemory(
    qdrantClient, embeddingModel,
    collectionName: "empirical_memory",    // default
    vectorSize: 1536,                      // must match embedding model
    dedupThreshold: 0.9f);
```

Entries are stored as Qdrant points. `ReinforceAsync` and `ContradictAsync` use
`SetPayloadAsync` to update confidence without re-embedding — the vector stays stable
across reinforcements.

---

## The Connect4 Demo — Learning Through Play

`Connect4Demo` illustrates the full empirical memory loop with zero LLM calls.

```
play → analyze → commit → recall → play better
```

The agent starts knowing only the rules (legal moves + win detection). After each
game, `GameAnalyzer` inspects the board and commits insights:

```csharp
// Loss: opponent controlled center — commit a heuristic
await memory.CommitAsync(new EmpiricalEntry
{
    Kind = EmpiricalKind.Heuristic,
    Description = "Prefer center column in early moves — it participates in the most winning lines",
    Tags = ["opening", "center"],
    Source = "game-analysis",
    Confidence = 0.35f,
    ...
});

// Win: reinforce patterns that contributed
foreach (var match in recalled)
    await memory.ReinforceAsync(match.Entry.Id, reinforcement);
```

On the next turn, the agent calls `recall_empirical` before choosing a column:

```csharp
var recalled = await memory.RecallAsync(
    DescribeSituation(board),
    new RecallOptions { TopK = 5, MinConfidence = 0.2f });

// Score columns by recalled heuristics and patterns
foreach (var match in recalled)
    ApplyHeuristicScore(match, ref scores);
```

No ML, no LLM — pure empirical learning from structured game analysis.

---

## RecallOptions

```csharp
var options = new RecallOptions
{
    TopK = 10,                            // max results
    Kind = EmpiricalKind.Pattern,         // filter to one kind (null = all)
    MinConfidence = 0.4f,                 // exclude low-confidence entries
    ScoreThreshold = 0.1f,               // exclude low composite score
    RequiredTags = ["production", "gc"]   // must have all specified tags
};
```


---

## Observability — Monitoring Whether Learning Works

Both `InMemoryEmpiricalMemory` and `QdrantEmpiricalMemory` emit metrics, traces,
and structured logs through the standard .NET observability APIs. No extra packages
— just subscribe to the `Ananke.EmpiricalMemory` meter and activity source.

### Metrics (`System.Diagnostics.Metrics`)

| Counter | What it tells you |
|---|---|
| `empirical.commits` | New entries stored. Rising over time = the agent is discovering patterns. |
| `empirical.dedup_merges` | Entries merged via semantic dedup. High ratio to commits = the agent keeps rediscovering the same thing (may need broader analysis). |
| `empirical.recalls` | Total recall queries issued. |
| `empirical.recall_hits` | Queries that returned ≥1 result. `hits / recalls` = **hit rate** — the core "is this useful?" metric. |
| `empirical.reinforcements` | Entries confirmed correct. Rising = knowledge is being validated. |
| `empirical.contradictions` | Entries marked incorrect. Healthy systems have some contradictions — it means bad knowledge is being pruned. |

**Key ratios to watch:**

```
Hit rate        = empirical.recall_hits / empirical.recalls
Dedup rate      = empirical.dedup_merges / (empirical.commits + empirical.dedup_merges)
Learning rate   = empirical.commits over time (should grow then plateau)
Validation rate = empirical.reinforcements / empirical.commits
```

### Distributed traces (`System.Diagnostics.ActivitySource`)

Both implementations emit spans under the `Ananke.EmpiricalMemory` source:

| Span | Tags |
|---|---|
| `EmpiricalMemory.Commit` | `empirical.entry_id`, `empirical.kind` |
| `EmpiricalMemory.Recall` | `empirical.recall_count` |

These nest inside workflow traces automatically when used with `ActivitySourceTracer`,
so you can see recall latency and commit activity in context alongside LLM calls and
tool executions in Jaeger, Grafana Tempo, or any OTLP-compatible backend.

### Structured logging (`ILogger`)

Pass an `ILogger` to the constructor for debug-level diagnostics:

```csharp
var memory = new InMemoryEmpiricalMemory(embedder,
    logger: loggerFactory.CreateLogger<InMemoryEmpiricalMemory>());
```

Log messages include:
- **Commit**: kind, ID, initial confidence
- **Dedup merge**: new ID, existing ID, similarity score
- **Recall**: query text, result count
- **Reinforce**: entry ID
- **Contradict**: entry ID, reason

### Wiring into OpenTelemetry

```csharp
// Traces
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Ananke.EmpiricalMemory"));

// Metrics
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Ananke.EmpiricalMemory"));
```

### Checking health in production

1. **Is the agent learning?** — `empirical.commits` should increase during active sessions.
2. **Is knowledge being validated?** — `empirical.reinforcements > 0`; check that key entries show rising confidence via `GetAsync`.
3. **Is recall useful?** — hit rate (`recall_hits / recalls`) above ~0.3 means the memory is contributing to decisions. Below that, entries may not match how the agent phrases situations.
4. **Is dedup preventing bloat?** — a healthy dedup rate means the semantic threshold is working. If it's 0, the agent may be storing many near-duplicates.
5. **Are bad patterns being pruned?** — `empirical.contradictions > 0` is expected. If it stays at 0, the agent may be blindly trusting early hypotheses.

---

## What to read next

- [Guide 15a — Empirical Memory Tuning](15a-empirical-memory-tuning.md) — fine-tuning `AffectOptions` and `OfflineLearnerOptions` for different domains
- [Guide 06 — Long-Term Memory](06-memory.md) — the semantic knowledge layer this complements
- [Guide 08 — State Machine](08-state-machine.md) — `IStateMachine<S,T>` for coordinating the analysis loop
- [Guide 14 — Testing](14-testing.md) — use `InMemoryEmpiricalMemory` + `InMemoryEmbedder` for zero-dependency tests
- [Guide 16 — Agentic Patterns § Smart Tool Router](16-agentic-patterns.md#smart-tool-router) — `IToolMemory` powers the `SemanticRecallStage` and inflammation advisories in the routing pipeline

**Also see:**
- [EntityMemoryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/EntityMemoryDemo) — per-entity memory isolation; same workflow, different empirical + knowledge stores per customer
- [LearningPrimitivesDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/LearningPrimitivesDemo) — OpenClaw skill pipeline and UCB-based adaptive routing evolution in isolation
- [SelfImprovingWorkflowDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/SelfImprovingWorkflowDemo) — a workflow that records its own performance and uses empirical memory to refine its strategy across runs

← [Learning Path](learning-path.md)
