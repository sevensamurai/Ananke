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
Compatibility is maintained with type-forwarding for several agent and knowledge types that used to live in this assembly.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `Workflow<TState>` — the fluent typed workflow builder: direct, routed, looped, fork/join,
   and sub-workflow transitions, frozen after `Build()` — `src/Ananke.Orchestration/Workflows/Workflow.cs`
2. `IWorkflowRunner` — executes, resumes, and streams `WorkflowDefinition<TState>` instances — `src/Ananke.Orchestration/Execution/IWorkflowRunner.cs`
3. `WorkflowRunner` — the default execution engine: checkpoints, interrupts, fork/join
   orchestration, middleware, and event streaming — `src/Ananke.Orchestration/Execution/WorkflowRunner.cs`
4. `ToolKit` — named collection of `ToolDefinition` with tool-memory integration, routing
   hooks, fault observation, and execution-strategy support — `src/Ananke.Orchestration/Tools/ToolKit.cs`
5. `AgentJobFactory` — fluent builder factory for `AgentJob<TState,TResponse>` and
   `TextAgentJob<TState>`, the `IJob`s that wrap an `IAgentModel` call — `src/Ananke.Orchestration/Agents/AgentJob.cs`

---

## Dependencies

- `Ananke.Abstractions` (project)
- `Ananke.Orchestration.Knowledge` (project)
- `Ananke.Analyzers` (bundled as Roslyn analyzer in NuGet package)
- `Polly` (resilience)

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Orchestration` | `AgenticPattern`, `JobRef`, type-forwards for selected agent/knowledge types |
| `Ananke.Orchestration.Workflows` | `Workflow`, `Workflow<TState>`, `WorkflowDefinition`, `WorkflowExecution`, `WorkflowResult`, `ExecutionStatus`, `WorkflowInputExtensions` |
| `Ananke.Orchestration.Agents` | `AgentJob<TState,TResponse>`, `TextAgentJob<TState>`, `StreamingChatWorkflow`, `ChatSessionEvent`, `JsonSchemaGenerator`, token-usage capture helpers |
| `Ananke.Orchestration.Agents.Context` | `IContextStrategy`, `SlidingWindowContextStrategy`, `SummarizingContextStrategy`, `ITokenCounter`, `ApproximateTokenCounter`, `AgentMessageExtensions` |
| `Ananke.Orchestration.Agents.Middleware` | `IAgentModelMiddleware`, `MiddlewareAgentModel`, `GuardrailAgentModelMiddleware`, `LoggingAgentModelMiddleware`, `CachingAgentModel`, `ResilientAgentModel`, `SmartToolRouterMiddleware` |
| `Ananke.Orchestration.Agents.Routing` | `IModelRouter`, `ModelRouter`, `CapabilityModelRouter`, `ModelCatalog`, `ModelProfile`, `ModelCapability`, `ModelCostRates`, `TaskRequirements` |
| `Ananke.Orchestration.Jobs` | `IJob`, `DelegateJob`, `HandoffJob`, `HandoffProxy`, `SubFlowJob`, `SubFlowContext`, `SubFlowInterruptedException`, `InMemoryHandoffChannel`, `JobDescriptor`, `JobExecution`, `Handoff`, `InterruptMode` |
| `Ananke.Orchestration.Routing` | `IRouter`, `DelegateRouter`, `AgentRouter`, `AgentRoutingException`, `Connection` (abstract; `DirectConnection`, `RouterConnection<TState>`, `ForkConnection`, `LoopConnection<TState>`), `ForkMode`, `ForkTarget`, `JoinDescriptor`, `LoopExitReason` |
| `Ananke.Orchestration.Tools` | `ToolKit`, `ToolBuilder`, `ToolDefinition`, `ToolArgs`, `ToolExecutionMode`, `ToolMetrics`, `IToolExecutorStrategy` |
| `Ananke.Orchestration.Tools.Gating` | `IToolFaultObserver`, `ToolAffinityTracker`, `InMemoryToolMemory` |
| `Ananke.Orchestration.Tools.Routing` | `ISmartToolRouter`, `CompositeSmartToolRouter`, `HeuristicTagStage`, `SemanticRecallStage`, `AffinityRerankStage`, `HealthFilterStage`, `LlmRouterStage`, `PinnedToolStage`, `PassThroughRouter`, `IRoutingPromptTemplate`, `DefaultRoutingPromptTemplate` |
| `Ananke.Orchestration.Tools.Faults` | `InMemoryToolFaultObserver`, `ToolHealthRecovery`, `ToolPruner` |
| `Ananke.Orchestration.Knowledge.Tools` | `KnowledgeSearchTool`, `KnowledgeTools` (bridge: Knowledge → ToolKit) |
| `Ananke.Orchestration.Knowledge.Catalog` | `KnowledgeCatalogTools` (bridge: Catalog → ToolKit) |
| `Ananke.Orchestration.Checkpointing` | `ICheckpointStore`, `InMemoryCheckpointStore`, `Checkpoint` |
| `Ananke.Orchestration.Memory` | `InMemoryConversationMemory`, `ConversationMemoryCleanupTimer` |
| `Ananke.Orchestration.Middleware` | `IWorkflowJobMiddleware<TState>` |
| `Ananke.Orchestration.Patterns` | `ReviewCritiqueBuilder`, `IterativeRefinementBuilder`, `InterviewBuilder`, `Interview` |
| `Ananke.Orchestration.Streaming` | `WorkflowEvent`, `WorkflowStreamOptions`, `WorkflowEventExtensions` |
| `Ananke.Orchestration.Execution` | `IWorkflowRunner`, `WorkflowRunner` |
| `Ananke.Orchestration.Tracing` | `WorkflowTraceContext`, `NullTracer` |
| `Ananke.Orchestration.Extensions` | `ServiceCollectionExtensions` |
| `Ananke.Orchestration.Budget` | `BudgetConfig`, token usage helpers |
| ~~`Ananke.Orchestration.Credentials` / `Translators`~~ | Moved to `Ananke.Abstractions.Providers`: `ICredentialProvider`, `IJsonSchemaTranslator`, `IModelMapper`, `ISystemPromptCompiler`, `IToolSchemaTranslator`, `SystemPromptBuilder` — provider-agnostic credential resolution and schema/prompt translation contracts; implementations live in each `Ananke.Orchestration.{Provider}` package |

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `Workflow<TState>` | Class | Fluent typed workflow builder with direct, routed, looped, fork/join, and sub-workflow transitions. Frozen after `Build()`. | `src/Ananke.Orchestration/Workflows/Workflow.cs` |
| `IWorkflowRunner` | Interface | Executes, resumes, and streams `WorkflowDefinition<TState>` instances | `src/Ananke.Orchestration/Execution/IWorkflowRunner.cs` |
| `WorkflowRunner` | Class | Default execution engine implementing checkpoints, interrupts, fork/join orchestration, middleware, and event streaming | `src/Ananke.Orchestration/Execution/WorkflowRunner.cs` |
| `AgentJobFactory` | Static class | Fluent builder factory for `AgentJob<TState,TResponse>` and `TextAgentJob<TState>` | `src/Ananke.Orchestration/Agents/AgentJob.cs` |
| `AgentJob<TState,TResponse>` | Class | `IJob` that wraps an `IAgentModel` call with system prompt, tools, and response mapping | `src/Ananke.Orchestration/Agents/AgentJob.cs` |
| `TextAgentJob<TState>` | Class | `IJob` wrapper for plain-text agent output, optional memory, context compaction, and tool loops | `src/Ananke.Orchestration/Agents/TextAgentJob.cs` |
| `StreamingChatWorkflow` | Static class | Pre-built streaming agent-tools loop with delta callbacks, optional memory, and context strategies | `src/Ananke.Orchestration/Agents/StreamingChatWorkflow.cs` |
| `ToolKit` | Class | Named collection of `ToolDefinition` with tool-memory integration, routing hooks, fault observation, and execution-strategy support | `src/Ananke.Orchestration/Tools/ToolKit.cs` |
| `AgenticPattern` | Static class | Factory for `ReviewCritique<TState>`, `IterativeRefinement<TState>`, and `Interview<TState>` pattern builders | `src/Ananke.Orchestration/AgenticPattern.cs` |
| `WorkflowInputExtensions` | Static class | `ResumeWithInputAsync` — channel-agnostic fold-then-resume helper for `ask`/`AwaitInput` turns | `src/Ananke.Orchestration/Workflows/WorkflowInputExtensions.cs` |
| `ModelCatalog` | Class | Registry of `ModelProfile` entries for capability-based routing | `src/Ananke.Orchestration/Agents/Routing/ModelCatalog.cs` |
| `CompositeSmartToolRouter` | Class | Pipeline-style smart tool router; compose stages (heuristic, semantic, affinity, health, LLM) via `ISmartToolRouter` | `src/Ananke.Orchestration/Tools/Routing/CompositeSmartToolRouter.cs` |

## Workflow Execution Model

```
Workflow<TState>.RunAsync(initialState)
  → WorkflowExecution (tracks status, state snapshots)
    → For each job in topological order:
        JobExecution (runs IJob.ExecuteAsync, applies middleware)
        → Router decides next job(s)
    → When reaching Workflow.End:
        WorkflowResult<TState> { Success, FinalState, TotalDuration, JobsExecuted, History, Error, Exception }
```

## Extension Points

- `IJob<TState>` — custom job logic
- `IRouter<TState>` — custom routing decisions
- `IAgentModel` / `IStreamingAgentModel` — custom LLM providers (defined in `Ananke.Abstractions`)
- `IWorkflowJobMiddleware<TState>` — cross-cutting concerns at the workflow-job level (logging, retry, auth)
- `IAgentModelMiddleware` — model-level middleware (guardrails, caching, logging)
- `IContextStrategy` — conversation history management (sliding window, summarizing)
- `IModelRouter` — capability-based model selection for multi-model workflows
- `ICheckpointStore` — custom checkpoint persistence
- `IToolExecutorStrategy` — execution for remote-backed or externally hosted tools
- `IToolFaultObserver` — health/fault observation for tool execution
- `IKnowledgeStore`, `IDocumentExtractor`, `IDocumentChunker` — knowledge pipeline (defined in `Ananke.Orchestration.Knowledge`)

## Agents Sub-Structure

The `Agents` area is split into sub-namespaces by concern:

```
Agents/
  ├── Root           AgentJobFactory, AgentJob, TextAgentJob, StreamingChatWorkflow,
  │                  ChatSessionEvent, JsonSchemaGenerator
  ├── Context/ (6)   IContextStrategy, SlidingWindowContextStrategy,
  │                  SummarizingContextStrategy, ITokenCounter,
  │                  ApproximateTokenCounter, AgentMessageExtensions
  ├── Middleware/    IAgentModelMiddleware, MiddlewareAgentModel,
  │                  GuardrailAgentModelMiddleware, LoggingAgentModelMiddleware,
  │                  CachingAgentModel, ResilientAgentModel, SmartToolRouterMiddleware
  └── Routing/       IModelRouter, ModelRouter, CapabilityModelRouter,
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
| `Workflow<TState>` fluent builder (`Job`, `Then`, `Decide`, `Loop`, `Fork`, `Join`, `SubFlow`, `Chain`) | Stable |
| `AgentJobFactory` / `AgentJob<TState,TResponse>` / `TextAgentJob<TState>` | Stable |
| `StreamingChatWorkflow` | Stable |
| `ToolKit` / `ToolDefinition` / `ToolBuilder` / `ToolArgs` | Stable |
| `IJob<TState>` / `IRouter` / `IAgentModelMiddleware` / `IWorkflowJobMiddleware<TState>` | Stable |
| `ICheckpointStore` / `InMemoryCheckpointStore` | Stable |
| `IContextStrategy` / `SlidingWindowContextStrategy` / `SummarizingContextStrategy` | Stable |
| `IModelRouter` / `ModelCatalog` / `ModelProfile` | Stable |
| `AgenticPattern` (`ReviewCritique`, `IterativeRefinement`) | Stable |
| `AgenticPattern.Interview` / `InterviewBuilder` / `Interview<TState>` | **Preview** — exercised end-to-end by an external demo, not yet by an in-repo one |
| `Workflow<TState>.AwaitInput` / `WorkflowDefinition.InputJobs` / `WorkflowInputExtensions.ResumeWithInputAsync` | **Preview** |
| `CompositeSmartToolRouter` / `ISmartToolRouter` pipeline stages | **Preview** — stage API may change |
| `SmartToolRouterMiddleware` | **Preview** |
| `IWorkflowRunner` / `WorkflowRunner` | Stable |
| `ServiceCollectionExtensions.AddWorkflowOrchestration` | Stable |

Breaking changes to **Stable** surfaces require a documented design review. **Preview** surfaces may change between minor versions.
