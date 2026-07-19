<!-- topic: advanced-agents, tags: caching, retries, decorator, local-llm, resilience -->
# 11 — Advanced Agent Features

Production-ready model decorators for response caching and resilient retries,
decorator composition, and local/custom LLM endpoints.

→ **Full reference:** [Advanced Agent Features](../reference/advanced-agent-features.md)

---

## Local & Custom Endpoints

Any OpenAI-compatible endpoint works via the `endpoint` parameter:

```csharp
using Ananke.Orchestration.OpenAI;

// Ollama
var ollama = OpenAIChatAgentModel.Create(
    apiKey: "ollama", model: "llama3.2",
    endpoint: new Uri("http://localhost:11434/v1"));

// LM Studio
var lmStudio = OpenAIChatAgentModel.Create(
    apiKey: "lm-studio", model: "local-model",
    endpoint: new Uri("http://localhost:1234/v1"));

// Azure OpenAI
var azure = OpenAIChatAgentModel.Create(
    apiKey: azureKey, model: "gpt-4.1-mini",
    endpoint: new Uri("https://my-resource.openai.azure.com/"));
```

### Compatible Providers

| Provider | Endpoint |
|---|---|
| Ollama | `http://localhost:11434/v1` |
| LM Studio | `http://localhost:1234/v1` |
| vLLM | `http://localhost:8000/v1` |
| Azure OpenAI | `https://{resource}.openai.azure.com/` |
| Deepseek | `https://api.deepseek.com/v1` |
| Groq | `https://api.groq.com/openai/v1` |
| Together AI | `https://api.together.xyz/v1` |

### YAML Configuration

```yaml
models:
  local:
    provider: openai
    model: llama3.2
    endpoint: http://localhost:11434/v1
  cloud:
    provider: openai
    model: gpt-4.1-mini
    # no endpoint = default OpenAI API
```

---

## Response Caching (`CachingAgentModel`)

Cache LLM responses to avoid redundant API calls:

```csharp
using Ananke.Orchestration.Agents;

var model = new CachingAgentModel(
    inner: openAiModel,
    cache: redisAdapter,               // any IKeyValueDataAdapter
    ttl:   TimeSpan.FromMinutes(5));

var response = await model.GenerateAsync(request);
```

### How It Works

| Aspect | Detail |
|---|---|
| **Cache key** | SHA256 of SystemPrompt + Messages + Tools + ResponseFormat |
| **TTL** | Stored with the response; expired entries treated as misses |
| **Tool-call responses** | Never cached (depend on external state) |
| **Streaming** | Cache hits replay as a single chunk — SSE consumers work unchanged |

---

## Resilient Retries (`ResilientAgentModel`)

Handle HTTP 429 rate-limit errors with exponential backoff and jitter:

```csharp
// Quick setup — 5 retries, 1s base delay
var model = ResilientAgentModel.Create(openAiModel);

// Custom configuration
var model = ResilientAgentModel.Create(
    openAiModel,
    maxRetryAttempts: 3,
    baseDelay: TimeSpan.FromSeconds(2));
```

### Full Control with Polly

```csharp
using Polly;
using Polly.Retry;

var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TimeoutException>(),
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromSeconds(1)
    })
    .AddTimeout(TimeSpan.FromSeconds(30))
    .Build();

var model = new ResilientAgentModel(openAiModel, pipeline);
```

### OTel Integration

Each retry records an event on the current `Activity`:

```
Event: llm.rate_limit_retry
  retry.attempt:    2
  retry.delay_ms:   2150
  exception.type:   ClientResultException
  exception.message: HTTP 429 (Too Many Requests)
```

---

## Job-Level Retry (`WithRetry`)

`ResilientAgentModel` (above) retries a single model call on HTTP 429. `WithRetry` is a
different, complementary mechanism: it retries the **whole agent job** — model call and any
tool round — on a transient failure, available on every `AgentJobFactory.Create(...)` builder:

```csharp
var agent = AgentJobFactory.Create<MyState, MyResponse>("classify", model)
    .WithPrompt(s => s.Input)
    .WithRetry(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(200))
    .MapResult((s, r) => s with { Result = r })
    .Build();
```

Each retry attempt increments the `ananke.model.retry` counter (meter
`Ananke.Orchestration.Tools`, tag `agent_id`) — see [10 — Observability](10-observability.md) for
wiring metrics into an OTEL exporter. Pair it with `WithTrajectoryObserver` (below) to also
surface the retry count on the completed episode's `TrajectorySnapshot.RetryCount`.

---

## Hallucination Detection (`IHallucinationObserver`)

When a model calls a tool name that isn't registered in the `ToolKit`, the agent doesn't crash
or silently ignore it — it returns a well-formed error result to the model (so the model can
self-correct on the next turn) and, if a hallucination observer is registered, reports a
`HallucinatedToolCallEvent`:

```csharp
using Ananke.Abstractions.Tools;

internal sealed class LoggingHallucinationObserver : IHallucinationObserver
{
    public ValueTask ReportAsync(HallucinatedToolCallEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"[hallucination] agent={@event.AgentId} kit={@event.RequestedKitName} " +
            $"requested unknown tool '{@event.RequestedToolName}'");
        return ValueTask.CompletedTask;
    }
}

var tools = new ToolKit("ops")
    .AddTool("real_tool", "A real tool", () => ToolResult.Ok("..."))
    .WithHallucinationObserver(new LoggingHallucinationObserver());
```

Each hallucinated call also increments the `ananke.tools.hallucination` counter (tags
`agent_id`, `kit`, `requested_name`).

---

## Trajectory Observability & the Adaptive Harness

A `TrajectorySnapshot` is a deterministic, per-episode outcome record — tool-call counts,
hallucinations, faults, retries, and terminal reward — emitted when an agent job's episode
completes. Register an observer with `WithTrajectoryObserver` to receive one per run:

```csharp
using Ananke.Abstractions.Trajectory;

internal sealed class LoggingTrajectoryObserver : ITrajectoryObserver
{
    public ValueTask OnTrajectoryCompleteAsync(TrajectorySnapshot snapshot, CancellationToken ct = default)
    {
        Console.WriteLine($"[trajectory] episode={snapshot.EpisodeId} succeeded={snapshot.Succeeded} " +
            $"retries={snapshot.RetryCount} hallucinations={snapshot.HallucinatedToolCalls}");
        return ValueTask.CompletedTask;
    }
}

var agent = AgentJobFactory.Create<MyState>("respond", model)
    .WithPrompt(s => s.Input)
    .WithTrajectoryObserver(new LoggingTrajectoryObserver())
    .MapResult((s, text) => s with { Output = text })
    .Build();
```

`Ananke.Orchestration` ships a default policy that *reacts* to these snapshots instead of just
logging them — `CompositeAdaptiveHarnessPolicy` implements both `IAdaptiveHarnessPolicy` and
`ITrajectoryObserver`, so registering it as the trajectory observer is enough to wire it in:

```csharp
using Ananke.Orchestration.Agents.Trajectory;

var harness = new CompositeAdaptiveHarnessPolicy(
    tracker: toolAffinityTracker,        // ToolAffinityTracker — same one backing the ToolKit's router
    options: new AdaptiveHarnessOptions { KitName = "ops", HallucinationThreshold = 3 },
    learningTrigger: offlineLearner);    // ILearningCycleTrigger — e.g. Ananke.Learning's OfflineLearner

var agent = AgentJobFactory.Create<MyState>("respond", model)
    .WithPrompt(s => s.Input)
    .WithTrajectoryObserver(harness)
    .MapResult((s, text) => s with { Output = text })
    .Build();
```

It applies three rules per completed episode: hallucinations at or above
`HallucinationThreshold` trigger a learning cycle; abandoned faults penalize the affinity of
every tool tracked under `KitName`; a clean success (zero retries) rewards them. This is the
seam where tool routing adapts from real outcomes instead of a fixed configuration — see
[06 — Memory](06-memory.md) and [15 — Empirical Memory](15-empirical-memory.md) for what
`ToolAffinityTracker` and the offline learner do with that signal.

---

## Composing Decorators

Both decorators implement `IStreamingAgentModel` and stack naturally.
Recommended order — resilience inside, caching outside:

```csharp
// 1. Provider
IStreamingAgentModel openAi = OpenAIChatAgentModel.Create(apiKey, "gpt-4.1-mini");

// 2. Add retry (innermost)
var resilient = ResilientAgentModel.Create(openAi);

// 3. Add caching (outermost)
var model = new CachingAgentModel(resilient, redisAdapter, TimeSpan.FromMinutes(5));

// 4. Use in any workflow
var workflow = StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are a helpful assistant.")
    .WithTools(toolkit)
    .Build();
```

**Cache hit** → returned instantly, no LLM call.
**Cache miss** → calls LLM via resilient wrapper; retries on 429; caches result.
**Tool-call responses** → never cached, still retried on 429.

### With Model Routing

Wrap each model individually, then register it with a profile from the shared
`Ananke.Orchestration.Agents.Routing.ModelCatalog` — its `ModelProfileTemplate`s carry the stable
capability/context/tier metadata, so you only supply live pricing via `ToProfile(model, rates)`:

```csharp
using Ananke.Orchestration.Agents.Routing;

var miniModel = new CachingAgentModel(
    ResilientAgentModel.Create(OpenAIChatAgentModel.Create(apiKey, "gpt-5.6-terra")),
    redisAdapter, TimeSpan.FromMinutes(10));

var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
    .AddModel(ModelCatalog.OpenAI.Gpt56Terra.ToProfile(miniModel, new ModelCostRates(0.0002m, 0.0008m)));
```

Every template carries a `Status` (`Current`/`Legacy`/`Deprecated`/`Retired`) and, for anything
short of `Current`, a `ReplacedBy` pointing at today's recommended model — both flow through into
the resulting `ModelProfile`. Pass an `ILogger` to `CapabilityModelRouter` and it logs a one-time
warning (per process, per model) whenever routing selects a `Deprecated` profile, naming the
replacement — so a router quietly still serving a superseded model doesn't go unnoticed:

```csharp
var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit, logger)
    .AddModel(ModelCatalog.OpenAI.Gpt41.ToProfile(model, rates)); // still callable, but Deprecated

// First SelectProfile() call that resolves to Gpt41 logs once:
//   "Routed to deprecated model 'gpt-4.1' — use 'gpt-5.6-sol' instead."
```

See [Model Deprecations](../reference/model-deprecations.md) for the full lifecycle policy and
current per-provider status.

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [12 — MCP & Interop](12-mcp-and-interop.md) | Expose tools as MCP server, consume external tools |
| [14 — Testing](14-testing.md) | Test without LLMs or infrastructure |

---

← [Back to Learning Path](../learning-path.md)
