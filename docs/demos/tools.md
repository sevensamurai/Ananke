<!-- topic: demos-tools, tags: demo, tools, toolkit, function-calling, typed-parameters, async-tools, custom-tool, structured-output -->
# Demo — Tools

Register built-in tools and author custom tools that LLMs can call during agent workflows.

This walkthrough validates that typed tool parameters produce accurate JSON Schema for the model, that `ToolResult.Ok`/`Error` signals propagate correctly through the framework, and that async tools behave identically to sync ones.

→ **Further reading:** [04 — Tools](../guides/04-tools.md) · [Tools Reference](../reference/tools-reference.md)

---

## Scenario A — Built-in tool registration

Wire a `ToolKit` of utility functions into an agent and let the LLM decide when to call them.

```csharp
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Workflows;

// 1. Build the toolkit
var toolkit = new ToolKit("utilities")
    .AddTool("get_time", "Returns the current UTC time",
        () => DateTime.UtcNow.ToString("O"))

    .AddTool("lookup", "Looks up information about a topic",
        (string topic) => $"Here is what I know about {topic}: …",
        "topic", "The topic to look up")

    .AddTool("add", "Adds two numbers and returns the result", b => b
        .Param("a", "First number")
        .Param("b", "Second number")
        .OnExecute(args => ToolResult.Ok($"{double.Parse(args.Get("a")) + double.Parse(args.Get("b"))}")));

// 2. Wire the toolkit into an agent job
var agentJob = AgentJobFactory.Create<AssistantState>("assistant", model)
    .WithSystemPrompt("You are a helpful assistant. Use the available tools when appropriate.")
    .WithPrompt(s => s.UserMessage)
    .WithTools(toolkit)
    .MapResult((s, reply) => s with { Response = reply })
    .Build();

// 3. Run inside a workflow
var workflow = new Workflow<AssistantState>("assistant-workflow")
    .Job("assistant", agentJob);

var result = await workflow.RunAsync(new AssistantState
{
    UserMessage = "What time is it, and what is 42 + 58?"
});
Console.WriteLine(result.State.Response);
```

---

## Scenario B — Typed parameters (automatic JSON Schema)

Use generic overloads for correct type inference in the model's tool schema:

```csharp
var mathKit = new ToolKit("math")
    .AddTool<int>("square", "Squares an integer",
        (int n) => (n * n).ToString(),
        "value", "The integer to square")           // schema type → "integer"

    .AddTool("power", "Raises base to exponent", b => b
        .Param<double>("base", "The base value")        // schema type → "number"
        .Param<double>("exponent", "The exponent")
        .OnExecute(args =>
            ToolResult.Ok(Math.Pow(args.Get<double>("base"), args.Get<double>("exponent")).ToString("G"))));
```

---

## Scenario C — Async tool with real I/O

Every sync overload has an async variant. Use it for tools that perform network requests, file I/O, or any awaitable work:

```csharp
var webKit = new ToolKit("web")
    .AddTool("fetch", "Fetches a URL and returns a preview",
        async (string url) =>
        {
            using var http = new HttpClient();
            var html = await http.GetStringAsync(url);
            return html[..Math.Min(html.Length, 500)];   // first 500 chars
        },
        "url", "The URL to fetch");
```

---

## Scenario D — Custom tool with structured input/output

For complex payloads, accept and return JSON manually or use `System.Text.Json`:

```csharp
using System.Text.Json;

record SearchQuery(string Keywords, int MaxResults);
record SearchResult(string[] Items);

var searchKit = new ToolKit("search")
    .AddTool("semantic_search", "Runs a semantic search over the knowledge base",
        async (string queryJson) =>
        {
            var query = JsonSerializer.Deserialize<SearchQuery>(queryJson)!;
            var results = await knowledgeBase.SearchAsync(query.Keywords, query.MaxResults);
            return JsonSerializer.Serialize(new SearchResult(results.ToArray()));
        },
        "query", "JSON object with 'keywords' and 'maxResults' fields");
```

---

## Combining multiple toolkits

`WithTools` takes a single `ToolKit` — merge multiple kits into one first:

```csharp
var combinedTools = new ToolKit("combined").Merge(webKit).Merge(mathKit).Merge(searchKit);

var combinedJob = AgentJobFactory.Create<AppState>("agent", model)
    .WithSystemPrompt("You have access to web, math, and search tools.")
    .WithPrompt(s => s.Input)
    .WithTools(combinedTools)
    .MapResult((s, reply) => s with { Output = reply })
    .Build();
```

→ [Full Tools reference](../reference/tools-reference.md)
