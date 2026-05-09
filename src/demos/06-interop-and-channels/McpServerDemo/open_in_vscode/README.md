# Try the Ananke MCP Demo

You opened this folder in VS Code — the MCP server is already configured.

## Quick start

1. **Open Copilot Chat** (`Ctrl+Shift+I` / `Cmd+Shift+I`)
2. **Switch to Agent mode** — click the mode dropdown at the top of the chat panel and select **Agent** (not "Ask" or "Edit")
3. You should see a **tools icon** (🔧) in the chat — click it to confirm `ananke-demo` is listed
4. If VS Code shows a prompt to "Start" the MCP server, click **Start**
5. Ask a question:
   - *"What's the population of Japan?"*
   - *"Add 42 and 58"*
   - *"Run the data pipeline with input 'hello world'"*

That's it. The server builds and starts automatically on the first chat that uses a tool.

> **⚠️ Agent mode is required.** In the default "Ask" mode, Copilot answers from its own knowledge and will not call MCP tools. You must select **Agent** mode for tool calls to work.

## What's happening

The `.vscode/mcp.json` in this folder tells VS Code:

> *"There's an MCP server called `ananke-demo`. To start it, run `dotnet run --project ../McpServerDemo.csproj`."*

VS Code launches the server as a child process, communicates over stdin/stdout, and shuts it down when you close the chat. Nothing is deployed, no ports are opened, no cloud involved.

## Troubleshooting

| Problem | Fix |
|---|---|
| Model answers from its own knowledge instead of calling tools | **Switch to Agent mode** — click the mode dropdown at the top of the chat panel and select **Agent** |
| No tools icon in chat | Make sure you opened **this folder** (`open_in_vscode/`) in VS Code, not the parent |
| "Server failed to start" | Run `dotnet build ../McpServerDemo.csproj` in a terminal first to check for build errors |
| Tools not appearing | Click the tools icon → check that `ananke-demo` shows as "Running" |
