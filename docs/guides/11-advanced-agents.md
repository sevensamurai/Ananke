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

Wrap each model individually:

```csharp
var miniModel = new CachingAgentModel(
    ResilientAgentModel.Create(OpenAIChatAgentModel.Create(apiKey, "gpt-4.1-mini")),
    redisAdapter, TimeSpan.FromMinutes(10));

var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
    .AddModel(new ModelProfile { Name = "gpt-4.1-mini", Model = miniModel, /* ... */ });
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [12 — MCP & Interop](12-mcp-and-interop.md) | Expose tools as MCP server, consume external tools |
| [14 — Testing](14-testing.md) | Test without LLMs or infrastructure |

---

← [Back to Learning Path](../learning.md)
