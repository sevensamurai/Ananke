<!-- topic: tools-reference, tags: tools, toolkit, api, parameters, function-calling -->
# Tools & ToolKit Reference

Ananke's tool system lets you define executable functions that LLMs can call during agent workflows. Tools are grouped into named `ToolKit` collections and wired into `AgentJob`, `StreamingChatWorkflow`, or exposed externally via MCP and A2A.

## Core types

### `ToolParameter`

Describes a single parameter accepted by a tool.

```csharp
public record ToolParameter(
    string Name,             // JSON property key
    string Description,      // sent to the LLM
    string JsonType = "string",
    IReadOnlyList<string>? Examples = null);
```

| Property | Purpose |
|---|---|
| `Name` | The JSON property key in the function-calling schema |
| `Description` | Human-readable text the LLM reads to understand the parameter |
| `JsonType` | JSON Schema type: `"string"`, `"integer"`, `"number"`, `"boolean"` |
| `Examples` | Sample values emitted as the JSON Schema `examples` annotation |

**`Examples`** is the most impactful field for LLM accuracy. It's emitted directly into the JSON Schema that all providers (OpenAI, Anthropic, Google) pass to the model:

```json
{
  "query": {
    "type": "string",
    "description": "The search query",
    "examples": ["distributed consensus algorithms", "Raft vs Paxos"]
  }
}
```

Use examples when:
- The parameter is **ambiguous** — "query" could mean SQL, natural language, or regex
- The parameter is **format-sensitive** — dates, IDs, enum-like strings
- The LLM consistently produces **wrong formats** for a parameter

### `ToolDefinition`

A named, executable tool with typed parameters.

```csharp
public record ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<ToolParameter> Parameters { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Examples { get; init; } = [];
    public required Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> Execute { get; init; }
}
```

| Property | Purpose |
|---|---|
| `Tags` | Keywords for categorisation, filtering, and discovery. Forwarded to A2A `AgentSkill.Tags` when tools are exposed via `AgentCardBuilder` |
| `Examples` | Tool-level usage examples (full invocation descriptions). Useful for documentation and A2A `AgentSkill.Examples` |

### `ToolResult`

```csharp
public readonly record struct ToolResult(string Value, bool IsError);
```

Return `ToolResult.Ok(value)` or `ToolResult.Error(message)`. There's also an implicit conversion from `string` → `ToolResult.Ok`.

## ToolKit

A named collection of tools. Build once, share across agents.

### Basic tools (no parameters)

```csharp
var toolkit = new ToolKit("utils")
    .AddTool("ping", "Returns pong", () => "pong")
    .AddTool("time", "Current UTC time", () => DateTime.UtcNow.ToString("O"));
```

### Single parameter

```csharp
var toolkit = new ToolKit("search")
    .AddTool("lookup", "Looks up a term", (string query) => $"Results for: {query}",
        "query", "The search query");
```

### Two parameters

```csharp
var toolkit = new ToolKit("math")
    .AddTool("add", "Adds two numbers",
        (string a, string b) => $"{double.Parse(a) + double.Parse(b)}",
        ("a", "First number"), ("b", "Second number"));
```

### Typed parameters

Automatic JSON deserialization and schema type inference:

```csharp
var toolkit = new ToolKit("math")
    .AddTool<int>("square", "Squares a number",
        (int n) => (n * n).ToString(),
        "value", "The number to square")           // → schema type "integer"
    .AddTool<double, bool>("format", "Formats a number",
        (double n, bool round) => round ? Math.Round(n).ToString() : n.ToString(),
        ("number", "The number"),                   // → "number"
        ("round", "Whether to round"));             // → "boolean"
```

### Async tools

Every overload has an async variant:

```csharp
var toolkit = new ToolKit("web")
    .AddTool("fetch", "Fetches a URL",
        async (string url) =>
        {
            var html = await httpClient.GetStringAsync(url);
            return html[..Math.Min(html.Length, 500)];
        },
        "url", "The URL to fetch");
```

### Pre-built ToolDefinition

For advanced scenarios — tags, examples, or tools bridged from external sources:

```csharp
var tool = new ToolDefinition
{
    Name = "search_docs",
    Description = "Searches the engineering knowledge base",
    Tags = ["retrieval", "knowledge"],
    Examples = ["search_docs query='Raft consensus'", "search_docs query='API rate limiting'"],
    Parameters = [
        new ToolParameter("query", "Natural language search query",
            Examples: ["distributed consensus algorithms", "how does circuit breaking work"])
    ],
    Execute = async (args, ct) =>
    {
        var query = args["query"]?.ToString() ?? "";
        var results = await knowledgeStore.SearchAsync(query, ct: ct);
        return ToolResult.Ok(string.Join("\n", results.Select(r => r.Text)));
    }
};

var toolkit = new ToolKit("knowledge").AddTool(tool);
```

### Merging toolkits

```csharp
var combined = new ToolKit("all")
    .Merge(searchTools)
    .Merge(mathTools)
    .Merge(webTools);
```

If both kits contain a tool with the same name, the merged kit's tool wins.

## Parameter examples in practice

### Without examples

```csharp
new ToolParameter("date", "The date to query")
// LLM might produce: "today", "2024-01-15", "Jan 15", "15/01/2024"
```

### With examples

```csharp
new ToolParameter("date", "The date to query (ISO 8601 format)",
    Examples: ["2024-01-15", "2024-12-31"])
// LLM consistently produces: "2024-06-20"
```

### Enum-like values

```csharp
new ToolParameter("priority", "Task priority level",
    Examples: ["low", "medium", "high", "critical"])
// LLM picks from the examples rather than inventing values
```

### Format patterns

```csharp
new ToolParameter("ticker", "Stock ticker symbol",
    Examples: ["AAPL", "GOOGL", "MSFT"])
// LLM produces uppercase ticker format
```

## How tools reach the LLM

```
ToolParameter.Examples
        ↓
ToolDefinition.ParametersJsonSchema   →  JSON Schema with "examples" annotation
        ↓
AgentTool(Name, Description, Schema)  →  sent to LLM provider
        ↓
OpenAI: ChatTool.CreateFunctionTool(name, desc, BinaryData.FromString(schema))
Anthropic: new Tool { Name, Description, InputSchema = deserialize(schema) }
Google: same pattern via Ananke.Orchestration.Google
```

All providers pass the schema through unchanged — the `examples` annotation is part of the JSON Schema spec and respected by all major LLMs.

## Integration with MCP and A2A

| Integration | What happens with Tags / Examples |
|---|---|
| **MCP** (`Ananke.MCP`) | `AnankeToolAdapter` emits the full `ParametersJsonSchema` including `examples` — MCP clients receive them in `tool/list` |
| **A2A** (`Ananke.A2A`) | `AgentCardBuilder.WithSkillsFrom(toolkit)` maps `ToolDefinition.Tags` → `AgentSkill.Tags` and `ToolDefinition.Examples` → `AgentSkill.Examples` for agent discovery |
