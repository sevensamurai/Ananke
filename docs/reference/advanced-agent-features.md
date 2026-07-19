<!-- topic: advanced-agent-features, tags: caching, retries, decorator, local-llm, resilience -->
# Advanced Agent Features

Ananke ships production-ready model decorators and supports local/custom LLM
endpoints out of the box. Everything lives in `Ananke.Orchestration` and
`Ananke.Orchestration.OpenAI` -- no additional packages required.

---

## Local & Custom Endpoints (Ollama, LM Studio, vLLM, Azure OpenAI)

The OpenAI .NET SDK supports any OpenAI-compatible endpoint. Ananke exposes this
through the `endpoint` parameter on `OpenAIChatAgentModel.Create` and the `endpoint:`
field in YAML manifests.

### Direct usage

```csharp
using Ananke.Orchestration.OpenAI;

// Ollama running locally
var ollama = OpenAIChatAgentModel.Create(
    apiKey: "ollama",                                   // any non-empty string
    model: "llama3.2",
    endpoint: new Uri("http://localhost:11434/v1"));

// LM Studio
var lmStudio = OpenAIChatAgentModel.Create(
    apiKey: "lm-studio",
    model: "local-model",
    endpoint: new Uri("http://localhost:1234/v1"));

// Azure OpenAI
var azure = OpenAIChatAgentModel.Create(
    apiKey: azureKey,
    model: "gpt-4.1-mini",
    endpoint: new Uri("https://my-resource.openai.azure.com/"));

// Use exactly like any IStreamingAgentModel
var response = await ollama.GenerateAsync(request);
```

### YAML manifest

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

### Configuration override

The endpoint can also come from configuration instead of (or as a fallback to) YAML:

```json
{
  "OpenAI": {
    "ApiKey": "ollama",
    "Model": "llama3.2",
    "Endpoint": "http://localhost:11434/v1"
  }
}
```

Priority: YAML `endpoint:` field > `{Section}:Endpoint` in config > default provider URL.

### ModelResolver with endpoint support

```csharp
var models = new ModelResolver()
    .Register("openai", "OpenAI", OpenAIChatAgentModel.Create)  // 3-param factory (apiKey, model, endpoint)
    .Register("anthropic", "Anthropic", AnthropicAgentModel.Create)  // 2-param factory (no endpoint)
    .Resolve(manifest, key => config[key]);
```

The resolver automatically passes the endpoint from YAML or config to the factory.
Providers registered with the 2-param `Register` overload ignore the endpoint.

### Compatible providers

| Provider | Endpoint | Notes |
|---|---|---|
| **Ollama** | `http://localhost:11434/v1` | Default Ollama port. Use any string as API key. |
| **LM Studio** | `http://localhost:1234/v1` | Default LM Studio port. |
| **vLLM** | `http://localhost:8000/v1` | Default vLLM port. |
| **Azure OpenAI** | `https://{resource}.openai.azure.com/` | Requires a real API key. |
| **Deepseek** | `https://api.deepseek.com/v1` | OpenAI-compatible; requires Deepseek API key. |
| **Groq** | `https://api.groq.com/openai/v1` | OpenAI-compatible; requires Groq API key. |
| **Together AI** | `https://api.together.xyz/v1` | OpenAI-compatible; requires Together API key. |

---

## Response Caching (`CachingAgentModel`)

Caches LLM responses to avoid redundant API calls for identical prompts.
Uses the existing `IKeyValueDataAdapter` from `Ananke.Abstractions`, so any
backend you already have (e.g. `RedisDataAdapter` from `Ananke.Redis`) works
out of the box.

### When to use

- **Cost reduction** -- agentic tool-loops can produce identical sub-requests
  across retries or parallel branches. Caching deduplicates them.
- **Latency** -- cache hits return instantly without an LLM round-trip.
- **Development / testing** -- cache responses during iteration to avoid burning
  API credits on repeated runs.

### Basic usage

```csharp
using Ananke.Orchestration.Agents;

var model = new CachingAgentModel(
    inner: openAiModel,                 // any IStreamingAgentModel
    cache: redisAdapter,                // any IKeyValueDataAdapter
    ttl: TimeSpan.FromMinutes(5));

// Use exactly like any IStreamingAgentModel -- workflows, jobs, direct calls
var response = await model.GenerateAsync(request);
```

### With `StreamingChatWorkflow`

```csharp
var cachedModel = new CachingAgentModel(openAiModel, redisAdapter, TimeSpan.FromMinutes(10));

var workflow = StreamingChatWorkflow.Create("chat", cachedModel)
    .WithSystemPrompt("You are a helpful assistant.")
    .WithTools(toolkit)
    .OnTextDelta(async delta => Console.Write(delta))
    .Build();

var execution = await workflow.RunAsync(messages);
```

Streaming cache hits replay the full text as a single `TextDelta` chunk followed by
the `CompletedResponse` sentinel, so `StreamingChatWorkflow` and SSE consumers work
unchanged.

### How caching works

| Aspect | Detail |
|---|---|
| **Cache key** | SHA256 hash of `SystemPrompt` + `Messages` + `Tools` + `ResponseFormat`. `Metadata` and `StoreCompletions` are excluded (they don't affect model output). |
| **TTL** | Stored alongside the response as an `ExpiresAt` timestamp. Expired entries are treated as cache misses and cleaned up on read. |
| **Tool-call responses** | Never cached. Responses where `RequiresAction == true` are always fetched fresh, because tool results depend on external state. |
| **Key prefix** | Defaults to `"ananke:llm-cache"`. Configurable via the `keyPrefix` constructor parameter for multi-tenant or multi-model isolation. |

---

## Resilient Retries (`ResilientAgentModel`)

Handles HTTP 429 (rate-limit) errors from LLM providers with exponential backoff,
jitter, and automatic OTel event reporting. Uses the existing Polly dependency --
no new packages required.

### When to use

- **Production workloads** -- providers return 429 when you exceed rate limits.
  Without retry, the workflow faults immediately. With `ResilientAgentModel`, the
  call backs off and retries transparently.
- **Multi-user apps** -- concurrent users sharing an API key will occasionally hit
  limits. Graceful retry keeps the experience smooth.
- **Batch processing** -- long-running pipelines that make many LLM calls benefit
  from automatic recovery without manual error handling.

### Quick setup (defaults)

```csharp
using Ananke.Orchestration.Agents;

// 5 retries, 1s base delay, exponential backoff + jitter
var model = ResilientAgentModel.Create(openAiModel);

var response = await model.GenerateAsync(request);
```

### Custom configuration

```csharp
var model = ResilientAgentModel.Create(
    openAiModel,
    maxRetryAttempts: 3,
    baseDelay: TimeSpan.FromSeconds(2));
```

### Full control with a custom Polly pipeline

For advanced scenarios (circuit-breaker, timeout, custom predicates), pass your own
`ResiliencePipeline`:

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

### How retry works

| Aspect | Detail |
|---|---|
| **Non-streaming** (`GenerateAsync`) | Wrapped with Polly `ResiliencePipeline.ExecuteAsync`. Retry count, backoff, and jitter are handled by Polly. |
| **Streaming** (`GenerateStreamAsync`) | Two-phase approach. Phase 1 retries until the first chunk arrives (this is where 429 occurs -- the initial HTTP request). Phase 2 yields all remaining chunks without retry (partial streams can't be replayed). |
| **429 detection** | Provider-agnostic. Checks `HttpRequestException.StatusCode`, then duck-types `Status` / `StatusCode` properties for provider-specific exceptions (covers OpenAI's `ClientResultException`, Anthropic, etc.). Walks the full inner-exception chain. |
| **Custom predicate** | Pass `shouldRetry: ex => ...` to `Create()` or the constructor for full control over which exceptions trigger retry. |

### OTel integration

Each retry records an event on the current `Activity` (if a trace is active):

```
Event: llm.rate_limit_retry
  retry.attempt:    2
  retry.delay_ms:   2150
  exception.type:   System.ClientModel.ClientResultException
  exception.message: HTTP 429 (Too Many Requests)
```

The cumulative retry count is also set as a span tag (`llm.retries`). This
integrates with Ananke's `ActivitySourceTracer` automatically -- no extra wiring.

When viewed in your tracing backend (Jaeger, BetterStack, Grafana Tempo), retry
events appear as annotations on the workflow span, making rate-limit pressure
visible at a glance.

---

## Composing Decorators

Both decorators implement `IStreamingAgentModel`, so they stack naturally.
The recommended order is resilience on the inside, caching on the outside:

```csharp
// 1. Start with the provider (local or cloud)
IStreamingAgentModel openAi = OpenAIChatAgentModel.Create(apiKey, "gpt-4.1-mini");

// 2. Add retry (innermost -- retries happen before caching)
var resilient = ResilientAgentModel.Create(openAi);

// 3. Add caching (outermost -- cache hits skip both the LLM and retry logic)
var model = new CachingAgentModel(resilient, redisAdapter, TimeSpan.FromMinutes(5));

// 4. Use in any workflow
var workflow = StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are a helpful assistant.")
    .WithTools(toolkit)
    .Build();
```

With this ordering:
- **Cache hit** -- returned instantly, no LLM call, no retry overhead.
- **Cache miss** -- calls the LLM via the resilient wrapper. If 429 hits,
  retries transparently. Successful response is cached for next time.
- **Tool-call responses** -- never cached (always fresh), but still retried on 429.

### With `CapabilityModelRouter`

Both decorators work with model routing. Wrap each model individually:

```csharp
var miniModel = new CachingAgentModel(
    ResilientAgentModel.Create(OpenAIChatAgentModel.Create(apiKey, "gpt-4.1-mini")),
    redisAdapter, TimeSpan.FromMinutes(10));

var localModel = new CachingAgentModel(
    ResilientAgentModel.Create(
        OpenAIChatAgentModel.Create("ollama", "llama3.2", new Uri("http://localhost:11434/v1"))),
    redisAdapter, TimeSpan.FromMinutes(30));

var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
    .AddModel(new ModelProfile { Name = "gpt-4.1-mini", Model = miniModel, /* ... */ })
    .AddModel(new ModelProfile { Name = "llama3.2",     Model = localModel, /* ... */ });
```

Each model gets independent caching (different key hashes due to different model
behaviour) and independent retry budgets.

---

## Tool Definitions & LLM Discoverability

Every tool registered through `ToolKit` carries enough metadata for the LLM to
understand **what the tool does**, **when to call it**, and **what arguments to
pass** — without any manual JSON Schema authoring.

### What the LLM sees

When a `ToolKit` is attached to a workflow or `AgentJob`, each `ToolDefinition`
is serialised into the provider's function-calling format. The mapping is:

| Ananke type | Provider concept | Purpose |
|---|---|---|
| `ToolDefinition.Name` | Function name | The identifier the LLM emits when it decides to call the tool. |
| `ToolDefinition.Description` | Function description | Tells the LLM *when* and *why* to call the tool. Write this like a doc-comment aimed at the model. |
| `ToolParameter.Name` | Property key in `parameters` | The JSON key the LLM must include in its arguments object. |
| `ToolParameter.Description` | Property description | Tells the LLM *what* value to supply and any constraints (format, range, etc.). |
| `ToolParameter.JsonType` | `type` in JSON Schema | The expected data type: `"string"` (default), `"integer"`, `"number"`, or `"boolean"`. |

`ToolDefinition.ParametersJsonSchema` generates the complete JSON Schema
automatically — including `required` (all parameters are required) and
`additionalProperties: false` — so providers like OpenAI receive a
well-formed function definition with no extra wiring.

### Example — descriptive names and descriptions matter

Good tool metadata helps the LLM choose the right tool and pass correct
arguments. Compare:

```csharp
// ❌ Vague — the LLM may not know when to call this or what "q" means
var weak = new ToolKit("tools")
    .AddTool("search", "Search.", (string q) => DoSearch(q),
        "q", "The query.");

// ✅ Clear — the LLM knows exactly when to call and what to pass
var strong = new ToolKit("research")
    .AddTool(
        "lookup_population",
        "Looks up the current population of a country by name. Returns a formatted string with the population count and year.",
        (string country) => country.ToUpperInvariant() switch
        {
            "JAPAN"   => "125.7 million (2024)",
            "BRAZIL"  => "216.4 million (2024)",
            "GERMANY" => "84.5 million (2024)",
            _         => $"Population data not available for {country}"
        },
        "country",
        "The English name of the country to look up (e.g. \"Japan\", \"Brazil\").");
```

### Typed parameters

For non-string parameters, use the generic `AddTool<T>` overload (one type parameter — there is
no `AddTool<T1, T2>`). The JSON Schema `type` is inferred automatically (`int`/`long` →
`"integer"`, `float`/`double`/`decimal` → `"number"`, `bool` → `"boolean"`). For two or more
typed parameters, use the `ToolBuilder` callback with `Param<T>` per parameter:

```csharp
var tools = new ToolKit("math")
    .AddTool("add", "Adds two integers and returns the sum.", b => b
        .Param<int>("a", "The first integer operand.")
        .Param<int>("b", "The second integer operand.")
        .OnExecute(args => ToolResult.Ok($"{args.Get<int>("a") + args.Get<int>("b")}")));
```

The generated JSON Schema for this tool:

```json
{
  "type": "object",
  "properties": {
    "a": { "type": "integer", "description": "The first integer operand." },
    "b": { "type": "integer", "description": "The second integer operand." }
  },
  "required": ["a", "b"],
  "additionalProperties": false
}
```

### Error handling in tools — `ToolResult.Ok` and `ToolResult.Error`

Every tool execution returns a `ToolResult` — a lightweight discriminator that
tells the framework whether the call succeeded or failed. Both cases carry a
string that is sent to the LLM as the tool result; the difference is what the
**framework** does with the signal (logging, span attributes).

```csharp
// The type — defined in Ananke.Orchestration.Tools
public readonly record struct ToolResult(string Value, bool IsError)
{
    public static ToolResult Ok(string value) => new(value, IsError: false);
    public static ToolResult Error(string error) => new(error, IsError: true);
    public static implicit operator ToolResult(string value) => Ok(value);
}
```

#### Two paths — no third option

| Return | When to use | What the framework does |
|---|---|---|
| `ToolResult.Ok(value)` | Tool executed successfully. | Records `output_length` on the span. Logs at debug level. |
| `ToolResult.Error(msg)` | Expected/recoverable failure (bad input, not found, validation). | Sets `tool.error` span attribute. Logs at warning level. The error string is still sent to the LLM so it can self-correct. |
| **Throw an exception** | Unrecoverable failure (auth error, network outage, corrupt state). | Exception propagates — workflow faults. |

There are exactly two tool outcomes (`Ok` and `Error`) plus the escape hatch of
throwing. No error codes, no severity levels, no categories.

#### Implicit conversion — existing code just works

`ToolResult` has an implicit conversion from `string`, so plain string returns
are treated as `Ok`:

```csharp
// These two are equivalent:
.AddTool("ping", "Pings", () => "pong")
.AddTool("ping", "Pings", () => ToolResult.Ok("pong"))
```

This means **every existing tool lambda compiles unchanged** — the implicit
conversion wraps the string as `ToolResult.Ok` automatically.

Use `ToolResult.Error(...)` explicitly when you want the framework to know
something went wrong:

```csharp
var tools = new ToolKit("weather")
    .AddTool(
        "get_weather",
        "Returns the current weather for a city. Returns an error if the city is not recognised.",
        (string city) =>
        {
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["London"]  = "15°C, overcast",
                ["Tokyo"]   = "28°C, sunny",
                ["Sydney"]  = "19°C, partly cloudy"
            };

            return data.TryGetValue(city, out var weather)
                ? ToolResult.Ok(weather)
                : ToolResult.Error($"City '{city}' not found. Available: {string.Join(", ", data.Keys)}.");
        },
        "city",
        "The city name to look up (e.g. \"London\", \"Tokyo\").");
```

When the LLM calls `get_weather` with `"Paris"`, it receives:

```
City 'Paris' not found. Available: London, Tokyo, Sydney.
```

The LLM sees this as the tool result, understands the failure, and can either
pick a valid city or inform the user — without crashing the workflow. Meanwhile,
the framework logs a warning and marks the span so the error is visible in your
tracing backend.

#### Wrapping external calls

For tools that call external services, catch expected exceptions and return
`ToolResult.Error`. Let truly fatal errors propagate:

```csharp
var tools = new ToolKit("api")
    .AddTool(
        "fetch_price",
        "Fetches the current price of a product by SKU from the catalogue API.",
        async (string sku) =>
        {
            try
            {
                var price = await catalogueClient.GetPriceAsync(sku);
                return ToolResult.Ok($"${price:F2}");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ToolResult.Error($"SKU '{sku}' not found in the catalogue.");
            }
            // Other exceptions (500, timeout, auth) propagate and fault the workflow
        },
        "sku",
        "The product SKU to look up (e.g. \"WIDGET-42\").");
```

#### What the framework does with `IsError`

In `AgentJob`, the tool loop branches after each call:

```
tool executes → ToolResult
                   │
        ┌──────────┴──────────┐
        │                     │
   ToolResult.Ok         ToolResult.Error
        │                     │
   span: output_length   span: output_length + tool.error
   log: (debug)          log: LogWarning with tool name + error text
        │                     │
        └──────────┬──────────┘
                   │
          AgentMessage.ToolResult(call.Id, toolResult.Value)
                   │
              → back to LLM
```

Both paths send the same `AgentMessage.ToolResult` to the LLM — the
discrimination exists for **framework observability**, not for the model.

#### Guidelines

- **Use `ToolResult.Ok` / `ToolResult.Error`** — don't rely on string
  conventions like `"Error: ..."` prefixes. The framework reads `IsError`,
  not the string content.
- **Include actionable detail in errors** — tell the LLM *why* it failed and
  *what* to try instead (valid values, format hints, constraints).
- **Mention error behaviour in the tool description** — e.g.
  `"Returns an error if the city is not recognised."` This primes the LLM to
  handle the error path gracefully.
- **Don't silently swallow exceptions** — if an error means the workflow cannot
  continue (auth failure, data corruption), let it throw so the caller can
  handle it at the workflow level.
- **Unknown tools** are returned as `ToolResult.Error` automatically — both
  `AgentJob` and `StreamingChatWorkflow` handle this for you.

### Tips for effective tool descriptions

- **Tool name**: Use `snake_case` verbs (e.g. `lookup_population`, `send_email`).
  Most providers normalise to this format.
- **Tool description**: Write 1–2 sentences explaining *what* the tool does and
  *when* the agent should reach for it. Mention return format if non-obvious.
- **Parameter description**: Include example values, units, or constraints.
  The LLM uses this to decide what to pass — treat it like API documentation.

---

## Empirical Memory Observability

`InMemoryEmpiricalMemory` and `QdrantEmpiricalMemory` emit counters, traces, and
structured logs through `System.Diagnostics.Metrics`, `ActivitySource`, and
`ILogger` under the `Ananke.EmpiricalMemory` namespace.

Key metrics for monitoring whether learning is working:

| Metric | Question it answers |
|---|---|
| `empirical.commits` | Is the agent discovering patterns? |
| `empirical.recall_hits / empirical.recalls` | Is stored knowledge being found when needed? |
| `empirical.reinforcements` | Is knowledge being validated by experience? |
| `empirical.contradictions` | Are bad hypotheses being pruned? |
| `empirical.dedup_merges` | Is semantic dedup preventing entry bloat? |

Wire into OTLP by subscribing to the `Ananke.EmpiricalMemory` meter and activity
source. Full details, ratios, and production health checks:
**[Guide 15 — Empirical Memory: Observability](../guides/15-empirical-memory.md#observability--monitoring-whether-learning-works)**.
