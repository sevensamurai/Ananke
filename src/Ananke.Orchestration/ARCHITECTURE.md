# Ananke.Orchestration — Architecture

> Workflow engine, tool system, streaming chat, agentic design patterns,
> checkpointing, and knowledge-to-tool bridge.

## Role

The central orchestration library. Provides the `Workflow<TState>` DAG engine,
`ToolKit` tool system, `StreamingChatWorkflow` (pre-built streaming agent loop),
`AgenticPattern` factory, and bridge code connecting `Ananke.Orchestration.Knowledge`
types to the tool system.

Agent model interfaces (`IAgentModel`, `AgentRequest`, etc.) live in `Ananke.Abstractions.Agents`.
Knowledge types (`IKnowledgeStore`, `IKnowledgeCatalog`, etc.) live in `Ananke.Orchestration.Knowledge`.

## Dependencies

- `Ananke.Abstractions` (project)
- `Ananke.Orchestration.Knowledge` (project)
- `Ananke.Analyzers` (bundled as Roslyn analyzer in NuGet package)
- `Polly` (resilience)

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Orchestration` | `Workflow<TState>`, `WorkflowDefinition`, `WorkflowExecution`, `WorkflowResult`, `AgenticPattern`, `BudgetConfig`, `JobRef`, `ExecutionStatus` |
| `Ananke.Orchestration.Agents` | `AgentJob<TState,TResponse>`, `AgentJobFactory`, `TextAgentJob`, `StreamingChatWorkflow`, `ChatSessionEvent`, `JsonSchemaGenerator` |
| `Ananke.Orchestration.Agents.Context` | `IContextStrategy`, `SlidingWindowContextStrategy`, `SummarizingContextStrategy`, `ITokenCounter`, `ApproximateTokenCounter`, `AgentMessageExtensions` |
| `Ananke.Orchestration.Agents.Middleware` | `IAgentModelMiddleware`, `MiddlewareAgentModel`, `GuardrailAgentModelMiddleware`, `LoggingAgentModelMiddleware`, `CachingAgentModel`, `ResilientAgentModel`, `SmartToolRouterMiddleware` |
| `Ananke.Orchestration.Agents.Routing` | `IModelRouter`, `ModelRouter`, `CapabilityModelRouter`, `ModelCatalog`, `ModelProfile`, `ModelCapability`, `ModelCostRates`, `TaskRequirements` |
| `Ananke.Orchestration.Jobs` | `IJob`, `DelegateJob`, `HandoffJob`, `SubFlowJob`, `JobDescriptor`, `JobExecution`, `Handoff`, `InterruptMode` |
| `Ananke.Orchestration.Routing` | `IRouter`, `DelegateRouter`, `AgentRouter`, `Connections`, `ForkMode`, `ForkTarget`, `JoinDescriptor` |
| `Ananke.Orchestration.Tools` | `ToolKit`, `ToolBuilder`, `ToolDefinition`, `ToolArgs` |
| `Ananke.Orchestration.Tools.Gating` | `IToolFaultObserver`, `ToolAffinityTracker`, `InMemoryToolMemory` |
| `Ananke.Orchestration.Tools.Routing` | `ISmartToolRouter`, `CompositeSmartToolRouter`, `HeuristicTagStage`, `SemanticRecallStage`, `AffinityRerankStage`, `HealthFilterStage`, `LlmRouterStage`, `PinnedToolStage`, `PassThroughRouter`, `IRoutingPromptTemplate`, `DefaultRoutingPromptTemplate` |
| `Ananke.Orchestration.Tools.Faults` | `InMemoryToolFaultObserver`, `ToolHealthRecovery`, `ToolPruner` |
| `Ananke.Orchestration.Knowledge.Tools` | `KnowledgeSearchTool`, `KnowledgeTools` (bridge: Knowledge → ToolKit) |
| `Ananke.Orchestration.Knowledge.Catalog` | `KnowledgeCatalogTools` (bridge: Catalog → ToolKit) |
| `Ananke.Orchestration.Checkpointing` | `ICheckpointStore`, `InMemoryCheckpointStore`, `Checkpoint` |
| `Ananke.Orchestration.Memory` | `InMemoryConversationMemory`, `ConversationMemoryCleanupTimer` |
| `Ananke.Orchestration.Middleware` | `IWorkflowJobMiddleware<TState>` |
| `Ananke.Orchestration.Patterns` | `ReviewCritiqueBuilder`, `IterativeRefinementBuilder` |
| `Ananke.Orchestration.Streaming` | `WorkflowEvent`, `WorkflowStreamOptions`, `WorkflowEventExtensions` |
| `Ananke.Orchestration.Execution` | `IWorkflowRunner`, `WorkflowRunner` |
| `Ananke.Orchestration.Tracing` | `WorkflowTraceContext`, `NullTracer` |
| `Ananke.Orchestration.Extensions` | `ServiceCollectionExtensions` |

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `Workflow<TState>` | Class | Fluent DAG builder — `Job()`, `Then()`, `Decide()`, `Chain()`, `Fork()`, `SubFlow()`. Immutable after first `RunAsync`. |
| `AgentJobFactory` | Static class | Fluent builder factory for `AgentJob<TState,TResponse>` and `TextAgentJob<TState>` — `AgentJobFactory.Create<TState,TResponse>(name, model)` |
| `AgentJob<TState,TResponse>` | Class | `IJob` that wraps an `IAgentModel` call with system prompt, tools, and response mapping |
| `StreamingChatWorkflow` | Static class | Pre-built agent-tools loop with `OnTextDelta`/`OnToolResult` callbacks. Builder pattern via `Create().WithSystemPrompt().WithTools().RunAsync()` |
| `ToolKit` | Class | Named collection of `ToolDefinition` — quick-add (0/1 param) or builder (2+ params) |
| `AgenticPattern` | Static class | Factory for `ReviewCritique<TState>` and `IterativeRefinement<TState>` pattern builders |
| `ModelCatalog` | Class | Registry of `ModelProfile` entries for capability-based routing |
| `CompositeSmartToolRouter` | Class | Pipeline-style smart tool router; compose stages (heuristic, semantic, affinity, health, LLM) via `ISmartToolRouter` |

## Workflow Execution Model

```
Workflow<TState>.RunAsync(initialState)
  → WorkflowExecution (tracks status, state snapshots)
    → For each job in topological order:
        JobExecution (runs IJob.ExecuteAsync, applies middleware)
        → Router decides next job(s)
    → When reaching Workflow.End:
        WorkflowResult { State, Status, ExecutionHistory }
```

## Extension Points

- `IJob` — custom job logic
- `IRouter` — custom routing decisions
- `IAgentModel` / `IStreamingAgentModel` — custom LLM providers (defined in `Ananke.Abstractions`)
- `IWorkflowJobMiddleware<TState>` — cross-cutting concerns at the workflow-job level (logging, retry, auth)
- `IAgentModelMiddleware` — model-level middleware (guardrails, caching, logging)
- `IContextStrategy` — conversation history management (sliding window, summarizing)
- `IModelRouter` — capability-based model selection for multi-model workflows
- `ICheckpointStore` — custom checkpoint persistence
- `IKnowledgeStore`, `IDocumentExtractor`, `IDocumentChunker` — knowledge pipeline (defined in `Ananke.Orchestration.Knowledge`)

## Agents Sub-Structure

The `Agents` folder organizes 26 files into sub-namespaces by concern:

```
Agents/
  ├── Root (5)       AgentJobFactory, AgentJob, TextAgentJob, StreamingChatWorkflow,
  │                  ChatSessionEvent, JsonSchemaGenerator
  ├── Context/ (6)   IContextStrategy, SlidingWindowContextStrategy,
  │                  SummarizingContextStrategy, ITokenCounter,
  │                  ApproximateTokenCounter, AgentMessageExtensions
  ├── Middleware/ (6) IAgentModelMiddleware, MiddlewareAgentModel,
  │                  GuardrailAgentModelMiddleware, LoggingAgentModelMiddleware,
  │                  CachingAgentModel, ResilientAgentModel
  └── Routing/ (8)   IModelRouter, ModelRouter, CapabilityModelRouter,
                     ModelCatalog, ModelProfile, ModelCapability,
                     ModelCostRates, TaskRequirements
```

## `ConfigureAwait` Convention

This project targets **ASP.NET Core / hosted services / console** hosts — none of which install a `SynchronizationContext`. The convention is:

| Site | Rule |
|---|---|
| **Public pipeline entry points** — `IAgentModelMiddleware.OnBeforeGenerateAsync`, `OnAfterGenerateAsync`, `IJob.ExecuteAsync`, etc. | **No `ConfigureAwait`**. The caller (`MiddlewareAgentModel`, `WorkflowRunner`) already runs on the thread pool; annotating here is noise and would mislead a reader into thinking propagation is required. |
| **Private / internal library helpers** — store implementations, Qdrant helpers, `ToolKit` private methods, background timers | **`ConfigureAwait(false)`**. These are pure infrastructure calls with no reason to resume on any particular thread. |

**Do not propagate `ConfigureAwait(false)` up the call stack.** The "propagate all the way up" rule only applies to UI frameworks (WPF/WinForms/MAUI) and classic ASP.NET (non-Core), neither of which this library targets.

## Public API Stability

| Surface | Stability |
|---|---|
| `Workflow<TState>` fluent builder (`Job`, `Then`, `Decide`, `Fork`, `Join`, `SubFlow`, `Chain`) | Stable |
| `AgentJobFactory` / `AgentJob<TState,TResponse>` / `TextAgentJob<TState>` | Stable |
| `StreamingChatWorkflow` | Stable |
| `ToolKit` / `ToolDefinition` / `ToolBuilder` / `ToolArgs` | Stable |
| `IJob<TState>` / `IRouter` / `IAgentModelMiddleware` / `IWorkflowJobMiddleware<TState>` | Stable |
| `ICheckpointStore` / `InMemoryCheckpointStore` | Stable |
| `IContextStrategy` / `SlidingWindowContextStrategy` / `SummarizingContextStrategy` | Stable |
| `IModelRouter` / `ModelCatalog` / `ModelProfile` | Stable |
| `AgenticPattern` (`ReviewCritique`, `IterativeRefinement`) | Stable |
| `CompositeSmartToolRouter` / `ISmartToolRouter` pipeline stages | **Preview** — stage API may change |
| `SmartToolRouterMiddleware` | **Preview** |
| `IWorkflowRunner` / `WorkflowRunner` | Stable |
| `ServiceCollectionExtensions.AddWorkflowOrchestration` | Stable |

Breaking changes to **Stable** surfaces require a documented design review. **Preview** surfaces may change between minor versions.
