<!-- topic: quickstart, tags: install, quickstart, workflow, agent, openai, anthropic, google -->
# Quickstart

Build a minimal agentic workflow in one file.

This path is intentionally small: one model, one optional `ToolKit`, one `AgentJob`, one `Workflow<T>`. You do **not** need the state machine, distributed infrastructure, checkpointing, or human-in-the-loop pieces to get started.

→ **Full guide:** [01 — Getting Started](guides/01-getting-started.md)

---

## Install The Smallest Useful Set

If you want the lean path, install the orchestration core plus one provider package:

```bash
dotnet add package Ananke.Orchestration
dotnet add package Ananke.Orchestration.OpenAI
```

If you prefer the everything-included path while exploring:

```bash
dotnet add package Ananke
```

OpenAI is only the example provider below. You can swap in Anthropic, Google Gemini, or any OpenAI-compatible endpoint later without changing the workflow shape.

---

## One-File Agentic Workflow

```csharp
using Ananke.Abstractions.Agents;
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using OpenAI.Chat;
using System.ClientModel;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set OPENAI_API_KEY first.");

IStreamingAgentModel model = new OpenAIChatAgentModel(
    new ChatClient("gpt-4.1-mini", new ApiKeyCredential(apiKey)));

var tools = new ToolKit("assistant-tools")
    .AddTool("get_weather", "Gets the current weather for a city.",
        (string city) => ToolResult.Ok($"Sunny, 22 C in {city}"),
        "city", "City name")
    .AddTool("get_time", "Gets the current UTC time.",
        () => ToolResult.Ok(DateTime.UtcNow.ToString("O")));

var agent = AgentJobFactory.Create<AssistantState>("assistant", model)
    .WithSystemPrompt(
        "You are a concise assistant. Use tools when they help. " +
        "If no tool is needed, answer directly.")
    .WithPrompt(state => state.UserInput)
    .WithTools(tools) // Optional: remove this line if you do not need tool calling yet.
    .MapResult((state, text) => state with { Output = text })
    .Build();

var workflow = new Workflow<AssistantState>("quickstart")
    .Job("assistant", agent);

var result = await workflow.RunAsync(new AssistantState
{
    UserInput = "What is the weather in Seattle, and what time is it in UTC?"
});

Console.WriteLine(result.State.Output);
Console.WriteLine(result.Status);

record AssistantState
{
    public string UserInput { get; init; } = "";
    public string Output { get; init; } = "";
}
```

This is already an agentic workflow:
- `ToolKit` exposes callable functions to the model.
- `AgentJob` wraps the model as a workflow job.
- `Workflow<T>` gives you the same graph abstraction you will use for larger multi-step flows.
- `WithTools(tools)` is optional. Remove it if you want a plain model-backed job first.

---

## Swap The Provider, Keep The Workflow

Only the model creation changes.

### Anthropic (Claude)

```bash
dotnet add package Ananke.Orchestration.Anthropic
```

```csharp
using Ananke.Orchestration.Anthropic;

IStreamingAgentModel model = AnthropicAgentModel.Create(
    apiKey,
    "claude-sonnet-4-20250514");
```

### Google Gemini

```bash
dotnet add package Ananke.Orchestration.Google
```

```csharp
using Ananke.Orchestration.Google;

IStreamingAgentModel model = GeminiAgentModel.Create(
    apiKey,
    "gemini-2.5-pro");
```

### Local Or OpenAI-Compatible Endpoints

```csharp
var model = OpenAIChatAgentModel.Create(
    apiKey: "ollama",
    model: "llama3.2",
    endpoint: new Uri("http://localhost:11434/v1"));
```

That includes Ollama, LM Studio, vLLM, and Azure OpenAI-style endpoints.

---

## Use `nnke` To Bootstrap It

If you do not want to hand-write the first project, `nnke` can help you get to the same code-first starting point faster.

### Command line

```bash
dotnet tool install -g nnke
nnke new workflow MyAssistant
```

After that, `nnke` is useful for the inner loop as well:

```bash
nnke inspect . --json
nnke docs --search "AgentJob ToolKit" --json
nnke patterns --json
```

That gives you a scaffolded project, machine-readable diagnostics, and quick access to the framework docs while you shape the workflow.

### MCP / agentic mode

`nnke` can also run as an MCP server so Copilot, Claude, or another agentic editor can call the same capabilities directly:

```bash
nnke mcp-server
```

That is useful when you want an assistant to help refine the workflow, inspect the project, validate topology, or search the Ananke docs without guessing.

→ See [nnke Tool Companion](cli/nnke-tool.md) and [CLI Overview](cli/overview.md).

---

## What To Add Next

Once this runs, the usual next steps are:

| Goal | Start here |
|---|---|
| Conditional routing and multi-step graphs | [02 — Workflows](guides/02-workflows.md) |
| Provider options and structured output | [03 — Agents](guides/03-agents.md) |
| Better tool design and typed parameters | [04 — Tools](guides/04-tools.md) |
| Streaming chat instead of one-shot workflow runs | [05 — Streaming Chat](guides/05-streaming-chat.md) |
| Human approvals and pause/resume | [07 — Human-in-the-Loop](guides/07-human-in-the-loop.md) |
| Full learning path | [Learning Path](learning-path.md) |
