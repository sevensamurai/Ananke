<!-- topic: tools, tags: tools, toolkit, function-calling, parameters, tool-result -->
# 04 — Tools

Define callable functions that LLMs can invoke during agent workflows using
`ToolKit`, typed parameters, and explicit success/error signaling.

**Demo:** [BasicAgentDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/BasicAgentDemo)

→ **Full API reference:** [Tools & ToolKit Reference](../reference/tools-reference.md)

---

## Core Concepts

A **tool** is a named function with a description and typed parameters. The LLM
reads the description and parameter schema to decide when and how to call it.
Tools are grouped into a **ToolKit** — a named collection you wire into agents.

---

## Creating a ToolKit

### No parameters

```csharp
using Ananke.Orchestration.Tools;

var toolkit = new ToolKit("utils")
    .AddTool("ping", "Returns pong", () => "pong")
    .AddTool("time", "Current UTC time", () => DateTime.UtcNow.ToString("O"));
```

### Single parameter

```csharp
var toolkit = new ToolKit("search")
    .AddTool("lookup", "Looks up a term",
        (string query) => $"Results for: {query}",
        "query", "The search query");
```

### Two or more parameters

The convenience overloads above only cover 0 or 1 parameters. For 2+ parameters, use the
`ToolBuilder` callback:

```csharp
var toolkit = new ToolKit("math")
    .AddTool("add", "Adds two numbers", b => b
        .Param("a", "First number")
        .Param("b", "Second number")
        .OnExecute(args =>
        {
            var sum = double.Parse(args.Get("a")) + double.Parse(args.Get("b"));
            return ToolResult.Ok($"{sum}");
        }));
```

### Typed parameters

Use generic overloads for automatic JSON Schema type inference:

```csharp
var toolkit = new ToolKit("math")
    .AddTool<int>("square", "Squares a number",
        (int n) => (n * n).ToString(),
        "value", "The number to square")           // → schema type "integer"
    .AddTool("multiply", "Multiplies two numbers", b => b
        .Param<double>("a", "First number")        // → "number"
        .Param<double>("b", "Second number")       // → "number"
        .OnExecute(args =>
            ToolResult.Ok($"{args.Get<double>("a") * args.Get<double>("b")}")));
```

There's no generic `AddTool<T1, T2>` overload — `Param<T>` per parameter on the builder is how
multiple typed parameters are declared.

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

---

## ToolResult — Success and Error

Tools return `ToolResult` to signal success or failure explicitly:

```csharp
private static ToolResult BuyShares(string symbol, string quantity)
{
    if (!int.TryParse(quantity, out var qty) || qty <= 0)
        return ToolResult.Error($"Invalid quantity: {quantity}");

    if (!StockData.TryGetValue(symbol, out var data))
        return ToolResult.Error($"Unknown symbol: {symbol}");

    // ... execute trade ...
    return ToolResult.Ok($"Bought {qty} shares of {symbol} at ${data.Price}");
}
```

There's also an implicit conversion from `string` → `ToolResult.Ok`:

```csharp
.AddTool("greet", "Says hello", (string name) => $"Hello, {name}!")
// ↑ The string return is automatically wrapped in ToolResult.Ok
```

---

## Writing Effective Descriptions

The LLM reads your tool descriptions and parameter descriptions to decide
when and how to invoke them. Good descriptions dramatically improve accuracy.

### Tool description tips

```csharp
// ❌ Vague
.AddTool("search", "Searches things", ...)

// ✅ Specific
.AddTool("search_knowledge",
    "Searches the indexed engineering reference materials for information. " +
    "Use this when the user asks about previously indexed documents.",
    ...)
```

### Parameter examples

Use `ToolParameter.Examples` to guide the LLM on expected formats:

```csharp
new ToolParameter("ticker", "Stock ticker symbol",
    Examples: ["AAPL", "GOOGL", "MSFT"])
// LLM consistently produces uppercase ticker format

new ToolParameter("date", "The date to query (ISO 8601 format)",
    Examples: ["2024-01-15", "2024-12-31"])
// LLM produces consistent date format
```

---

## Wiring Tools into Agents

### With StreamingChatWorkflow

```csharp
var execution = await StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are a stock market assistant.")
    .WithTools(stockTools)
    .OnTextDelta(delta => { Console.Write(delta); return Task.CompletedTask; })
    .OnToolResult((name, result) =>
    {
        Console.WriteLine($"\n  [{name}] {result}");
        return Task.CompletedTask;
    })
    .Build()
    .RunAsync(new StreamingChatState { Messages = messages });
```

### With AgentJob

```csharp
var job = new AgentJob<MyState, MyResponse>
    .Builder("gather", model)
    .WithSystemPrompt("Use tools to gather data.")
    .WithPrompt(s => s.Query)
    .WithTools(researchTools)
    .WithMaxToolRounds(5)
    .MapResult((s, r) => s with { Result = r.Text })
    .Build();
```

---

## Merging ToolKits

Combine multiple toolkits into one:

```csharp
var combined = new ToolKit("all")
    .Merge(searchTools)
    .Merge(mathTools)
    .Merge(webTools);
```

If both kits contain a tool with the same name, the merged kit's tool wins.

---

## Real-World Example

From the [BasicAgentDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/01-foundations/BasicAgentDemo):

```csharp
var stockTools = new ToolKit("stock")
    .AddTool(
        "get_stock_price",
        "Gets the current stock price, daily change, and volume for a given ticker symbol.",
        GetStockPrice,
        "symbol", "The stock ticker symbol (e.g. AAPL, MSFT)")
    .AddTool("buy_shares",
        "Buys a specified number of shares at the current market price.", b => b
        .Param("symbol", "The stock ticker symbol (e.g. AAPL, MSFT)")
        .Param("quantity", "The number of shares to buy")
        .OnExecute(args => BuyShares(args.Get("symbol"), args.Get("quantity"))));
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [05 — Streaming Chat](05-streaming-chat.md) | Build a streaming chat UI with tools |
| [06 — Memory](06-memory.md) | Long-term knowledge pipeline |
| [12 — MCP & Interop](12-mcp-and-interop.md) | Import/export tools via MCP |
| [15 — Empirical Memory](15-empirical-memory.md) | `IToolMemory` for health tracking and semantic recall |
| [16 — Agentic Patterns § Smart Tool Router](16-agentic-patterns.md#smart-tool-router) | Narrow the tool window with a multi-stage routing pipeline |

---

← [Back to Learning Path](../learning-path.md)
