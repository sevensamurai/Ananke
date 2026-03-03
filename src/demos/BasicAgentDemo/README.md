# BasicAgentDemo — Model Routing

This demo walks through three progressively richer ways to use Ananke's LLM integration, culminating in **capability-based model routing** — automatic, cost-effective model selection based on what each task actually needs.

---

## Quick Start

```bash
cd demos/BasicAgentDemo
# Add your OpenAI key to secrets.json
dotnet run
```

---

## What the Demo Shows

### Part 1 — Direct Model Call

The simplest path: create a model, build a request, call `GenerateAsync`.

```csharp
IStreamingAgentModel model = new OpenAIChatAgentModel(
    new ChatClient("gpt-4.1-mini", new ApiKeyCredential(apiKey)));

var response = await model.GenerateAsync(new AgentRequest
{
    SystemPrompt = "You are a concise assistant.",
    Messages = [AgentMessage.User("What is the capital of Japan?")]
});
```

No routing, no workflow — just a raw LLM call. Fine for scripts and one-off tasks.

### Part 2 — Capability-Based Routing

Register model profiles with capabilities, cost, and intelligence metadata. The `CapabilityModelRouter` then picks the cheapest model that satisfies every request automatically.

```csharp
var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
    .AddModel(new ModelProfile
    {
        Name = "gpt-4.1-mini",
        Model = miniModel,
        Capabilities = ModelCapability.TextGeneration | ModelCapability.ToolCalling
                     | ModelCapability.StructuredOutput | ModelCapability.LargeContext,
        IntelligenceTier = 2,
        CostPer1KTokens = 0.40m,
        MaxContextTokens = 1_047_576,
        SpeedTier = 4
    })
    .AddModel(new ModelProfile
    {
        Name = "gpt-4.1",
        Model = fullModel,
        Capabilities = ModelCapability.TextGeneration | ModelCapability.CodeGeneration
                     | ModelCapability.Reasoning | ModelCapability.ToolCalling
                     | ModelCapability.StructuredOutput | ModelCapability.LargeContext,
        IntelligenceTier = 4,
        CostPer1KTokens = 2.00m,
        MaxContextTokens = 1_047_576,
        SpeedTier = 3
    });
```

The router selects based on requirements — either inferred from the request structure or tagged explicitly:

```csharp
// Simple text → gpt-4.1-mini (cheapest that satisfies TextGeneration)
router.Select(simpleRequest);

// Reasoning + high intelligence → gpt-4.1 (only candidate with tier ≥ 3)
var request = new AgentRequest { ... }
    .WithRequiredCapabilities(ModelCapability.Reasoning)
    .WithMinIntelligence(3);
router.Select(request);
```

### Part 3 — Workflow with AgentJob + Routed Model

The same router plugs into `AgentJob.Builder` via the existing `IModelRouter` interface. Different jobs in the same workflow can hit different models without any manual switching:

```csharp
// gather job — needs ToolCalling → mini (cheaper)
var gatherJob = new AgentJob<ResearchState, GatherResult>
    .Builder("gather", router)
    .WithTools(researchTools)
    ...

// analyze job — needs Reasoning → full (only candidate)
var analyzeJob = new AgentJob<ResearchState, AnalysisResult>
    .Builder("analyze", router)
    ...

var workflow = new Workflow<ResearchState>("country-research")
    .Job("gather", gatherJob)
    .Job("analyze", analyzeJob)
    .Then("gather", "analyze")
    .Then("analyze", Workflow.End);
```

---

## How Routing Works

### Architecture

```
AgentRequest
    │
    ▼
TaskRequirements.InferFrom(request)      ← auto-detect from request structure
    │                                       + explicit metadata overrides
    ▼
CapabilityModelRouter.Select(request)
    │
    ├─ filter: ModelProfile.Satisfies(requirements)
    │    • capability flags match
    │    • intelligence tier ≥ minimum
    │    • context window ≥ minimum
    │
    ├─ rank by RoutingStrategy
    │    • CheapestFit  → lowest cost, break ties by speed
    │    • FastestFit   → highest speed, break ties by cost
    │    • BestFit      → highest intelligence, break ties by cost
    │
    └─► IAgentModel (selected)
```

### Automatic Inference

`TaskRequirements.InferFrom()` detects needs from the request itself:

| Request Shape | Inferred Capability |
|---|---|
| `Tools` present | `ToolCalling` |
| `ResponseFormat` set | `StructuredOutput` |
| Content > 64 K chars | `LargeContext` |

### Explicit Overrides

Tag requests via extension methods for full control:

```csharp
request
    .WithRequiredCapabilities(ModelCapability.Reasoning | ModelCapability.CodeGeneration)
    .WithMinIntelligence(3)
    .WithMinContextTokens(100_000);
```

These are stored in `AgentRequest.Metadata` and merged with inferred capabilities.

### ModelCapability Flags

Ordered from least to most demanding (higher bits = harder tasks):

| Tier | Flag | Bit |
|---|---|---|
| 1 — Basic | `TextGeneration` | 0 |
| 1 — Basic | `LargeContext` | 1 |
| 2 — Intermediate | `StructuredOutput` | 2 |
| 2 — Intermediate | `ToolCalling` | 3 |
| 3 — Advanced | `CodeGeneration` | 4 |
| 3 — Advanced | `Vision` | 5 |
| 4 — Frontier | `Reasoning` | 6 |

Flags compose with `|` — a model that supports `ToolCalling | Reasoning` can handle requests requiring either or both.

---

## Benefits

| Benefit | Description |
|---|---|
| **Cost optimisation** | Simple tasks hit cheap models automatically; only complex tasks use expensive ones |
| **Zero manual predicates** | Unlike `ModelRouter.When()`, you don't write routing logic — declare profiles once |
| **Drop-in integration** | `CapabilityModelRouter` implements `IModelRouter` — works with `AgentJob`, `RoutedAgentModel`, and existing workflows |
| **Mixed providers** | Profiles can wrap any `IAgentModel` — OpenAI, Anthropic, local models — all in one router |
| **Strategy flexibility** | Switch between `CheapestFit`, `FastestFit`, and `BestFit` with a single enum change |
| **Fallback safety** | `WithFallback()` ensures a high-capability model catches any unmatched requests |

---

## When to Use Which

| Approach | Use When |
|---|---|
| **Direct model** | Scripts, prototypes, single-model apps |
| **`ModelRouter`** (predicate) | Custom routing logic based on state or request content |
| **`CapabilityModelRouter`** | Production multi-model setups where you want automatic cost/performance optimisation |
