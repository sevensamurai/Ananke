# Connect 4 — Empirical Learning Demo

A Connect 4 game where the agent starts knowing **only the rules** (legal moves, win
detection) and learns strategy while playing with users. No LLM, no API keys, no Docker — just
`dotnet run` and play.

## What this demonstrates

This demo showcases how an agent could use a parallel mechanism to learn from experience. 

```
play → analyze → commit → recall → play better
```

| Framework feature | How the demo uses it |
|---|---|
| `StateMachine<S, T>` | Game flow: `Idle → Playing → Analyzing → Idle` |
| `OnInsight<T>` | Game analyzer signals discoveries; handler buffers during play, displays between games |
| `SignalInsightAsync` | Post-game analysis pushes insights through the state machine |
| `InMemoryEmpiricalMemory` | Stores all learned patterns, skills, and heuristics |
| Composite scoring | Agent recalls the most relevant, confident, recent experience before each move |
| Semantic dedup | Similar insights merge instead of duplicating (e.g. repeated "block 3-in-a-row" observations) |
| Reinforcement | Patterns that contribute to wins get stronger; losers get weaker |

## Run it

```bash
cd src/demos/Connect4Demo
dotnet run
```

## What to observe

### Games 1–2: Random play
The agent plays randomly (aside from obvious win/block moves). It loses fast.

### Games 3–4: Defensive awareness
After losing, the analyzer discovers blocking patterns. You'll see:
```
✨ Pattern: "Opponent had 3 in a line with an open playable cell — must block immediately"
```
The agent now blocks your threats.

### Games 5–6: Center control
Statistical analysis reveals that center column dominance correlates with wins:
```
💡 Heuristic: "Prefer center column in early moves"
```
The agent starts contesting the center.

### Games 7+: Offensive play
When the agent wins, it learns offensive skills:
```
🎯 Skill: "Build offensive pressure by placing pieces in adjacent columns"
```
The agent transitions from reactive to proactive.

## Memory inspection

Press `m` during your turn to inspect the agent's empirical memory:

```
┌─────────────── empirical memory ───────────────┐
│ 🔍 [Pattern] Opponent had 3 in a line...
│    confidence: 0.82 | observations: 4 | score: 0.712
│ 💡 [Heuristic] Prefer center column in early moves
│    confidence: 0.68 | observations: 3 | score: 0.589
│ 🎯 [Skill] Build offensive pressure by placing...
│    confidence: 0.45 | observations: 1 | score: 0.321
└──────────────────────────────────────────────────┘
```

## Three experience kinds in action

| Kind | Example from play |
|---|---|
| **Pattern** | _"Opponent has 3 in a line with an open cell → must block"_ |
| **Skill** | _"Build offensive pressure: extend lines, create shared threats, force reactive play"_ |
| **Heuristic** | _"Prefer center column — it participates in the most winning lines"_ |

## Swapping to Qdrant for persistence

To persist learned experience across sessions, replace the memory setup in
`Program.cs`:

```csharp
// Before (in-memory, resets each run):
var embedder = new InMemoryEmbedder();
var memory = new InMemoryEmpiricalMemory(embedder);

// After (Qdrant, persists across runs):
var qdrantClient = new QdrantClient("localhost", 6334);
var embedder = new OpenAIEmbeddingModel(apiKey, "text-embedding-3-small");
var memory = new QdrantEmpiricalMemory(qdrantClient, embedder);
```

The agent will remember everything from previous sessions and start each run
with the accumulated knowledge.

---

## Guided Challenge: Beyond the Board

Connect 4 is a toy problem — but the learning loop underneath it is not. The
same `play → analyze → commit → recall → play better` cycle can power systems
that deal with messy, real-world signals.

### Think about this

Imagine an on-call engineer investigating a production incident. They sift
through **logs**, **traces**, **error emails**, and **dashboards** — slowly
building a mental model of what went wrong. Two months later a similar symptom
appears and they start from scratch because the insight was never captured.

What if the system *learned* the way the Connect 4 agent does?

| Connect 4 concept | Real-world equivalent |
|---|---|
| A finished game | A resolved incident |
| Board positions & moves | Logs, traces, emails, metrics |
| Post-game analyzer | A process that reviews incident artifacts after resolution |
| **Pattern** — _"opponent has 3 in a row → block"_ | **Pattern** — _"OOM spike + deployment within 2 h → likely memory-leak regression"_ |
| **Skill** — _"create a fork with two open threes"_ | **Skill** — _"correlate pod restart times with upstream deploy timestamps to narrow scope"_ |
| **Heuristic** — _"prefer center column early"_ | **Heuristic** — _"check the most-recently-changed service first — it's the root cause ~60 % of the time"_ |
| Composite scoring (relevance × confidence × recency) | Surfacing the most applicable past insight for the *current* incident |
| Reinforcement after win/loss | Strengthening patterns that led to fast resolution; weakening red herrings |
| Semantic dedup | Merging near-duplicate observations across incidents instead of hoarding noise |

### The building blocks you already have

Everything the Connect 4 demo uses is a general-purpose framework primitive:

- **`StateMachine<S, T>`** — model any workflow with states and triggers
  (incident lifecycle, CI pipeline, review flow).
- **`OnInsight<T>` / `SignalInsightAsync`** — decouple discovery from
  consumption; buffer insights and surface them at the right moment.
- **`InMemoryEmpiricalMemory`** (or Qdrant) — store, query, and
  reinforce empirical knowledge with semantic search.
- **Composite scoring** — rank recalled experience by relevance, confidence,
  and recency so the most useful memory wins.
- **Semantic dedup** — keep the memory store clean as observations accumulate.

### Your challenge

Pick a domain you know well — incident response, log analysis, code review,
test-failure triage — and sketch out how you would wire these same building
blocks into a learning agent for that domain:

1. **What is a "game"?** — What constitutes a completed episode that can be
   analyzed? (e.g., a resolved incident, a merged PR, a triaged alert)
2. **What are the "moves"?** — What raw signals does the agent observe?
   (e.g., log lines, trace spans, email threads, metric series)
3. **What does the analyzer produce?** — What patterns, skills, or heuristics
   would emerge from reviewing those signals after resolution?
4. **How does reinforcement work?** — What outcome tells the system a learned
   insight was *useful* vs. a red herring?

Then try building it. The framework gives you the primitives — the domain
knowledge is yours. 🚀
