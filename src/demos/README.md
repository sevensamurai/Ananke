# Ananke — Demos

Self-contained runnable examples that show Ananke features in isolation and in combination.
Each demo is an independent .NET 10 project you can run with `dotnet run`.

---

## Quick Start

Most demos that call an LLM need a `secrets.json` file in the demo folder.
Copy the template, fill in your keys, and run:

```bash
cp demos/secrets.json.template demos/<Category>/<DemoName>/secrets.json
# edit secrets.json with your keys
dotnet run --project demos/<Category>/<DemoName>
```

See [`secrets.json.template`](./secrets.json.template) for the full key reference.

---

## Demo Index

| # | Demo | What it shows | Infrastructure needed |
|---|------|---------------|-----------------------|
| 01 | [BasicAgentDemo](#basicagentdemo) | Direct LLM calls · capability routing · caching | OpenAI (Anthropic optional) |
| 01 | [StateMachineDemo](#statemachinedemo) | `AbstractStateMachine` · guards · lifecycle hooks · MQTT transport | None (MQTT optional) |
| 02 | [AgenticDesignPatternsDemo](#agenticdesignpatternsdemo) | 14 agentic patterns — ReAct, fork/join, sub-flows, streaming, HITL | None (all simulated) |
| 02 | [DesignPipelineDemo](#designpipelinedemo) | YAML-declared ETL pipeline · fork/join · model aliases · Mermaid export | OpenAI |
| 02 | [SelfImprovingWorkflowDemo](#selfimprovingworkflowdemo) | Self-diagnosing workflow · YAML manifest diff · simulated doc tools | None (all simulated) |
| 03 | [EntityMemoryDemo](#entitymemorydemo) | Per-entity memory isolation · cold-start vs. personalized recommendations | None |
| 03 | [LearningPrimitivesDemo](#learningprimitivesdemo) | Skill catalog (OpenClaw/cowsay) · post-division UCB routing via Qdrant | OpenAI · Qdrant (routing scenario) |
| 03 | [LongTermMemoryDemo](#longtermmemorydemo) | Document ingestion · vector search · knowledge catalog · cross-doc linking | OpenAI · Qdrant (optional) |
| 04 | [Connect4Demo](#connect4demo) | Empirical learning via gameplay · UCB scoring · skill import/export | None |
| 04 | [LogEventsDemo](#logeventsdemo) | Rule-based pattern detection · empirical memory · offline learning REPL | None |
| 04 | [OrganicKernelDemo](#organickerneldemo) | Organic growth · complexity sensing · division · approval gate · memory feedback | None (all simulated) |
| 05 | [AgenticWebDemo](#agenticwebdemo) | ASP.NET Core streaming chat · tool-calling agent · trade approval HITL | OpenAI · BetterStack (optional) |
| 05 | [PetAdoptionDemo](#petadoptiondemo) | Full-stack RAG app · stateful phases · SSE streaming · mid-gen interrupts · voice/photo | OpenAI or Gemini · Docker (optional) |
| 06 | [AgentToAgentProtocolDemo](#agenttoagentprotocoldemo) | A2A server · agent card · C# + Python clients · cross-language interop | None |
| 06 | [ChannelsDemo](#channelsdemo) | Ananke agent as a Discord or Slack bot · `IPlatformMessageHandler` | OpenAI · Discord or Slack bot token |
| 06 | [McpServerDemo](#mcpserverdemo) | MCP server exposing Ananke tools and a workflow to VS Code / Claude Desktop | None |

---

## Descriptions

### 01 — Foundations

#### BasicAgentDemo

**[`01-foundations/BasicAgentDemo`](./01-foundations/BasicAgentDemo)** · [`README`](./01-foundations/BasicAgentDemo/README.md)

Three progressively richer ways to use Ananke's LLM integration:

1. **Direct call** — raw `GenerateAsync` with no routing
2. **Capability routing** — `CapabilityModelRouter` picks the cheapest model that satisfies each request
3. **Caching** — `CachingAgentModel` wraps any model and memoises identical prompts

**Secrets required:** `OpenAI:ApiKey` (Anthropic optional)

---

#### StateMachineDemo

**[`01-foundations/StateMachineDemo`](./01-foundations/StateMachineDemo)** · [`README`](./01-foundations/StateMachineDemo/README.md)

`AbstractStateMachine` modelling a car engine lifecycle (`Parked → Running → Moving → Idle`):

- Full happy-path trip with `StateMachineChannelWorker` over an `InMemoryChannelReader/Writer`
- Guard condition: `Drive` blocked when `FuelLevel = 0`
- Fault / reset — faulted machine rejects all transitions; `ResetAsync` restores it
- MQTT-driven transitions via `MqttChannelReader/Writer` (opt-in, requires Docker)

**Secrets required:** None (MQTT section requires Docker)

---

### 02 — Workflow Patterns

#### AgenticDesignPatternsDemo

**[`02-workflow-patterns/AgenticDesignPatternsDemo`](./02-workflow-patterns/AgenticDesignPatternsDemo)** · [`README`](./02-workflow-patterns/AgenticDesignPatternsDemo/README.md)

A runnable catalogue of **14 agentic design patterns** — all offline, no API keys needed:

| # | Pattern | Key APIs |
|---|---------|----------|
| 1 | Single Agent (ReAct) | `AgentJobFactory`, `ToolKit`, `Workflow<T>` |
| 2 | Sequential Chain | `.Chain()` |
| 3 | Parallel Fork / Join | `Workflow.Fork()`, `.Join()` |
| 4 | Router / Coordinator | `Workflow.Decide<T>()` |
| 5–14 | Loop, Review, HITL, Sub-flow, Middleware, Context Strategy, Budget, Streaming… | various |

**Secrets required:** None

---

#### DesignPipelineDemo

**[`02-workflow-patterns/DesignPipelineDemo`](./02-workflow-patterns/DesignPipelineDemo)** · [`README`](./02-workflow-patterns/DesignPipelineDemo/README.md)

Declarative ETL pipeline driven from a YAML manifest (`etl-pipeline.ananke.yml`).
The manifest declares the graph topology, model aliases, and system prompts.
`Program.cs` binds code jobs as lambdas and runs the workflow.
Prints a Mermaid diagram of the workflow graph after execution.

**Secrets required:** `OpenAI:ApiKey`

---

#### SelfImprovingWorkflowDemo

**[`02-workflow-patterns/SelfImprovingWorkflowDemo`](./02-workflow-patterns/SelfImprovingWorkflowDemo)** · [`README`](./02-workflow-patterns/SelfImprovingWorkflowDemo/README.md)

A travel-expense analyzer workflow that **diagnoses its own missing capability**:

- Run 1 uses `expense-analyzer.ananke.yml` — overseer agent detects no currency-conversion step via `inspect_workflow` / `search_docs` / `suggest_fix` tools
- Run 2 uses `expense-analyzer-v2.ananke.yml` — fixed manifest with `convert_currencies` code job

All LLM responses are simulated — no API keys required.

**Secrets required:** None

---

### 03 — Memory & Knowledge

#### EntityMemoryDemo

**[`03-memory-and-knowledge/EntityMemoryDemo`](./03-memory-and-knowledge/EntityMemoryDemo)** · [`README`](./03-memory-and-knowledge/EntityMemoryDemo/README.md)

Per-entity long-term memory for a furniture shopping companion:

- `EntityMemoryProvider` creates per-customer scoped facades over shared stores
- Cold-start visit: generic bestsellers; return visit: style-matched recommendations
- Entity isolation is metadata-based — different customers' profiles never merge

**Secrets required:** None

---

#### LearningPrimitivesDemo

**[`03-memory-and-knowledge/LearningPrimitivesDemo`](./03-memory-and-knowledge/LearningPrimitivesDemo)**

Two selectable scenarios focused on the `Ananke.Learning` and `Ananke.Skills` primitives:

```bash
dotnet run                           # skills scenario (default)
dotnet run -- --scenario routing     # routing evolution scenario
```

**Skills scenario** — discovers and invokes an external CLI skill (`cowsay`) through `OpenClawCatalog`. Demonstrates `SkillDescriptor` registration, `ToolKit.AddFromCatalogAsync`, and the full OpenClaw pipeline with JSON-file score persistence.
Prerequisites: `uv` installed (`winget install astral-sh.uv`), `OpenAI:ApiKey`.

**Routing scenario** — simulates a post-division bookstore mesh routing evolution (Hybrid Routing, Option D).

**Secrets required:** `OpenAI:ApiKey` (skills scenario only)

---

#### LongTermMemoryDemo

**[`03-memory-and-knowledge/LongTermMemoryDemo`](./03-memory-and-knowledge/LongTermMemoryDemo)** · [`README`](./03-memory-and-knowledge/LongTermMemoryDemo/README.md)

End-to-end knowledge pipeline:

1. **Batch import** — PDF → `PdfExtractor` → `SlidingWindowChunker` → embedding → knowledge store
2. **Agent Q&A** — agent autonomously calls `search_knowledge` to answer grounded questions
3. **Conversational knowledge building** — agent indexes a URL on request, then answers from it immediately
4. **Cross-document linking** — `DocumentLinkExtractor` + `IDocumentLinkGraph` expand search via relationship graph

```bash
dotnet run                                    # in-memory, no catalog
dotnet run -- --qdrant                        # Qdrant backend
dotnet run -- --catalog                       # LLM-enriched metadata + time decay
dotnet run -- --linking                       # cross-document link graph
docker compose up -d && dotnet run -- --qdrant --catalog --linking  # full featured
```

**Secrets required:** `OpenAI:ApiKey`

---

### 04 — Organics & Emergence

#### Connect4Demo

**[`04-organics-and-emergence/Connect4Demo`](./04-organics-and-emergence/Connect4Demo)** · [`README`](./04-organics-and-emergence/Connect4Demo/README.md)

A Connect 4 game where the agent starts knowing only the rules and learns strategy while playing. No LLM, no API keys.

- `StateMachine<S, T>` drives the game flow (`Idle → Playing → Analyzing → Idle`)
- `OnInsight<T>` + `InMemoryEmpiricalMemory` accumulate patterns, skills, and heuristics
- Composite UCB scoring recalls the most relevant, confident, recent experience before each move
- Supports self-play training, skill import/export, and analysis reports

```bash
dotnet run -- --train 200 --export skills.json   # train then save
dotnet run -- --import skills.json               # play with pre-trained skills
```

**Secrets required:** None

---

#### LogEventsDemo

**[`04-organics-and-emergence/LogEventsDemo`](./04-organics-and-emergence/LogEventsDemo)** · [`README`](./04-organics-and-emergence/LogEventsDemo/README.md)

Empirical memory built from operations logs — no LLM required:

- `LogSimulator` emits structured `LogEvent` records with injected `FailureScenario` cascades
- `RuleBasedPatternDetector` detects cascade signatures and stores them as `EmpiricalKind.Episode`
- Interactive REPL (`Explorer`) for `browse` / `recall` / `confirm` / `reject` / `learn` / `report`
- `OfflineLearner` uses `TagOverlapPredictionSource` with UCB curiosity scoring

**Secrets required:** None

---

#### OrganicKernelDemo

**[`04-organics-and-emergence/OrganicKernelDemo`](./04-organics-and-emergence/OrganicKernelDemo)** · [`README`](./04-organics-and-emergence/OrganicKernelDemo/README.md)

Full organic lifecycle of an Ananke workflow — a generalist bookstore agent accumulates tools, detects structural tension, and divides into two specialists. All LLM responses simulated.

- `OrganicHost` + `IComplexityMonitor` sense tool-count and routing-entropy tension
- `IDivisionPolicy` proposes the split; `IDivisionApprovalGate` gates it (interactive in `--supervised` mode)
- `WorkflowDivider` spawns child workflows and kills the parent
- Division outcome written to `InMemoryEmpiricalMemory` for future policy learning

**Secrets required:** None

---

### 05 — Applications

#### AgenticWebDemo

**[`05-applications/AgenticWebDemo`](./05-applications/AgenticWebDemo)** · [`README`](./05-applications/AgenticWebDemo/README.md)

ASP.NET Core web application with two agentic features:

- **Streaming chat** (`POST /api/chat`) — `StreamingChatWorkflow` over SSE with stock-market tool-calling
- **Trade approval** (`POST /api/trade/analyze` + `POST /api/trade/approve`) — multi-step HITL workflow
- Scalar API explorer at `/scalar`; OpenTelemetry traces to BetterStack (optional)

**Secrets required:** `OpenAI:ApiKey`, `BetterStack:OtlpSourceToken` (optional)

---

#### PetAdoptionDemo

**[`05-applications/PetAdoptionDemo`](./05-applications/PetAdoptionDemo)** · [`README`](./05-applications/PetAdoptionDemo/README.md)

Full-stack pet adoption assistant demonstrating advanced Ananke features in a web app:

- State machine drives session phases: `Searching → Paperwork → Payment → Done`
- RAG over Markdown knowledge base (pets + policies)
- Token-level SSE streaming with mid-generation interrupts
- Human-in-the-loop payment: agent pauses at `Payment` phase until `/api/payment` is called
- Voice and photo input support
- Runs on OpenAI (`gpt-4.1-mini`) or Google Gemini (`gemini-2.5-flash`) — one config line swap
- Single `docker compose up --build` starts all services

**Secrets required:** `OpenAI:ApiKey` or `Gemini:ApiKey`

---

### 06 — Interop & Channels

#### AgentToAgentProtocolDemo

**[`06-interop-and-channels/AgentToAgentProtocolDemo`](./06-interop-and-channels/AgentToAgentProtocolDemo)** · [`README`](./06-interop-and-channels/AgentToAgentProtocolDemo/README.md)

An Ananke agent exposed as an **A2A-compliant HTTP server** called from two independent clients:

- `GET /.well-known/agent-card.json` — automatic skill discovery
- C# client: `A2AAgentModel` — uses the remote agent as a drop-in `IStreamingAgentModel`
- Python client: plain HTTP + JSON-RPC, no Ananke SDK, no third-party packages

**Secrets required:** None

---

#### ChannelsDemo

**[`06-interop-and-channels/ChannelsDemo`](./06-interop-and-channels/ChannelsDemo)**

An Ananke tool-calling agent deployed as a **Discord or Slack bot** using `Ananke.Platforms`:

```bash
dotnet run -- --platform discord
dotnet run -- --platform slack
```

- `AddAnankeDiscord` / `AddAnankeSlack` register the platform adapters in the DI container
- `IPlatformMessageHandler` implementations (`DiscordAgentHandler`, `SlackAgentHandler`) handle incoming messages
- `IConversationMemory` preserves conversation context across turns (1-hour TTL)
- Tools exposed: `current_time`, `echo`

**Secrets required:** `OpenAI:ApiKey` + `Discord:BotToken` (Discord) or `Slack:BotToken` + `Slack:AppToken` (Slack)

---

#### McpServerDemo

**[`06-interop-and-channels/McpServerDemo`](./06-interop-and-channels/McpServerDemo)** · [`README`](./06-interop-and-channels/McpServerDemo/README.md)

Exposes Ananke tools and a `Workflow<T>` as an **MCP server** over stdin / stdout.
Connect it from VS Code Copilot or Claude Desktop — no API keys, no network ports, no cloud.

Individual tools (`add`, `multiply`, `word_count`, `reverse`, `country_population`, …) and a
`run_data_pipeline` workflow tool are all callable from the AI client's chat window.

**Secrets required:** None
