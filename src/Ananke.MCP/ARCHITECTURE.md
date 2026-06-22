# Ananke.MCP — Architecture

> Model Context Protocol integration — expose Ananke tools/workflows as
> MCP server capabilities and import external MCP tools.

## Role

Bidirectional MCP bridge:
1. **Export:** Expose `ToolKit` tools and `Workflow` executions as MCP server
   tools (callable by Claude Desktop, Cursor, or any MCP client).
2. **Import:** Discover tools from external MCP servers and add them to a
   `ToolKit` for agent use.

Supports stdio and HTTP transports via the official C# MCP SDK.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `ToolKitMcpExtensions` — `toolkit.AddMcpServerToolsAsync(client)`: discovers tools from a
   connected MCP client and registers them in a `ToolKit` — `src/Ananke.MCP/ToolKitMcpExtensions.cs`
2. `AnankeMcpServerBuilderExtensions` — `builder.WithAnankeTools(toolkit)`: registers `ToolKit`
   tools with the MCP server builder (the export path) — `src/Ananke.MCP/McpServerBuilderExtensions.cs`
3. `WorkflowToolAdapter` — wraps a `Workflow<TState>` as an MCP server tool — `src/Ananke.MCP/WorkflowToolAdapter.cs`
4. `McpToolInvoker` — invokes tools on an external MCP server and returns typed results, used
   when importing external tools into a `ToolKit` — `src/Ananke.MCP/McpToolInvoker.cs`

---

## Dependencies

- `Ananke.Orchestration` (project)
- `ModelContextProtocol` (NuGet — official C# MCP SDK)

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `ToolKitMcpExtensions` | Static class | `toolkit.AddMcpServerToolsAsync(client)` — discovers tools from a connected MCP client and registers them as `ToolDefinition` entries in the `ToolKit` | `src/Ananke.MCP/ToolKitMcpExtensions.cs` |
| `AnankeToolAdapter` | Class | Wraps a `ToolDefinition` as an MCP server tool handler | `src/Ananke.MCP/AnankeToolAdapter.cs` |
| `WorkflowToolAdapter` | Class | Wraps a `Workflow<TState>` as an MCP server tool | `src/Ananke.MCP/WorkflowToolAdapter.cs` |
| `AnankeMcpServerBuilderExtensions` | Static class | `builder.WithAnankeTools(toolkit)` — registers tools with MCP server builder | `src/Ananke.MCP/McpServerBuilderExtensions.cs` |
| `McpToolInvoker` | Class | Invokes tools registered on an external MCP server and returns typed results; used when importing external MCP tools into a `ToolKit` | `src/Ananke.MCP/McpToolInvoker.cs` |
