<!-- topic: mcp-and-interop, tags: mcp, interop, a2a, protocol, server, client -->
# 12 — MCP & Interop

Expose tools and workflows as an [MCP](https://modelcontextprotocol.io/) server,
consume external MCP tools in agents, and use the A2A protocol for agent-to-agent
communication.

**Demo:** [McpServerDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/06-interop-and-channels/McpServerDemo)

---

## MCP Server — Expose Tools

Turn any `ToolKit` into MCP server capabilities with a single call:

```bash
dotnet add package Ananke.MCP
```

```csharp
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var mathTools = new ToolKit("math")
    .AddTool("add", "Adds two numbers", b => b
        .Param<double>("a", "First number")
        .Param<double>("b", "Second number")
        .OnExecute(args => ToolResult.Ok($"{args.Get<double>("a") + args.Get<double>("b")}")))
    .AddTool("multiply", "Multiplies two numbers", b => b
        .Param<double>("a", "First number")
        .Param<double>("b", "Second number")
        .OnExecute(args => ToolResult.Ok($"{args.Get<double>("a") * args.Get<double>("b")}")));

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "my-tools", Version = "1.0.0" };
    })
    .WithStdioServerTransport()
    .WithAnankeTools(mathTools);

await builder.Build().RunAsync();
```

MCP clients (VS Code Copilot, Claude Desktop, etc.) launch the process and
communicate over stdin/stdout. Nothing is exposed to the network.

---

## MCP Server — Expose Workflows

Expose a full workflow as an MCP tool:

```csharp
var pipeline = new Workflow<PipelineState>("data-pipeline")
    .Job("validate", (state, _) => Task.FromResult(state with
        { IsValid = !string.IsNullOrWhiteSpace(state.Input) }))
    .Job("enrich", (state, _) => Task.FromResult(state with
        { WordCount = state.Input?.Split(' ').Length ?? 0 }))
    .Job("format", (state, _) => Task.FromResult(state with
        { Output = state.Input!.ToUpperInvariant() }))
    .Chain("validate", "enrich", "format")
    .Then("format", Workflow.End);

builder.Services
    .AddMcpServer(options => { /* ... */ })
    .WithStdioServerTransport()
    .WithAnankeTools(mathTools, textTools)
    .WithAnankeWorkflow<PipelineState>(
        name:         "run_data_pipeline",
        description:  "Runs a 3-step data pipeline (validate → enrich → format).",
        workflow:     pipeline,
        stateFactory: args =>
        {
            var input = args.TryGetValue("input", out var el) ? el.GetString() ?? "" : "";
            return new PipelineState { Input = input };
        });
```

When the MCP client calls `run_data_pipeline`, all three steps execute and
the final state is returned as JSON.

---

## MCP Client — Consume External Tools

Import tools from any MCP server into a `ToolKit`:

```csharp
var toolkit = new ToolKit("external");
await toolkit.AddMcpServerToolsAsync(mcpClient);

// Use in any agent workflow
await StreamingChatWorkflow.Create("chat", model)
    .WithTools(toolkit)
    .OnTextDelta(async delta => Console.Write(delta))
    .Build()
    .RunAsync(state);
```

`AddMcpServerToolsAsync` is built on `McpToolInvoker`, which handles request
serialisation, response mapping, and error propagation for each external call.
You can also instantiate `McpToolInvoker` directly for custom transport scenarios:

```csharp
var invoker = new McpToolInvoker(mcpClient);
ToolResult result = await invoker.InvokeAsync("tool_name", arguments);
```

---

## A2A Protocol

The [Agent-to-Agent (A2A)](https://google.github.io/A2A/) protocol enables
communication between agents across services and frameworks.

### A2A Client — Call Remote Agents

```bash
dotnet add package Ananke.A2A
```

Call a remote A2A agent as if it were a local `IAgentModel`:

```csharp
using Ananke.A2A.Client;

var remoteAgent = new A2AAgentModel(new A2AAgentModelOptions
{
    AgentUrl = new Uri("https://remote-agent.example.com/.well-known/agent.json")
});

var response = await remoteAgent.GenerateAsync(new AgentRequest
{
    Messages = [AgentMessage.User("Analyze this data")]
});
```

### A2A Server — Expose Workflows

Expose Ananke workflows as A2A-compliant endpoints with `WorkflowTaskAdapter`, which bridges
a text-in/text-out processing function (typically a workflow run) to A2A's `TaskManager`:

```csharp
using A2A;
using Ananke.A2A.Server;

var taskManager = new TaskManager();
var adapter = new WorkflowTaskAdapter(async (text, ct) =>
{
    var result = await workflow.RunAsync(new MyState { Input = text }, ct);
    return result.FinalState.Output;
});

var agentCard = new AgentCardBuilder()
    .WithName("my-agent")
    .WithDescription("A data analysis agent")
    .WithSkillsFrom(toolkit)
    .Build("http://localhost:5100/agent");

adapter.Attach(taskManager, agentCard);
```

`Attach` wires `taskManager.OnMessageReceived` and the agent-card query callback. Mapping the
HTTP endpoint itself (JSON-RPC `message/send` dispatch and the `/.well-known/agent-card.json`
route) is a small amount of ASP.NET Minimal API code — see
[`A2AEndpoints.cs`](https://github.com/sevensamurai/Ananke/tree/main/src/demos/06-interop-and-channels/AgentToAgentProtocolDemo/A2AEndpoints.cs)
in the AgentToAgentProtocolDemo for a complete, runnable version.

---

## Tool Tags and Examples for A2A

`ToolDefinition.Tags` and `ToolDefinition.Examples` are forwarded to A2A
`AgentSkill` when tools are exposed via `AgentCardBuilder`:

```csharp
var tool = new ToolDefinition
{
    Name = "search_docs",
    Description = "Searches the engineering knowledge base",
    Tags = ["retrieval", "knowledge"],
    Examples = ["search_docs query='Raft consensus'"],
    Parameters = [new ToolParameter("query", "Search query")],
    Execute = async (args, ct) => ToolResult.Ok("results...")
};
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [13 — Design Tooling](13-design-tooling.md) | Text DSL, YAML manifests, Mermaid export |
| [14 — Testing](14-testing.md) | Test without infrastructure |

**Also see:** [AgentToAgentProtocolDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/06-interop-and-channels/AgentToAgentProtocolDemo) — a runnable A2A server with a matching Python client showing the raw JSON-RPC wire format.

---

← [Back to Learning Path](../learning-path.md)
