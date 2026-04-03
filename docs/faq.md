# Ananke — Frequently Asked Questions

This page answers the most common questions about Ananke.
For a hands-on walkthrough, start at [Getting Started](guides/01-getting-started.md).
For a complete capability reference, see the [Feature Index](reference/features.md).

---

## Contents

- [General](#general)
- [Installation](#installation)
- [LLM Providers](#llm-providers)
- [Workflows](#workflows)
- [State Machine](#state-machine)
- [Memory & Knowledge (RAG)](#memory--knowledge-rag)
- [Empirical Memory & Agent Learning](#empirical-memory--agent-learning)
- [Skill Package Export/Import](#skill-package-exportimport)
- [External Skill Catalog](#external-skill-catalog)
- [Human-in-the-Loop](#human-in-the-loop)
- [MCP & Interoperability](#mcp--interoperability)
- [Testing](#testing)
- [Observability](#observability)
- [Design & Architecture](#design--architecture)

---

## General

### What is Ananke?

Ananke is a vendor-agnostic, production-ready .NET framework for building AI agents and
automated multi-step pipelines. It provides:

- **Typed workflow orchestration** — directed graphs of jobs with compile-time state safety
- **LLM agent integration** — tool calling, structured output, token-level streaming
- **Multi-provider AI model support** — OpenAI, Anthropic, Google Gemini, and any
  OpenAI-compatible endpoint
- **Long-term memory** — document ingestion (RAG), knowledge catalog, empirical learning
- **Human-in-the-loop** — pause, checkpoint, and resume workflows with human review
- **Distributed coordination** — Redis distributed locking, MQTT pub/sub, agent handoff
- **OpenTelemetry observability** — automatic spans for workflows, state machines, and LLM calls
- **MCP & A2A interoperability** — expose tools/workflows as MCP servers, consume MCP tools,
  use the A2A agent protocol

### Who is Ananke for?

Ananke is designed for .NET developers (C# 12+, .NET 10) building production AI systems.
It is suitable for:

- Streaming chat agents and AI assistants
- Document Q&A systems (RAG pipelines)
- Multi-step agentic task pipelines
- State-machine-driven conversation flows
- Distributed multi-service agentic architectures
- Any system where AI agents need to call tools, remember context, or coordinate across processes

### What .NET version is required?

Ananke targets **.NET 10**.

### Is Ananke production-ready?

Yes. Ananke is designed with production requirements first:

- All state is typed end-to-end — the compiler enforces correctness
- Every infrastructure contract (`IDistributedLock`, `IKnowledgeStore`, `ICheckpointStore`,
  `IConversationMemory`) has a well-defined interface and a zero-config in-memory implementation
  for testing
- Distributed coordination uses Redis RedLock (via `Ananke.Redis`)
- LLM calls have automatic 429 retry with exponential backoff and OTel reporting
  (`ResilientAgentModel`)
- LLM response caching is built in (`CachingAgentModel`)
- Polly integration provides circuit breakers and custom resilience pipelines
- OpenTelemetry tracing is emitted automatically for workflows, state transitions, and tool calls

### Is Ananke open source?

Yes. Ananke is licensed under the [Apache 2.0 License](../LICENSE).

---

## Installation

### How do I install Ananke?

Install the meta-package to get everything:

```bash
dotnet add package Ananke
```

Or install only the packages you need:

```bash
dotnet add package Ananke.Orchestration            # core: workflows, agents, tools, knowledge
dotnet add package Ananke.Orchestration.OpenAI     # OpenAI chat + embeddings provider
dotnet add package Ananke.Documents                # PDF + Markdown document extraction
dotnet add package Ananke.OpenTelemetry            # OTLP distributed tracing
```

### What is the minimal install for a streaming chat agent?

```bash
dotnet add package Ananke.Orchestration
dotnet add package Ananke.Orchestration.OpenAI
```

### What is the minimal install for a document Q&A (RAG) pipeline?

```bash
dotnet add package Ananke.Orchestration
dotnet add package Ananke.Orchestration.OpenAI   # chat + embeddings
dotnet add package Ananke.Documents              # PDF + Markdown extraction
```

### How many packages are there?

Ananke is split into focused NuGet packages so you only take the dependencies you need.
The full list is in the [README packages table](../README.md#packages) and the
[Feature Index](reference/features.md).

---

## LLM Providers

### Which LLM providers does Ananke support?

| Provider | Package | Example models |
|---|---|---|
| OpenAI | `Ananke.Orchestration.OpenAI` | GPT-4.1, GPT-4o, o1, o3, text-embedding-3-small/large |
| Anthropic | `Ananke.Orchestration.Anthropic` | Claude Sonnet, Claude Haiku, Claude Opus |
| Google Gemini | `Ananke.Orchestration.Google` | Gemini 2.5 Pro, Gemini Flash |
| Any OpenAI-compatible | `Ananke.Orchestration.OpenAI` | Ollama, LM Studio, vLLM, Azure OpenAI, Groq, Deepseek, Together AI |

### Does Ananke support Ollama (local models)?

Yes. Use `OpenAIChatAgentModel` with a custom `baseUri` pointing to your Ollama server.
See [Advanced Agent Features](guides/11-advanced-agents.md) for the exact configuration.

### Does Ananke support Azure OpenAI?

Yes. Azure OpenAI exposes an OpenAI-compatible API. Configure `OpenAIChatAgentModel` with
your Azure endpoint URL and API key. See [Advanced Agent Features](guides/11-advanced-agents.md).

### Can I use multiple LLM providers in the same workflow?

Yes. Each `AgentJob` takes its own `IStreamingAgentModel`, so different jobs in the same
workflow can use different providers or models. `CapabilityModelRouter` lets you route
requests to models based on declared capabilities (e.g., vision support, context window size,
reasoning tier).

### Can I swap providers without changing my workflow?

Yes. Workflows, state types, tool definitions, and routing rules are all expressed in terms of
Ananke's own interfaces — not any provider's SDK. Switching from one provider to another is a
one-line configuration change.

### What is `IStreamingAgentModel`?

`IStreamingAgentModel` is Ananke's provider-agnostic interface for LLM interaction. All
provider implementations (`OpenAIChatAgentModel`, `AnthropicAgentModel`, `GoogleAgentModel`,
`A2AAgentModel`) implement this interface. You can also implement it yourself to wrap any
model or API.

---

## Workflows

### What is a workflow in Ananke?

A workflow is a directed graph of **jobs** connected by edges. Each job receives a typed
state object, performs work (optionally calling an LLM, tools, or external services), and
returns a new state. The graph is validated at build time — invalid topologies (disconnected
nodes, missing edges) fail before the workflow ever runs.

```csharp
var workflow = new Workflow<MyState>("my-workflow")
    .Job("step-a", async (state, ct) => state with { A = "done" })
    .Job("step-b", async (state, ct) => state with { B = "done" })
    .Chain("step-a", "step-b")
    .Then("step-b", Workflow.End);

var result = await workflow.RunAsync(new MyState());
```

### What routing patterns are supported?

| Pattern | API |
|---|---|
| Linear chain | `.Chain("a", "b", "c")` |
| Conditional branching | `.Decide(state => ...)` with a lambda returning the next job name |
| LLM-driven routing | `.DecideWithAgent(...)` — the model picks the next step |
| Fork / Join | Fan-out to parallel branches, fan-in with a merge function |
| Sub-workflows | `.SubFlow(innerWorkflow)` — nest a complete workflow inside a parent |
| Agentic patterns | `AgenticPattern.ReviewCritique<T>()`, `AgenticPattern.IterativeRefinement<T>()` |

### Can I stream workflow events in real time?

Yes. `workflow.BuildStream(initialState)` returns an `IAsyncEnumerable<WorkflowEvent>` that
you can forward over Server-Sent Events (SSE) to a web client. See
[Streaming Chat](guides/05-streaming-chat.md) and [Workflows](guides/02-workflows.md).

### Does Ananke validate the workflow graph at build time?

Yes. Calling `.Build()` (or running the workflow) validates the topology. Invalid configurations
— disconnected nodes, missing terminal edge, duplicate job names — throw at build time,
not at runtime.

### What are Agentic Patterns?

`AgenticPattern` is a higher-level builder that pre-wires recognized agentic design patterns on
top of the `Workflow<TState>` primitives:

- **Review & Critique** — generator agent → critic agent → loop until approved or max iterations
- **Iterative Refinement** — single agent refinement loop until quality threshold

More patterns (Map-Reduce, Swarm, etc.) will be added in future releases.

---

## State Machine

### What is the difference between a Workflow and a State Machine?

| | Workflow | State Machine |
|---|---|---|
| **Model** | Directed pipeline — runs start to finish | Long-lived entity — stable states + event-driven transitions |
| **Best for** | Task pipelines, document processing, batch jobs, agentic tasks | Conversation sessions, order lifecycle, device management, anything with ongoing status |
| **Trigger** | Started explicitly with `.RunAsync()` | Driven by external events via `.TransitionAsync()` |
| **Composition** | Can contain sub-workflows | Can invoke workflows as jobs via the Bridge layer |

Both compose: a state machine can invoke a `Workflow<T>` as part of a transition, and a
workflow can interact with a state machine.

### Does the state machine support distributed coordination?

Yes. `AbstractStateMachine` uses `IDistributedLock` to ensure safe concurrent transitions across
multiple service instances. In production, use `RedisDistributedLock` (via `Ananke.Redis`
with RedLock.net). In tests, use the zero-config `InMemoryDistributedLock`.

### What is the middleware pipeline?

`IJobMiddleware<T>` lets you intercept every state transition for logging, metrics, validation,
or custom business rules. Middleware chains compose cleanly and are applied in order.

### What is circuit breaking?

When `AbstractStateMachine.OperationalStatus` is set to `Faulted`, all transitions are blocked
until `ResetAsync()` is called. This prevents cascading failures in distributed systems.

---

## Memory & Knowledge (RAG)

### How does Ananke handle long-term memory?

Ananke provides three complementary memory layers:

| Layer | What it stores | Interface |
|---|---|---|
| **Semantic** | Document chunks with vector embeddings (RAG) | `IKnowledgeStore` |
| **Episodic** | Conversation history per session | `IConversationMemory` |
| **Empirical** | Patterns, skills, and heuristics learned from interactions | `IEmpiricalMemory` |

### What is RAG and does Ananke support it?

RAG (Retrieval-Augmented Generation) gives agents access to a searchable knowledge base built
from your own documents. Ananke's ingestion pipeline:

1. **Extract** — parse PDFs and Markdown into normalized text (`Ananke.Documents`)
2. **Chunk** — split with heading-aware sliding windows and configurable overlap
3. **Embed** — generate vector embeddings via `IEmbeddingModel` (OpenAI text-embedding-3)
4. **Store** — index in `IKnowledgeStore` for semantic vector search

Agents search it with `KnowledgeSearchTool` and can ingest new documents via `KnowledgeTools`.

### What vector databases are supported?

| Store | Package | Notes |
|---|---|---|
| In-memory | `Ananke.Orchestration` | Zero-config, for dev and testing |
| Qdrant | `Ananke.Qdrant` | Persistent, distributed, production-ready |

Additional providers can be added by implementing `IKnowledgeStore`.

### What is the Knowledge Catalog?

The knowledge catalog tracks document-level metadata: source, title, LLM-enriched keywords,
categories, and summaries. It supports time-decay reranking — a configurable half-life and
floor weight deprioritize older documents so recent information surfaces first.

---

## Empirical Memory & Agent Learning

### What is empirical memory?

Empirical memory is a third memory layer where agents accumulate structured knowledge from
repeated interactions. There are three kinds:

| Kind | Meaning | Example |
|---|---|---|
| `Pattern` | Observed regularities | "When GC pause exceeds 200ms, downstream timeout spikes follow within 30s" |
| `Skill` | Step-by-step procedures | "How to investigate a latency spike: check GC, then thread pool, then DB connections" |
| `Heuristic` | Rules of thumb | "Prefer async I/O over sync for all outbound calls in this service" |

Each entry carries a **confidence score** that increases on reinforcement and decreases on
contradiction — without ever deleting the entry.

### Can agents learn and improve over time?

Yes. The learning loop works as follows:

1. **Commit** — during agent execution, `EmpiricalMemoryTools` lets the LLM store newly
   discovered patterns, skills, and heuristics via `commit_insight`
2. **Recall** — before acting, the agent searches its memory via `recall_empirical` to
   surface relevant prior knowledge
3. **Reinforce** — when a recalled insight proves correct, the agent (or framework) calls
   `reinforce_empirical` to increase its confidence
4. **Contradict** — when an insight proves wrong, confidence decreases without deletion
5. **Offline sweep** — `IOfflineLearner` runs background cycles between sessions:
   decaying stale beliefs, exploring low-confidence entries via `ISimulationSource`,
   discovering connections across the full memory corpus
6. **Consolidate** — when a pattern reaches a confidence threshold, `IConsolidationSummarizer`
   promotes it into `IKnowledgeStore` as permanent knowledge

Raw LLM capability is the starting point; every deployment gets smarter over time.

### How does recall scoring work?

`RecallAsync` ranks results by a composite score:

```
score = vectorSimilarity × confidence × recencyWeight
```

Recent, high-confidence, relevant entries surface first. Entries that have been contradicted
many times naturally sink to the bottom as their confidence drops.

### What is the offline learner?

`IOfflineLearner` (implemented by `OfflineLearner` in `Ananke.Learning`) runs
background sweeps between active sessions:

- **Decay** — reduces strength of entries that haven't been confirmed recently
- **Curiosity exploration** — walks low-confidence entries to validate or contradict them
- **Simulation** — uses `ISimulationSource` to test hypotheses without real-world cost
  (self-play, Monte Carlo rollouts, scenario replay)
- **Consolidation** — promotes mature, high-confidence patterns to `IKnowledgeStore` via
  `IConsolidationSummarizer`

### What is the episode store?

`IEpisodeStore` (implemented by `InMemoryEpisodeStore`) records completed **episodes** —
ordered trajectories of agent decisions linked to a terminal reward. Each `Episode`
contains a sequence of `EpisodeStep` entries (each pointing to an `EmpiricalEntry`)
and the terminal reward received at the end. Episodes enable:

- **Monte Carlo reward propagation** — discounted returns are computed backward through the
  trajectory and used to reinforce every empirical entry proportional to its causal contribution
- **Skill packaging** — episodes are bundled with exported skill packages so the
  receiving agent has the training trajectories, not just the conclusions

```csharp
var episode = new Episode
{
    Id    = Guid.NewGuid().ToString("N"),
    Steps = [new EpisodeStep { StepIndex = 0, EntryId = entryId }],
    TerminalReward = 1.0f,   // win
    StartedAt      = DateTimeOffset.UtcNow,
    CompletedAt    = DateTimeOffset.UtcNow
};
await episodeStore.CommitAsync(episode);

// Propagate terminal reward backward through all steps
var propagator = new MonteCarloRewardPropagator();
await propagator.PropagateAsync(episode, memory);
```

### What is tag importance tracking?

`ITagImportanceTracker` (`TagImportanceTracker`) analyzes all empirical entries and
computes a `TagImportanceMap` — a normalized weight per semantic tag that reflects its
correlation with positive vs. negative outcomes:

```
importance(tag) = (positive_count - negative_count) / total_count  → normalized to [0, 1]
```

Tags that appear only in entries with positive valence score near 1.0; tags that appear
only in negative entries score near 0.0. The map is:
- Used to boost recall priority for entries with high-importance tags
- Bundled into exported skill packages so the receiving agent inherits learned feature weights
- Used by the offline learner to bias exploration toward high-importance dimensions

### What exploration strategies are available?

`IExplorationStrategy` controls the exploration–exploitation balance during action
selection. Two implementations are provided:

| Strategy | Class | When to use |
|---|---|---|
| **UCB1** | `UcbExplorationStrategy` | Principled exploration with uncertainty estimates. Balances score, visit count, and entry variance. Best for game agents and iterative planners. |
| **ε-greedy with annealing** | `EpsilonGreedyExplorationStrategy` | Simpler. Explores randomly with probability ε, exploits otherwise. ε decays over time so the agent shifts from exploration to exploitation as experience grows. |

```csharp
var ucb = new UcbExplorationStrategy(new ExplorationOptions
{
    ExplorationCoefficient = 1.414f,   // √2 is the standard UCB1 constant
    UseVarianceBonus       = true,     // add entry.Variance to exploration bonus
    VarianceBonusWeight    = 0.5f
});

var epsilon = new EpsilonGreedyExplorationStrategy(new ExplorationOptions
{
    EpsilonInitial = 0.3f,   // 30% random exploration at start
    EpsilonMin     = 0.05f,  // never drop below 5%
    EpsilonDecay   = 0.999f  // anneal slowly per selection
});
```

---

## Skill Package Export/Import

### What is a skill package?

A skill package is a portable, self-contained bundle of everything an agent has learned:

- **Empirical entries** — patterns, skills, and heuristics that passed the quality gates
  (min confidence, min strength, min observation count)
- **Episodes** — the training trajectories that produced those entries, so the
  receiving agent can re-run reward propagation if needed
- **`TagImportanceMap`** — learned feature weights showing which semantic tags
  correlate with positive outcomes in that domain
- **`TrainingManifest`** — provenance metadata: total entries, average reward,
  training duration, creation timestamp, and arbitrary statistics

Packages are streamed as JSON via `ISkillPackageFormat` / `JsonSkillPackageFormat`.

### How do I export a skill package?

```csharp
using Ananke.Learning.Skills;

var packager = new SkillPackager();
var format   = new JsonSkillPackageFormat();

await using var file   = File.OpenWrite("connect4-v1.skill.json");
await using var writer = format.CreateWriter(file);

// Optionally compute tag importance weights to bundle with the package
var tracker     = new TagImportanceTracker();
var importances = await tracker.ComputeAsync(memory);

var result = await packager.ExportAsync(
    new SkillExportOptions
    {
        Name        = "connect4-strategy",
        Domain      = "connect4",
        Version     = "1.0.0",
        Description = "Learned Connect 4 opening and mid-game strategy",
        MinConfidence   = 0.4f,    // only export confident entries
        MinStrength     = 0.3f,    // drop weak entries
        MinObservations = 3,       // at least 3 sightings
        IncludeEpisodes = true     // bundle training trajectories
    },
    memory,
    writer,
    episodes:      episodeStore,
    tagImportances: importances);

Console.WriteLine($"Exported {result.EntriesExported} entries, {result.EpisodesExported} episodes");
```

### How do I import a skill package?

```csharp
await using var file   = File.OpenRead("connect4-v1.skill.json");
var reader = await format.CreateReaderAsync(file);

var result = await packager.ImportAsync(
    reader,
    targetMemory,
    episodes: targetEpisodeStore,
    options: new SkillImportOptions
    {
        TrustScale = 0.7f   // scale down imported confidence — trust but verify
    });

Console.WriteLine($"Imported {result.Added} new, {result.Merged} merged, {result.Skipped} skipped");
```

The importer applies **trust scaling** to every imported entry’s confidence and
strength. Set `TrustScale` below 1.0 to let the receiving agent re-validate the
knowledge against its own environment before fully trusting it.

### Can I transfer skills between different agent domains?

Yes, with care. The quality gates on export and trust scaling on import are the
controls. For cross-domain transfer, set a lower `TrustScale` and run offline
learning sweeps so the receiving agent can reinforce, contradict, or decay the
imported entries based on its own experience.

### What merge semantics apply on import?

When an imported entry is semantically similar (above the dedup threshold) to an
existing entry, the packager merges rather than duplicates: evidence is combined,
and confidence is updated according to the import’s `TrustScale`. Entries below the
similarity threshold are added as new entries.

---

## External Skill Catalog

### What is the External Skill Catalog?

`Ananke.Skills` provides `ISkillCatalog` — a protocol-agnostic interface for
discovering, caching, and running tools from external registries. The first
implementation, `OpenClawCatalog`, integrates with the
[OpenClaw/ClawHub](https://clawhub.io) registry of CLI-based tools.

This is distinct from the _learned_ skills in `Ananke.Learning`. External skills are
discovered from a registry and run as CLI processes; learned skills are patterns,
heuristics, and procedures accumulated from the agent’s own experience.

### How do I discover and add catalog skills to an agent?

```bash
dotnet add package Ananke.Skills
```

```csharp
using Ananke.Skills;
using Ananke.Skills.OpenClaw;

// Create a catalog backed by a local cache directory
var catalog = new OpenClawCatalog(
    cacheDir: Path.Combine(AppContext.BaseDirectory, ".skill-cache"),
    enableVoting: true);   // auto up/down vote on success/failure

// Sync the remote registry once (or on a timer)
await catalog.SyncAsync();

// Discover and add matching skills to an agent’s ToolKit in one call
var tools = new ToolKit("research");
await tools.AddFromCatalogAsync(catalog, "airbnb search lodging", limit: 3);

// Use the toolkit in any AgentJob or StreamingChatWorkflow
```

After `SyncAsync()`, all subsequent `SearchAsync()` calls operate entirely offline
from the local cache.

### How does skill scoring and voting work?

`ISkillScoreStore` (implemented by `JsonFileScoreStore`) tracks local up/down votes
for each skill. When `enableVoting: true` is set on `OpenClawCatalog`, successful
tool executions automatically record an up-vote; failed executions record a
down-vote. Scores influence search ranking, and skills with negative net scores are
filtered out of results.

```csharp
// Manual vote
var scoreStore = new JsonFileScoreStore(cacheDir);
await scoreStore.RecordVoteAsync("stveenli/airbnb", VoteDirection.Up);

var score = await scoreStore.GetScoreAsync("stveenli/airbnb");
Console.WriteLine($"Up: {score.UpVotes}, Down: {score.DownVotes}");
```

### Can a C# agent call tools written in Python?

Yes. `Ananke.Skills` bridges the language boundary through subprocesses, not interop.
A Python tool is just a CLI binary from the agent's perspective — the C# agent calls
`CliProcessRunner.RunAsync("uvx", "airbnb-search \"Denver, CO\"")` and gets back stdout.
No P/Invoke, no Python runtime embedded in the .NET process, no FFI.

The key enabler is **`uvx`** from the [uv](https://docs.astral.sh/uv/) package manager.
`uvx` downloads and runs any PyPI package in an isolated cache:

```powershell
# Install uv once (Windows)
winget install astral-sh.uv

# uvx then runs any Python tool on demand — no pip, no venv
uvx airbnb-search "Denver, CO" --checkin 2025-08-01 --checkout 2025-08-03
```

On the Ananke side:

```csharp
var catalog = new OpenClawCatalog(
    cacheDir: ".skill-cache",
    enableVoting: true);
await catalog.SyncAsync();   // populate local cache from OpenClaw registry

var tools = new ToolKit("travel");
await tools.AddFromCatalogAsync(catalog, "airbnb search lodging", limit: 3);
// tools now contains ToolDefinitions backed by Python processes
// — the LLM calls them exactly like any other Ananke tool
```

The Python process runs, returns JSON to stdout, and the C# agent reads it. The LLM
never knows the tool is Python.

> See [uv & uvx Setup for .NET Developers](../guides/uv-setup-for-dotnet-developers.md)
> for a setup walkthrough aimed at C#/.NET developers with no Python background.

### What runtimes do catalog skills support?

The `SkillInstallMethod` enum controls how a skill’s binary is launched:

| Method | What it does |
|---|---|
| `Uvx` | Runs the tool with `uvx <package>` — Python tools from PyPI, no install step |
| `Npx` | Runs the tool with `npx <package>` — Node.js tools from npm |
| `Docker` | Runs the tool in a Docker container |
| `Shell` | Runs an arbitrary shell command |

Most tools in the OpenClaw registry use `Uvx`. The C# agent never knows what
language the tool is written in — it receives a string result and continues.

### Can I add my own skills to the catalog?

The `ISkillCatalog` interface is designed to be implemented for any registry.
The `OpenClawCatalog` is the first implementation; future implementations could
target other registries, internal tool directories, or local config files.

---

## Human-in-the-Loop

### How does human-in-the-loop work?

Mark any workflow job with `.InterruptBefore("job-name")` or `.InterruptAfter("job-name")`.
When execution reaches that point, the workflow:

1. Checkpoints the full typed state to `ICheckpointStore`
2. Returns `WorkflowStatus.Interrupted` to the caller
3. Waits until `workflow.ResumeAsync(executionId, stateModifier)` is called

The human reviews the state, optionally modifies it, then resumes.

```csharp
// First run: pauses before "execute"
var execution = await workflow.RunAsync(initialState);
// execution.Status == Interrupted

// Human approves — inject their decision
var resumed = await workflow.ResumeAsync(
    execution.Id,
    state => state with { Approved = true });
// resumed.Status == Completed
```

### How is state persisted across process restarts?

`ICheckpointStore` serializes the full workflow state. Two implementations are provided:

- `InMemoryCheckpointStore` — for tests and single-process scenarios
- `FileCheckpointStore` — persists to disk for local dev and simple deployments

Implement `ICheckpointStore` to back checkpoints with a database or cloud storage.

---

## MCP & Interoperability

### What is MCP and does Ananke support it?

[MCP](https://modelcontextprotocol.io/) (Model Context Protocol) is a standard for connecting
LLM clients to external tools and data. Ananke supports both directions:

- **Expose** — turn any `ToolKit` or `Workflow` into an MCP server with `WithAnankeTools()`
  and `WithAnankeWorkflow<T>()`. Compatible with VS Code Copilot, Claude Desktop, and any
  MCP-compliant client.
- **Consume** — import tools from any external MCP server into a `ToolKit` via
  `AddMcpServerToolsAsync()`.

See [MCP & Interop](guides/12-mcp-and-interop.md) and [McpServerDemo](../src/demos/McpServerDemo/).

### What is A2A and does Ananke support it?

[A2A](https://a2a-protocol.org/) (Agent-to-Agent) is a protocol for direct agent-to-agent
communication over HTTP + JSON-RPC. Ananke supports both directions:

- **Client** — `A2AAgentModel` calls any remote A2A agent as a drop-in `IStreamingAgentModel`.
  Use it directly in workflows and `AgentJob`s just like any local model.
- **Server** — expose Ananke workflows as A2A-compliant endpoints that any A2A client
  (including non-.NET clients) can call.

> **A2A** is for agent-to-agent communication. **MCP** is for agent-to-tool communication.
> Ananke supports both.

### Can I call a remote agent from inside a workflow?

Yes. Wrap the remote agent with `A2AAgentModel` (for A2A) or use the `IStreamingAgentModel`
it produces. Pass it to any `AgentJob` in the workflow — the workflow has no knowledge of
whether the model is local or remote.

---

## Testing

### Can I test agents without calling a real LLM or any external service?

Yes. Every infrastructure contract ships with a zero-config in-memory implementation:

| Contract | In-memory implementation |
|---|---|
| `IDistributedLock` | `InMemoryDistributedLock` |
| `IKnowledgeStore` | `InMemoryKnowledgeStore` |
| `IEmpiricalMemory` | `InMemoryEmpiricalMemory` |
| `ICheckpointStore` | `InMemoryCheckpointStore` |
| `IHandoffChannel<T>` | `InMemoryHandoffChannel<T>` |
| `IConversationMemory` | `InMemoryConversationMemory` |

For LLM calls, implement `IStreamingAgentModel` with a stub that returns deterministic
responses. Integration tests run in milliseconds with no API keys and no network access.

### How do I write integration tests for a workflow?

```csharp
// Use in-memory infrastructure
var store      = new InMemoryKnowledgeStore(embeddingDimension: 1536);
var checkpoint = new InMemoryCheckpointStore();
var locker     = new InMemoryDistributedLock();

// Build and run the workflow
var result = await workflow.RunAsync(initialState);

// Assert on typed state and status
result.Status.ShouldBe(WorkflowStatus.Completed);
result.State.Output.ShouldNotBeNullOrEmpty();
```

See [Testing](guides/14-testing.md) for patterns and examples.

---

## Observability

### Does Ananke support OpenTelemetry?

Yes. `Ananke.OpenTelemetry` provides one-liner OTLP export. The following are automatically
instrumented:

- Workflow job start and end spans with state transition metadata
- State machine transition spans with guard condition results
- LLM retry events (`llm.rate_limit_retry`) with attempt count and delay on the active span
- Tool execution spans with `output_length` and `tool.error` attributes

Compatible with Jaeger, Grafana Tempo, BetterStack, and any OTLP-compatible backend.

### How do I enable tracing?

```bash
dotnet add package Ananke.OpenTelemetry
```

```csharp
services.AddAnankeOtlpTracing(endpoint: "http://localhost:4317");
```

See [Observability](guides/10-observability.md).

---

## Design & Architecture

### Why is the core provider-agnostic?

The workflow graph, state types, tool definitions, and routing rules are all expressed in
terms of Ananke's own interfaces (`IJob<T>`, `IStreamingAgentModel`, `IDistributedLock`,
etc.). LLM providers, vector databases, and infrastructure services are pluggable
implementations. This means:

- Provider changes, outages, or cost optimizations never require changes to business logic
- The same workflow that runs against OpenAI in production can run against a stub in tests
- The same state machine that uses Redis in production uses an in-memory lock in CI

### What makes Ananke's design unique?

- **Infrastructure-first** — distributed locks, checkpointing, and typed state exist before
  any LLM call; the LLM is a pluggable component, not the foundation
- **Full in-memory test mode** — every external dependency has a zero-config stub;
  no Docker, no API keys, no network needed to run a full integration test
- **Three-layer memory model** — semantic RAG + episodic conversation + empirical learning
  in one cohesive system with a clear promotion path from empirical to canonical knowledge
- **Agents that compound intelligence** — `IEmpiricalMemory` + `IOfflineLearner` enable
  genuine improvement over time, not just retrieval
- **MCP + A2A interoperability** — both major agent interop protocols supported out of the box
- **Agentic design patterns** — `AgenticPattern` builder pre-wires recognized patterns
  (Review-Critique, Iterative Refinement) as first-class constructs
- **Idiomatic C#** — async/await, generics, DI, `IAsyncEnumerable` throughout;
  no Python idioms translated to C#, no code-generation required

### Why three memory layers instead of one?

Each layer serves a different time horizon and access pattern:

| Layer | Time horizon | How it grows | Queried by |
|---|---|---|---|
| Semantic (RAG) | Stable documents | Manual ingestion pipeline | Vector similarity |
| Episodic | Single session | Auto-populated per conversation | Recency + session ID |
| Empirical | Repeated interactions | Agent tools + offline learner | Semantic similarity × confidence |

Combining all three into one store would require compromising the design of each.
Keeping them separate lets each layer be optimized for its use case.

### Can I extend Ananke with custom implementations?

Yes. Every core contract is an interface with a default in-memory implementation.
Implement `IKnowledgeStore` for a new vector database, `IStreamingAgentModel` for a new LLM
provider, `IDistributedLock` for a new coordination backend, etc.

---

← [Back to README](../README.md) · [Feature Index](reference/features.md) · [Getting Started](guides/01-getting-started.md)
