<!-- topic: streaming-chat, tags: streaming, chat, sse, web, session, ui -->
# 05 — Streaming Chat

Build streaming chat experiences with `StreamingChatWorkflow`, SSE endpoints,
and web UI integration.

**Demo:** [AgenticWebDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/AgenticWebDemo)

---

## StreamingChatWorkflow

`StreamingChatWorkflow` is a pre-built workflow for chat UIs. It handles the
conversation loop (user → LLM → tool calls → LLM → response) with token-level
streaming and event callbacks.

### Basic Console Chat

```csharp
using Ananke.Orchestration.Agents;

var messages = new List<AgentMessage> { AgentMessage.User("What's the weather?") };

var execution = await StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are a helpful assistant.")
    .WithTools(weatherTools)
    .OnTextDelta(delta => { Console.Write(delta); return Task.CompletedTask; })
    .Build()
    .RunAsync(new StreamingChatState { Messages = messages });
```

### Event Callbacks

| Callback | When it fires |
|---|---|
| `.OnTextDelta(delta => ...)` | Each text token from the LLM |
| `.OnToolCall((name, args) => ...)` | When the LLM invokes a tool |
| `.OnToolResult((name, result) => ...)` | After a tool finishes executing |

```csharp
var workflow = StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are a stock market assistant.")
    .WithTools(stockTools)
    .OnTextDelta(async delta => Console.Write(delta))
    .OnToolCall(async (name, args) =>
        Console.WriteLine($"\n  ⚡ Calling: {name}({args})"))
    .OnToolResult(async (name, result) =>
        Console.WriteLine($"  → {name}: {result}"))
    .Build();
```

---

## Conversation Memory

Persist chat history across multiple requests using `IConversationMemory`:

```csharp
using Ananke.Orchestration.Memory;

// In-memory (dev/test)
var memory = new InMemoryConversationMemory();

// Or Redis (production)
// var memory = new RedisConversationMemory(redis, ttl: TimeSpan.FromHours(1));

var workflow = StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are a helpful assistant.")
    .WithMemory(memory)
    .OnTextDelta(async delta => Console.Write(delta))
    .Build();
```

With memory enabled, the workflow automatically loads previous messages for the
conversation and appends new exchanges after each turn.

---

## SSE Web Endpoint

Expose the streaming chat as an HTTP Server-Sent Events endpoint for web UIs:

```csharp
// In an ASP.NET Core minimal API
app.MapPost("/api/chat", async (ChatRequest req, HttpContext ctx) =>
{
    ctx.Response.ContentType = "text/event-stream";
    var writer = new StreamWriter(ctx.Response.Body);

    var messages = new List<AgentMessage> { AgentMessage.User(req.Message) };

    await StreamingChatWorkflow.Create("web-chat", model)
        .WithSystemPrompt("You are a helpful assistant.")
        .WithTools(tools)
        .WithMemory(memory)
        .OnTextDelta(async delta =>
        {
            await writer.WriteLineAsync($"data: {delta}");
            await writer.WriteLineAsync();
            await writer.FlushAsync();
        })
        .Build()
        .RunAsync(new StreamingChatState { Messages = messages });

    await writer.WriteLineAsync("data: [DONE]");
    await writer.FlushAsync();
});
```

---

## Interactive Console Loop

Build a REPL-style chat that maintains context:

```csharp
var memory = new InMemoryConversationMemory();

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(input)) continue;
    if (input == "quit") break;

    var messages = new List<AgentMessage> { AgentMessage.User(input) };

    await StreamingChatWorkflow.Create("chat", model)
        .WithSystemPrompt("You are a concise assistant.")
        .WithTools(tools)
        .WithMemory(memory)
        .OnTextDelta(delta => { Console.Write(delta); return Task.CompletedTask; })
        .Build()
        .RunAsync(new StreamingChatState { Messages = messages });

    Console.WriteLine();
}
```

---

## OpenTelemetry Integration

Add distributed tracing to your streaming chat:

```csharp
using Ananke.Abstractions.Tracing;
using Ananke.OpenTelemetry;

services.AddTracingPipeline(o =>
{
    o.ServiceName = "my-chat-app";
    o.ServiceVersion = "1.0.0";
    o.UseOtlp(endpoint, $"Authorization=Bearer {token}");
});

var tracer = sp.GetRequiredService<IWorkflowTracer>();

var execution = await StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are a helpful assistant.")
    .WithTools(tools)
    .OnTextDelta(async delta => Console.Write(delta))
    .Build()
    .UseTracing(tracer)
    .RunAsync(new StreamingChatState { Messages = messages });
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [06 — Memory](06-memory.md) | Long-term knowledge with vector search |
| [07 — Human-in-the-Loop](07-human-in-the-loop.md) | Pause and resume workflows for approval |

---

← [Back to Learning Path](../learning-path.md)
