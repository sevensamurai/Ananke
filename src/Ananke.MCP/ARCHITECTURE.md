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

## Dependencies

- `Ananke.Orchestration` (project)
- `ModelContextProtocol` (NuGet — official C# MCP SDK)

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `ToolKitMcpExtensions` | Static class | `toolkit.ToMcpTools()` — converts `ToolKit` tools to MCP tool definitions |
| `AnankeToolAdapter` | Class | Wraps a `ToolDefinition` as an MCP server tool handler |
| `WorkflowToolAdapter` | Class | Wraps a `Workflow<TState>` as an MCP server tool |
| `McpServerBuilderExtensions` | Static class | `builder.AddAnankeTools(toolkit)` — registers tools with MCP server builder |
