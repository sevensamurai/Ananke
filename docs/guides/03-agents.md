<!-- topic: agents, tags: agent, llm, openai, anthropic, google, structured-output, model-routing, multimodal -->
# 03 — Agents

Integrate LLMs into workflows with `AgentJob`, structured output, multi-provider
support, and capability-based model routing.

The provider is a pluggable detail, not a foundation. Because every agent is wrapped behind `IStreamingAgentModel`, the workflow code — prompts, tool definitions, state mappings — stays unchanged when you swap OpenAI for Anthropic, Google Gemini, or a local model. Switching is a one-line configuration change.

**Demo:** [BasicAgentDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/BasicAgentDemo)

---

## Core Concepts

An **agent** in Ananke is an LLM wrapped in the `IStreamingAgentModel` interface.
An **AgentJob** drops that LLM into a workflow job with a system prompt, user prompt,
optional tools, and typed response mapping.

---

## LLM Providers

### OpenAI

```bash
dotnet add package Ananke.Orchestration.OpenAI
```

```csharp
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.OpenAI;
using OpenAI.Chat;
using System.ClientModel;

IStreamingAgentModel model = new OpenAIChatAgentModel(
    new ChatClient("gpt-4.1-mini", new ApiKeyCredential(apiKey)));
```

### Anthropic (Claude)

```bash
dotnet add package Ananke.Orchestration.Anthropic
```

```csharp
using Ananke.Orchestration.Anthropic;

IStreamingAgentModel model = AnthropicAgentModel.Create(apiKey, "claude-sonnet-4-20250514");
```

### Google Gemini

```bash
dotnet add package Ananke.Orchestration.Google
```

```csharp
using Ananke.Orchestration.Google;

IStreamingAgentModel model = GeminiAgentModel.Create(apiKey, "gemini-2.5-pro");
```

### Local / Custom Endpoints

Any OpenAI-compatible endpoint (Ollama, LM Studio, vLLM, Azure OpenAI) works via
the `endpoint` parameter:

```csharp
// Ollama running locally
var model = OpenAIChatAgentModel.Create(
    apiKey:   "ollama",
    model:    "llama3.2",
    endpoint: new Uri("http://localhost:11434/v1"));
```

→ See [Guide 11 — Advanced Agent Features](11-advanced-agents.md) for the full
list of compatible providers and YAML configuration.

---

## Direct Model Call

The simplest way to use a model — no workflow needed:

```csharp
var request = new AgentRequest
{
    SystemPrompt = "You are a concise assistant. Answer in one or two sentences.",
    Messages = [AgentMessage.User("What is the capital of Japan?")]
};

var response = await model.GenerateAsync(request);
Console.WriteLine(response.Text);  // "The capital of Japan is Tokyo."
```

---

## AgentJob — LLMs in Workflows

`AgentJob` wraps an LLM call as a workflow job with typed input/output:

```csharp
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Workflows;

// Build agent jobs
var gatherJob = new AgentJob<ResearchState, GatherResult>
    .Builder("gather", model)
    .WithSystemPrompt("You are a research assistant. Return a JSON summary.")
    .WithPrompt(s => $"Look up the population of {s.Country}.")
    .WithTools(researchTools)
    .MapResult((s, r) => s with { Facts = r.Summary })
    .Build();

var analyzeJob = new AgentJob<ResearchState, AnalysisResult>
    .Builder("analyze", model)
    .WithSystemPrompt("You are a senior analyst. Respond as JSON.")
    .WithPrompt(s => $"Country: {s.Country}\nFacts: {s.Facts}\n\nProvide an insight.")
    .MapResult((s, r) => s with { Analysis = r.Insight })
    .Build();

// Wire into a workflow
var workflow = new Workflow<ResearchState>("country-research")
    .Job("gather", gatherJob)
    .Job("analyze", analyzeJob)
    .Chain("gather", "analyze")
    .Then("analyze", Workflow.End);

var result = await workflow.RunAsync(new ResearchState { Country = "Japan" });
Console.WriteLine(result.State.Analysis);

// State and response types
record ResearchState
{
    public string Country { get; init; } = "";
    public string? Facts { get; init; }
    public string? Analysis { get; init; }
}

record GatherResult { public string Summary { get; init; } = ""; }
record AnalysisResult { public string Insight { get; init; } = ""; }
```

### Structured Output

The second type parameter (`GatherResult`, `AnalysisResult`) tells the agent to
return structured JSON. Ananke deserializes the LLM response into your C# record
automatically.

---

## Capability-Based Model Routing

Route requests to the best model based on capabilities, cost, and intelligence:

```csharp
var miniProfile = new ModelProfile
{
    Name = "gpt-4.1-mini",
    Model = miniModel,
    Capabilities = ModelCapability.TextGeneration | ModelCapability.ToolCalling
                 | ModelCapability.StructuredOutput | ModelCapability.LargeContext,
    IntelligenceTier = 2,
    CostPer1KTokens = 0.40m,
    MaxContextTokens = 1_047_576,
    SpeedTier = 4
};

var fullProfile = new ModelProfile
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
};

var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
    .AddModel(miniProfile)
    .AddModel(fullProfile);
```

### Routing by Capabilities

```csharp
// Simple text → routes to gpt-4.1-mini (cheaper, meets requirements)
var simpleModel = router.Select(new AgentRequest
{
    SystemPrompt = "You are a concise assistant.",
    Messages = [AgentMessage.User("Explain hash tables in one sentence.")]
});

// Reasoning → routes to gpt-4.1 (higher intelligence tier)
var reasoningModel = router.Select(new AgentRequest
{
    SystemPrompt = "You are a senior architect.",
    Messages = [AgentMessage.User("Compare event sourcing vs CRUD.")]
}
.WithRequiredCapabilities(ModelCapability.Reasoning)
.WithMinIntelligence(3));
```

### Router in Workflows

Pass the router where you'd pass a model — `AgentJob` uses it to select the best
model per request:

```csharp
var gatherJob = new AgentJob<ResearchState, GatherResult>
    .Builder("gather", router)    // router instead of a single model
    .WithSystemPrompt("Use tools to gather data.")
    .WithPrompt(s => $"Look up {s.Country}")
    .WithTools(researchTools)
    .MapResult((s, r) => s with { Facts = r.Summary })
    .Build();
```

---

## Token-Level Streaming

Stream individual tokens as they arrive from the LLM:

```csharp
var request = new AgentRequest
{
    SystemPrompt = "You are a helpful assistant.",
    Messages = [AgentMessage.User("Tell me about Ananke.")]
};

await foreach (var chunk in model.GenerateStreamAsync(request))
{
    if (chunk.TextDelta is not null)
        Console.Write(chunk.TextDelta);
}
```

---

## Conversation Memory

Persist chat history across requests with `IConversationMemory`:

```csharp
using Ananke.Orchestration.Memory;

var memory = new InMemoryConversationMemory();

var workflow = StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are a helpful assistant.")
    .WithMemory(memory)
    .OnTextDelta(async delta => Console.Write(delta))
    .Build();
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [04 — Tools](04-tools.md) | Give agents callable functions |
| [05 — Streaming Chat](05-streaming-chat.md) | Build streaming chat UIs |
| [11 — Advanced Agents](11-advanced-agents.md) | Caching, resilience, local endpoints |

---

← [Back to Learning Path](../learning-path.md)
