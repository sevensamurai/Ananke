# Architecture: Agents & LLM Providers

> Part of the [Architecture Guide](../ARCHITECTURE.md). Covers the agent model abstraction, provider adapters, middleware pipeline, and model routing.

---

## Agent Model Abstraction

```mermaid
classDiagram
    class IAgentModel {
        +GenerateAsync(AgentRequest, ct) Task~AgentResponse~
    }
    class IStreamingAgentModel {
        +GenerateStreamAsync(AgentRequest, ct) IAsyncEnumerable~AgentStreamChunk~
    }
    class IEmbeddingModel {
        +EmbedAsync(text) float[]
    }

    IStreamingAgentModel --|> IAgentModel
    OpenAIChatAgentModel ..|> IStreamingAgentModel
    AnthropicAgentModel ..|> IStreamingAgentModel
    GeminiAgentModel ..|> IStreamingAgentModel
```

### Key Design Decision

Ananke defines its own `IAgentModel` rather than using `Microsoft.Extensions.AI.IChatClient`. This keeps the abstraction minimal, vendor-neutral, and tightly aligned with agent-specific concerns (tool calls, structured output, token budgets).

## Message Types

| Type | Role |
|---|---|
| `AgentRequest` | System prompt + message history + tools + config |
| `AgentResponse` | Content + tool calls + token usage + stop reason |
| `AgentMessage` | Single message (role + content parts) |
| `ContentPart` | Text, image, or audio segment |
| `AgentToolCall` | Tool name + JSON arguments from LLM |
| `AgentStreamChunk` | Incremental token during streaming |
| `TokenUsage` | Prompt/completion/total token counts |

## Provider Adapters

Each provider package is a **thin adapter** that depends only on `Ananke.Abstractions`:

| Package | LLM SDK | Notes |
|---|---|---|
| `Ananke.Orchestration.OpenAI` | `OpenAI` NuGet | Also works with Azure OpenAI, Ollama, LM Studio, Groq, etc. via base URL override |
| `Ananke.Orchestration.Anthropic` | `Anthropic` NuGet | Claude models |
| `Ananke.Orchestration.Google` | `Google.GenAI` NuGet | Gemini Developer API + Vertex AI |

## Middleware Pipeline

```mermaid
flowchart LR
    CALL[AgentJob call] --> GUARD[GuardrailAgentModelMiddleware]
    GUARD --> LOG[LoggingAgentModelMiddleware]
    LOG --> CACHE[CachingAgentModel]
    CACHE --> RETRY[ResilientAgentModel<br/>Polly 429 retry]
    RETRY --> PROVIDER[OpenAI / Anthropic / Google]
```

Middleware is composed via `MiddlewareAgentModel` which wraps an inner `IAgentModel` with a chain of `IAgentModelMiddleware`.

## Model Routing

Two routing mechanisms are provided:

### `ModelRouter`

`ModelRouter` is a simple predicate-based router. Register one or more `When(predicate, model)` rules and a mandatory `Otherwise(fallback)`. Converts to `IAgentModel` via `.ToAgentModel()` (returns a `RoutedAgentModel` that also implements `IStreamingAgentModel`).

```csharp
var router = new ModelRouter()
    .When(req => req.Messages.Any(m => m.Parts?.Any(p => p is ImagePart) == true), visionModel)
    .Otherwise(defaultModel);
```

### `CapabilityModelRouter`

`CapabilityModelRouter` selects the best model for a task based on declared capabilities and cost/speed/intelligence tiers:

```mermaid
flowchart TD
    REQ[TaskRequirements<br/>e.g. vision + reasoning] --> ROUTER[CapabilityModelRouter]
    ROUTER --> CAT[ModelCatalog<br/>registered ModelProfiles]
    CAT --> MATCH[Best match by<br/>RoutingStrategy]
    MATCH --> MODEL[Selected IAgentModel]
```

- `ModelProfile` — declares model name, capabilities (`ModelCapability` flags), cost rates, speed tier, intelligence tier
- `ModelCapability` — flags enum with tiers:
  - *Tier 1 (basic)*: `TextGeneration`, `LargeContext`
  - *Tier 2 (intermediate)*: `StructuredOutput`, `ToolCalling`
  - *Tier 3 (advanced)*: `CodeGeneration`, `Vision`
  - *Tier 4 (frontier)*: `Reasoning`
  - *Tier 5 (multimodal)*: `AudioInput`, `ImageGeneration`, `AudioOutput`, `RealtimeStreaming`, `VideoInput`
- `TaskRequirements` — what the job needs (required `ModelCapability` flags)
- `ModelCostRates` — input/output token pricing for budget-aware routing
- `RoutingStrategy` — `CheapestFit`, `FastestFit`, `BestFit`, `Weighted`, `Custom`; configurable per-router

## Context Management

`IContextStrategy` controls how conversation history is managed within token budgets:

| Strategy | Behavior |
|---|---|
| `SlidingWindowContextStrategy` | Keep last N messages within token limit |
| `SummarizingContextStrategy` | Summarize older messages via LLM when window overflows |
