# Ananke.A2A

[![NuGet](https://img.shields.io/nuget/v/Ananke.A2A.svg)](https://www.nuget.org/packages/Ananke.A2A)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

[A2A](https://a2a-protocol.org/) (Agent-to-Agent) protocol integration for Ananke — call remote A2A agents as `IAgentModel` implementations and expose Ananke workflows as A2A-compliant endpoints. Enables cross-framework agent interoperability alongside [MCP](https://modelcontextprotocol.io/) tool connectivity.

> **A2A** is for agent-to-agent communication. **MCP** is for agent-to-tool communication. Together they cover the full interoperability spectrum.

## Install

```bash
dotnet add package Ananke.A2A
```

## Quick start — Client

Use any remote A2A agent as a drop-in `IAgentModel`:

```csharp
using Ananke.A2A.Client;
using Ananke.Orchestration.Agents;

var model = new A2AAgentModel(new A2AAgentModelOptions
{
    AgentUrl = new Uri("http://localhost:5100/echo")
});

// Use it like any other Ananke model — in workflows, AgentJobs, or directly
var response = await model.GenerateAsync(new AgentRequest
{
    Messages = [AgentMessage.User("Hello, remote agent!")]
});

Console.WriteLine(response.Text);
```

### Streaming

`A2AAgentModel` implements `IStreamingAgentModel`, so it works with `StreamingChatWorkflow`:

```csharp
await StreamingChatWorkflow.Create("remote-chat", model)
    .WithSystemPrompt("You are a helpful assistant.")
    .OnTextDelta(async delta => Console.Write(delta))
    .RunAsync([AgentMessage.User("Explain the A2A protocol")]);
```

### Agent discovery

Resolve an agent's capabilities before connecting:

```csharp
var discovery = new A2AAgentDiscovery();
var info = await discovery.DiscoverAsync(new Uri("http://localhost:5100/"));

Console.WriteLine($"Agent: {info.Name} v{info.Version}");
Console.WriteLine($"Streaming: {info.SupportsStreaming}");
Console.WriteLine($"Skills: {string.Join(", ", info.Skills.Select(s => s.Name))}");
```

## Quick start — Server

Expose any processing function as an A2A agent:

```csharp
using A2A;
using A2A.AspNetCore;
using Ananke.A2A.Server;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Build the agent card
var card = new AgentCardBuilder()
    .WithName("Research Agent")
    .WithDescription("Answers research questions")
    .WithVersion("1.0.0")
    .WithSkillsFrom(myToolKit)        // maps ToolDefinition -> AgentSkill
    .Build("http://localhost:5100/research");

// Bridge to a TaskManager
var taskManager = new TaskManager();
var adapter = new WorkflowTaskAdapter(async (text, ct) =>
{
    // Run your Ananke workflow, agent job, or any logic here
    var result = await workflow.RunAsync(new MyState { Input = text }, ct);
    return result.FinalState.Output;
});

adapter.Attach(taskManager, card);
app.MapA2A(taskManager, "/research");
app.Run();
```

### Wrap an existing IAgentModel

Expose an Ananke model directly — no manual wiring needed:

```csharp
var adapter = WorkflowTaskAdapter.FromAgentModel(model, systemPrompt: "You are a research analyst.");
adapter.Attach(taskManager, card);
```

## Handoff channel

Use A2A as a transport for `HandoffJob` — existing workflows delegate to remote agents with zero code changes:

```csharp
using Ananke.A2A.Channels;

var channel = new A2AHandoffChannel(new Uri("http://remote-agent:5100/a2a"));

var workflow = new Workflow<MyState>("pipeline")
    .Job("local-step", localJob)
    .Job("remote-step", Handoff.To<MyState, Request, Response>("topic", channel)
        .CreateMessage(s => new Request { Query = s.Input })
        .MapResult((s, r) => s with { Output = r.Answer })
        .Build())
    .Chain("local-step", "remote-step");
```

## Features

| Component | What it does |
|---|---|
| `A2AAgentModel` | `IStreamingAgentModel` that delegates to a remote A2A agent |
| `A2AAgentDiscovery` | Resolves `AgentCard` metadata into Ananke-friendly `A2AAgentCardInfo` |
| `AgentCardBuilder` | Fluent builder for `AgentCard` from Ananke `ToolKit` and workflow metadata |
| `WorkflowTaskAdapter` | Bridges `TaskManager` callbacks to any processing function or `IAgentModel` |
| `A2AHandoffChannel` | `IHandoffChannel` implementation over A2A for transparent remote delegation |

## Requirements

- `Ananke.Orchestration` (transitive)
- `A2A` SDK >= 0.3.3-preview
- `A2A.AspNetCore` (server-side only — for `MapA2A()`)

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)