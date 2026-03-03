# Ananke.MCP

[![NuGet](https://img.shields.io/nuget/v/Ananke.MCP.svg)](https://www.nuget.org/packages/Ananke.MCP)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

[MCP](https://modelcontextprotocol.io/) (Model Context Protocol) integration for Ananke — expose `ToolKit` tools and `Workflow` executions as MCP server capabilities, and import tools from external MCP servers into `ToolKit` for agent use. Supports stdio and HTTP transports via the official C# MCP SDK.

## Install

```bash
dotnet add package Ananke.MCP
```

## Quick start

### Consume tools from an MCP server

```csharp
await using var client = await McpClient.CreateAsync(
    new StdioClientTransport(new StdioClientTransportOptions
    {
        Command = "npx", Arguments = ["-y", "@modelcontextprotocol/server-everything"]
    }));

var toolkit = await new ToolKit("remote")
    .AddMcpServerToolsAsync(client);

// Every tool from the remote server is now a ToolDefinition —
// use it in any AgentJob workflow exactly like local tools.
```

### Expose tools as an MCP server

```csharp
var toolkit = new ToolKit("stock")
    .AddTool("get_price", "Gets the stock price", GetPrice, "symbol", "Ticker symbol");

builder.Services.AddMcpServer(options => { options.ServerName = "my-server"; })
    .WithAnankeTools(toolkit);
```

### Expose a workflow

```csharp
builder.Services.AddMcpServer(options => { options.ServerName = "my-server"; })
    .WithAnankeWorkflow(
        name:         "run_pipeline",
        description:  "Runs the ETL pipeline and returns results",
        workflow:     etlWorkflow,
        stateFactory: args => new PipelineState { Input = args["input"].GetString()! });
```

## Features

- **MCP client** — `AddMcpServerToolsAsync(IMcpClient)` discovers remote tools and bridges them into `ToolKit` / `ToolDefinition` for transparent agent use
- **MCP server** — register any `ToolKit` or `Workflow<T>` as MCP server capabilities
- Supports both **stdio** and **HTTP** transports
- Compatible with Claude Desktop, VS Code Copilot, and any MCP client

## Requirements

- `Ananke.Orchestration` (transitive)
- `ModelContextProtocol` SDK ≥ 1.0.0

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
