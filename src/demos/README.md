# Ananke — Demos

Self-contained runnable examples that show Ananke features in isolation and in combination.
Each demo is an independent .NET 10 project you can run with `dotnet run`.

---

## Quick Start

Most demos that call an LLM need a `secrets.json` file in the demo folder.
Copy the template, fill in your keys, and run:

```bash
cp demos/secrets.json.template demos/<DemoName>/secrets.json
# edit secrets.json with your keys
dotnet run --project demos/<DemoName>
```

See [`secrets.json.template`](./secrets.json.template) for the full key reference.

---

## Demo Index

| Demo | What it shows | Infrastructure needed |
|------|---------------|-----------------------|
| [BasicAgentDemo](#basicagentdemo) | Direct LLM calls · capability-based model routing · caching | OpenAI (Anthropic optional) |
| [SimpleWorkflowDemo](#simpleworkflowdemo) | Linear `Workflow<T>` pipeline · OpenTelemetry tracing | OpenAI · BetterStack (optional) |
| [ExtendedFlowDemo](#extendedflowdemo) | Fork / join · sub-flows · best-effort · interrupt / approval · streaming | None (all simulated) |
| [StateMachineDemo](#statefsmachinedemo) | `AbstractStateMachine` · guards · lifecycle hooks · fault / reset | None |
| [DistributedServicesDemo](#distributedservicesdemo) | Workflow + handoff + conversation memory + FSM bridge in one pipeline | Optional: MQTT, Redis |
| [LongTermMemoryDemo](#longtermmemorydemo) | Document ingestion · vector search · agent Q&A · knowledge catalog | OpenAI · Qdrant (optional) |
| [DesignPipelineDemo](#designpipelinedemo) | YAML-declared ETL pipeline · fork / join · model aliases | OpenAI |
| [AgenticWebDemo](#agenticwebdemo) | ASP.NET Core chat API · tool-calling agent · trade approval workflow | OpenAI · BetterStack (optional) |
| [McpServerDemo](#mcpserverdemo) | MCP server exposing Ananke tools and a workflow to VS Code / Claude Desktop | None |

---

## Descriptions

### BasicAgentDemo

**[`demos/BasicAgentDemo`](./BasicAgentDemo)** · [`README`](./BasicAgentDemo/README.md)

Three progressively richer ways to use Ananke's LLM integration:

1. **Direct call** — raw `GenerateAsync` with no routing
2. **Capability routing** — `CapabilityModelRouter` picks the cheapest model that satisfies each request
3. **Caching** — `CachingAgentModel` wraps any model and memoises identical prompts

**Secrets required:** `OpenAI:ApiKey` (Anthropic optional)

---

### SimpleWorkflowDemo

**[`demos/SimpleWorkflowDemo`](./SimpleWorkflowDemo)**

A minimal multi-step `Workflow<T>` pipeline that shows how jobs are wired together and how
OpenTelemetry spans are exported to BetterStack for distributed tracing.

**Secrets required:** `OpenAI:ApiKey`, `BetterStack:OtlpSourceToken` (tracing optional)

---

### ExtendedFlowDemo

**[`demos/ExtendedFlowDemo`](./ExtendedFlowDemo)**

Six standalone examples in one run — no LLM calls, all logic is simulated so the demo
runs without any API keys:

| Example | Pattern |
|---------|---------|
| `ParallelResearch` | Fork / join with `FailFast` error policy |
| `BestEffortIngest` | Fork / join with `BestEffort` — partial failures are tolerated |
| `MultiStepBranches` | Conditional routing with `Workflow.Decide` |
| `NestedSubFlow` | Sub-workflow composed inside a parent workflow |
| `InterruptApproval` | Workflow pauses and waits for an external approval signal |
| `ForkWithSubFlow` | Parallel branches where each branch is itself a sub-workflow |

**Secrets required:** None

---

### StateMachineDemo

**[`demos/StateMachineDemo`](./StateMachineDemo)**

Explores `AbstractStateMachine` with a support-ticket lifecycle (`Open → InProgress → Resolved → Closed`):

- Happy-path full lifecycle with entry / exit hooks
- Invalid transition rejection
- Guard conditions (Resolve blocked until a resolution note is set)
- Fault / reset — circuit-breaker pattern that freezes all transitions

**Secrets required:** None

---

### DistributedServicesDemo

**[`demos/DistributedServicesDemo`](./DistributedServicesDemo)** · [`README`](./DistributedServicesDemo/README.md)

A support-ticket triage pipeline that wires five Ananke subsystems together:

1. `Workflow<T>` graph-as-code orchestration
2. `Handoff.To<>()` agent-to-agent handoff (in-memory or MQTT)
3. `IConversationMemory` per-customer history (in-memory or Redis)
4. `AbstractStateMachine` ticket lifecycle (`New → Triaging → Resolved → Closed`)
5. `.StateMachineJob()` Bridge extension — zero-boilerplate FSM wiring

Runs fully in-memory with zero configuration, or against real MQTT + Redis brokers
by editing `appsettings.json`.

```bash
dotnet run                    # triage workflow (in-memory everything)
dotnet run -- --specialist    # specialist listener (requires MQTT configured)
```

**Secrets required:** None (infrastructure configured via `appsettings.json`)

---

### LongTermMemoryDemo

**[`demos/LongTermMemoryDemo`](./LongTermMemoryDemo)** · [`README`](./LongTermMemoryDemo/README.md)

End-to-end knowledge pipeline:

1. **Batch import** — PDF → `PdfExtractor` → `SlidingWindowChunker` → embedding → knowledge store
2. **Agent Q&A** — agent autonomously calls `search_engineering_docs` to answer grounded questions
3. **Conversational knowledge building** — agent indexes a URL on request, then answers from it immediately

Supports `InMemoryKnowledgeStore` (no infra) and `QdrantKnowledgeStore` (via Docker):

```bash
dotnet run                                    # in-memory, no catalog
dotnet run -- --qdrant                        # Qdrant backend
dotnet run -- --catalog                       # LLM-enriched metadata + time decay
docker compose up -d && dotnet run -- --qdrant --catalog  # full featured
```

**Secrets required:** `OpenAI:ApiKey`

---

### DesignPipelineDemo

**[`demos/DesignPipelineDemo`](./DesignPipelineDemo)** · [`README`](./DesignPipelineDemo/README.md)

Declarative ETL pipeline driven from a YAML manifest (`etl-pipeline.ananke.yml`).
The manifest declares the graph topology, model aliases, and system prompts.
`Program.cs` binds code jobs as lambdas and runs the workflow.

Demonstrates separation of concerns: topology lives in config, implementation in code.
Prints a Mermaid diagram of the workflow graph after execution.

**Secrets required:** `OpenAI:ApiKey`

---

### AgenticWebDemo

**[`demos/AgenticWebDemo`](./AgenticWebDemo)**

ASP.NET Core web application with:

- `/chat` endpoint — streaming agentic responses with tool-calling (stock price lookup)
- `/trade/approve` endpoint — multi-step trade approval workflow with human-in-the-loop
- Scalar API explorer at `/scalar`
- OpenTelemetry traces exported to BetterStack (optional)

**Secrets required:** `OpenAI:ApiKey`, `BetterStack:OtlpSourceToken` (tracing optional)

---

### McpServerDemo

**[`demos/McpServerDemo`](./McpServerDemo)** · [`README`](./McpServerDemo/README.md)

Exposes Ananke tools and a `Workflow<T>` as an **MCP server** over stdin / stdout.
Connect it from VS Code Copilot or Claude Desktop — no API keys, no network ports, no cloud.

Individual tools (`add`, `multiply`, `word_count`, `reverse`, `country_population`, …) and a
`run_data_pipeline` workflow tool are all callable from the AI client's chat window.

**Secrets required:** None
