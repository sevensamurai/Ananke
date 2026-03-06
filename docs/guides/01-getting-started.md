# 01 — Getting Started

Install Ananke, build your first workflow, and make your first LLM call.

**Demo:** [SimpleWorkflowDemo](../../src/demos/SimpleWorkflowDemo/)

---

## Installation

Install the meta-package to get everything:

```bash
dotnet add package Ananke
```

Or install only the packages you need:

```bash
dotnet add package Ananke.Orchestration            # core: workflows, agents, tools, knowledge
dotnet add package Ananke.Orchestration.OpenAI       # OpenAI chat + embeddings provider
```

> **Tip:** The `Ananke` meta-package pulls in all sub-packages. For production
> applications, install individual packages to keep your dependency tree lean.

---

## Your First Workflow

A workflow in Ananke is a directed graph of **jobs** connected by **edges**.
Each job receives a typed state, does work, and returns a new state.

```csharp
using Ananke.Orchestration;

// 1. Define your state — a plain C# record
record GreetingState
{
    public string Name { get; init; } = "";
    public string Greeting { get; init; } = "";
}

// 2. Build the workflow
var workflow = new Workflow<GreetingState>("hello")
    .Job("greet", (state, ct) =>
        Task.FromResult(state with { Greeting = $"Hello, {state.Name}!" }))
    .Then("greet", Workflow.End);

// 3. Run it
var result = await workflow.RunAsync(new GreetingState { Name = "World" });

Console.WriteLine(result.State.Greeting);   // "Hello, World!"
Console.WriteLine(result.Status);            // Completed
```

**What's happening:**
- `Workflow<T>` is generic over your state type — the compiler enforces type safety end-to-end.
- `.Job("greet", ...)` registers a named job with a delegate.
- `.Then("greet", Workflow.End)` connects the job to the terminal node.
- `.RunAsync()` executes the graph and returns a `WorkflowExecution<T>`.

---

## Chaining Multiple Jobs

Use `.Chain()` to wire a sequence of jobs in one call:

```csharp
var workflow = new Workflow<PipelineState>("pipeline")
    .Job("fetch",     async (state, ct) => state with { Raw = "raw data" })
    .Job("transform", async (state, ct) => state with { Clean = state.Raw.ToUpperInvariant() })
    .Job("publish",   async (state, ct) => state with { Done = true })
    .Chain("fetch", "transform", "publish")
    .Then("publish", Workflow.End);

var result = await workflow.RunAsync(new PipelineState());
// result.State.Done == true
```

---

## Your First LLM Call

Add an OpenAI model and use `StreamingChatWorkflow` for an interactive agent:

```bash
dotnet add package Ananke.Orchestration.OpenAI
```

```csharp
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.OpenAI;
using System.ClientModel;
using OpenAI.Chat;

// Create an LLM model
IStreamingAgentModel model = new OpenAIChatAgentModel(
    new ChatClient("gpt-4.1-mini", new ApiKeyCredential("your-api-key")));

// Build a streaming chat workflow
var messages = new List<AgentMessage> { AgentMessage.User("What is the capital of France?") };

var execution = await StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are a concise assistant. Answer in one sentence.")
    .OnTextDelta(delta => { Console.Write(delta); return Task.CompletedTask; })
    .Build()
    .RunAsync(new StreamingChatState { Messages = messages });

Console.WriteLine();
// Tokens stream to the console as they arrive
```

---

## Direct Model Call

For simple one-shot requests without a workflow:

```csharp
var request = new AgentRequest
{
    SystemPrompt = "You are a concise assistant.",
    Messages = [AgentMessage.User("What is 2 + 2?")]
};

var response = await model.GenerateAsync(request);
Console.WriteLine(response.Text);  // "4"
```

---

## Project Structure

A typical Ananke project looks like this:

```
MyProject/
├── Program.cs          # Workflow definition and execution
├── MyProject.csproj    # Package references
└── secrets.json        # API keys (excluded from source control)
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [02 — Workflows](02-workflows.md) | Conditional routing, fork/join parallelism, sub-workflows, streaming |
| [03 — Agents](03-agents.md) | LLM providers, structured output, model routing |
| [04 — Tools](04-tools.md) | Function calling for LLM agents |

---

← [Back to Learning Path](../learning.md)
