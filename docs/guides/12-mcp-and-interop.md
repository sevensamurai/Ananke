<!-- topic: mcp-and-interop, tags: mcp, interop, a2a, protocol, server, client -->
# 12 — MCP & Interop

Expose tools and workflows as an [MCP](https://modelcontextprotocol.io/) server,
consume external MCP tools in agents, and use the A2A protocol for agent-to-agent
communication.

**Demo:** [McpServerDemo](../../src/demos/McpServerDemo/)

---

## MCP Server — Expose Tools

Turn any `ToolKit` into MCP server capabilities with a single call:

```bash
dotnet add package Ananke.MCP
```

```csharp
using Ananke.Orchestration.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var mathTools = new ToolKit("math")
    .AddTool<double, double>("add", "Adds two numbers",
        (a, b) => $"{a + b}",
        ("a", "First number"), ("b", "Second number"))
    .AddTool<double, double>("multiply", "Multiplies two numbers",
        (a, b) => $"{a * b}",
        ("a", "First number"), ("b", "Second number"));

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
using Ananke.A2A;

var remoteAgent = new A2AAgentModel(new Uri("https://remote-agent.example.com/.well-known/agent.json"));

var response = await remoteAgent.GenerateAsync(new AgentRequest
{
    Messages = [AgentMessage.User("Analyze this data")]
});
```

### A2A Server — Expose Workflows

Expose Ananke workflows as A2A-compliant endpoints:

```csharp
using Ananke.A2A;

builder.Services.AddA2AServer(options =>
{
    options.AgentCard = new AgentCardBuilder()
        .WithName("my-agent")
        .WithDescription("A data analysis agent")
        .WithSkills(toolkit)
        .Build();
});
```

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

---

← [Back to Learning Path](../learning.md)
