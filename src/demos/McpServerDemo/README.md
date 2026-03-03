# McpServerDemo

A self-contained demo that exposes Ananke tools and a workflow as an **MCP server** 
— callable from VS Code Copilot, Claude Desktop, or any MCP-compatible client.

**No API keys required.** Everything runs locally with static data.

---

## What is an MCP server?

> **An MCP server is not a cloud server.** It is a **local process** that runs on your machine.

MCP (Model Context Protocol) is a standard for letting AI assistants call external tools.
When you configure an MCP client (VS Code, Claude Desktop, etc.) to use this demo, here's what happens:

```
┌─────────────────────────┐         stdin/stdout          ┌──────────────────────┐
│                         │  ◄──── JSON-RPC messages ───►  │                      │
│   VS Code / Claude      │                                │   McpServerDemo.exe  │
│   (MCP client)          │   Runs as a child process      │   (this project)     │
│                         │   on YOUR machine               │                      │
└─────────────────────────┘                                └──────────────────────┘
```

- The MCP client **launches the server process** when you start a chat session
- Communication happens over **stdin/stdout** (pipes) — not HTTP, not the network
- The server **dies when the client disconnects** — there is nothing to "deploy"
- No ports are opened, no firewall rules needed, no cloud involved

Think of it like a CLI tool that your AI assistant can talk to.

---

## What this demo exposes

### Individual tools (via `WithAnankeTools`)

| Tool | Description | Parameters |
|---|---|---|
| `add` | Adds two numbers | `a`, `b` |
| `multiply` | Multiplies two numbers | `a`, `b` |
| `word_count` | Counts words in text | `text` |
| `reverse` | Reverses a string | `text` |
| `uppercase` | Converts text to uppercase | `text` |
| `country_population` | Returns country population | `country` |
| `country_capital` | Returns capital city | `country` |

### Workflow-as-a-tool (via `WithAnankeWorkflow`)

| Tool | Description | Parameters |
|---|---|---|
| `run_data_pipeline` | Runs a 3-step pipeline: validate → enrich → format | `input` |

The workflow tool executes a full `Workflow<PipelineState>` graph and returns the final state as JSON. The MCP client's model doesn't know it's running a multi-step pipeline — it just calls one tool and gets a result.

---

## Setup

### Option A: One-click VS Code setup (recommended)

```bash
code demos/McpServerDemo/open_in_vscode
```

That's it. Open Copilot Chat and start asking questions. The MCP server config is pre-loaded — VS Code builds and launches the server automatically.

See [`open_in_vscode/README.md`](open_in_vscode/README.md) for troubleshooting.

### Option B: Manual setup

#### 1. Build

```bash
cd src
dotnet build demos/McpServerDemo/McpServerDemo.csproj
```

#### 2. Configure your MCP client

#### VS Code (GitHub Copilot)

Add to your **workspace** `.vscode/mcp.json` (create the file if it doesn't exist):

```json
{
  "servers": {
    "ananke-demo": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "${workspaceFolder}/src/demos/McpServerDemo/McpServerDemo.csproj"
      ]
    }
  }
}
```

Or add to your **user** `settings.json`:

```json
{
  "mcp": {
    "servers": {
      "ananke-demo": {
        "type": "stdio",
        "command": "dotnet",
        "args": [
          "run",
          "--project",
          "C:/path/to/Ananke/src/demos/McpServerDemo/McpServerDemo.csproj"
        ]
      }
    }
  }
}
```

#### Claude Desktop

Add to `claude_desktop_config.json` (located at `%APPDATA%\Claude\` on Windows, `~/Library/Application Support/Claude/` on macOS):

```json
{
  "mcpServers": {
    "ananke-demo": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/path/to/Ananke/src/demos/McpServerDemo/McpServerDemo.csproj"
      ]
    }
  }
}
```

#### 3. Use it

> **VS Code users:** Make sure you are in **Agent mode** (click the mode dropdown at the top of the chat panel and select **Agent**). In the default "Ask" mode, Copilot answers from its own knowledge and will not call MCP tools.

Open a chat in your MCP client and try:

- *"What's the population of Japan?"* → calls `country_population`
- *"Add 42 and 58"* → calls `add`
- *"Reverse the text 'hello world'"* → calls `reverse`
- *"Run the data pipeline with input 'the quick brown fox'"* → calls `run_data_pipeline`, returns full JSON state
- *"What's the capital of Brazil and what's 12 times 7?"* → calls `country_capital` and `multiply`

The AI model decides which tools to call based on your question. You'll see tool call indicators in the chat UI.

---

## How it works

```
Program.cs
│
├── ToolKit("math")         ──► add, multiply
├── ToolKit("text")         ──► word_count, reverse, uppercase
├── ToolKit("lookup")       ──► country_population, country_capital
│
├── Workflow("data-pipeline")
│   ├── validate  ──► enrich  ──► format  ──► __end__
│
└── Host.CreateEmptyApplicationBuilder()
    └── AddMcpServer()
        ├── .WithStdioServerTransport()
        ├── .WithAnankeTools(math, text, lookup)
        └── .WithAnankeWorkflow("run_data_pipeline", ...)
```

`Ananke.MCP` does the bridging:

- Each `ToolDefinition` in a `ToolKit` becomes an `McpServerTool` with auto-generated JSON Schema
- The `Workflow<PipelineState>` becomes a single `McpServerTool` that runs the full graph and returns the final state

The MCP client never knows about Ananke internals — it just sees standard MCP tools.

---

## FAQ

**Q: Do I need to deploy this somewhere?**
No. The MCP client launches it as a local child process. There is no deployment.

**Q: Does this open any network ports?**
No. Stdio transport uses stdin/stdout pipes. Nothing listens on the network.

**Q: Can I use HTTP transport instead?**
Yes — the MCP SDK supports HTTP/SSE via ASP.NET Core. 
That's useful for remote scenarios but not needed for local development. 
See the [MCP C# SDK docs](https://modelcontextprotocol.github.io/csharp-sdk/) for HTTP setup.

**Q: Do I need an API key?**
No. This demo uses only static data (hardcoded lookups, math, string operations). 
The MCP *client* (VS Code, Claude) uses its own model — this server just provides the tools.

**Q: Can I add my own tools?**
Yes — add more `ToolKit` instances or `Workflow` definitions in `Program.cs` 
and pass them to `.WithAnankeTools()` / `.WithAnankeWorkflow()`.
