# Ananke.AspNetCore

[![NuGet](https://img.shields.io/nuget/v/Ananke.AspNetCore.svg)](https://www.nuget.org/packages/Ananke.AspNetCore)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

ASP.NET Core integration helpers for Ananke — SSE streaming, `ChatSessionEvent`-to-SSE bridging, state-machine endpoint utilities, provider configuration, and session management.

## Install

```bash
dotnet add package Ananke.AspNetCore
```

## Quick start

### SSE streaming

```csharp
using Ananke.AspNetCore.Sse;

app.MapPost("/api/chat", async (ChatRequest request, HttpContext context, CancellationToken ct) =>
{
    // One call sets Content-Type + Cache-Control headers
    context.Response.EnableSse();

    // Write named SSE events with JSON data
    await context.Response.WriteSseAsync("session", new { sessionId = "abc" });
    await context.Response.WriteSseAsync("delta", new { text = "Hello!" });
    await context.Response.WriteSseAsync("done", new { text = "Hello!" });
});
```

### ChatSessionEvent → SSE bridge

Pipe a `StreamingChatWorkflow` event stream directly to an HTTP response:

```csharp
using Ananke.AspNetCore.Sse;

var events = StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are helpful.")
    .BuildStream(messages, ct);

await events.WriteSseAsync(context.Response);
```

Or via a delegate (useful for session-based scenarios where the writer is re-bound per request):

```csharp
await events.WriteSseAsync(
    writeSse: myWriteDelegate,
    onError: msg => logger.LogError("Error: {Msg}", msg));
```

Event mapping:

| `ChatSessionEvent` | SSE event name |
|---|---|
| `TextDeltaEvent` | `delta` |
| `AudioDeltaEvent` | `audio_delta` |
| `ToolCallEvent` | `tool_call` |
| `ToolResultEvent` | `tool_result` |
| `InterruptedEvent` | `interrupted` |
| `ResumedEvent` | `resumed` |
| `ErrorEvent` | `error` |
| `CompletedEvent` | *(silently consumed)* |

### State-machine SSE loop

Await a state machine's `CurrentWork` through phase transitions, surviving interrupt-induced cancellation races:

```csharp
using Ananke.AspNetCore.Sse;

await machine.FireAsync(MyAction.Start);
var reachedDone = await machine.RunSseLoopAsync(MyPhase.Done);

if (reachedDone)
    await context.Response.WriteSseAsync("done", new { });
```

### Provider configuration

Register LLM providers at startup and read settings from `IConfiguration`:

```csharp
using Ananke.AspNetCore.Configuration;

// Register providers (typically in Program.cs)
AgentModelFactory.RegisterProvider("OpenAI",
    defaultModel: "gpt-4.1-mini",
    agentFactory: (key, model) => OpenAIChatAgentModel.Create(key, model),
    embeddingFactory: (key, model) => OpenAIEmbeddingModel.Create(key, model));

AgentModelFactory.RegisterProvider("Google",
    defaultModel: "gemini-2.5-flash",
    agentFactory: (key, model) => GeminiAgentModel.Create(key, model));

// Read from configuration (secrets.json / appsettings.json)
// { "Provider": "OpenAI", "OpenAI": { "ApiKey": "sk-...", "Model": "gpt-4.1-mini" } }
var profile = AgentModelFactory.FromConfiguration(builder.Configuration);
var agentModel = profile.CreateAgentModel();
var embedder = profile.CreateEmbeddingModel(); // null if not configured
```

### Session store

Thread-safe in-memory session management:

```csharp
using Ananke.AspNetCore.Sessions;

var sessions = new InMemorySessionStore<MySession>();

// Atomic get-or-create
var session = sessions.GetOrCreate(sessionId, () => new MySession());

// Lookup
var existing = sessions.Get(sessionId);          // null if missing
sessions.TryGet(sessionId, out var s);           // bool pattern

// Cleanup
sessions.Remove(sessionId);
var removed = sessions.RemoveAndGet(sessionId);  // remove + return
```

## Features

- **`EnableSse()`** — one-liner SSE response header setup
- **`WriteSseAsync(name, data)`** — JSON-serialized SSE event writing with flush
- **`ChatSessionEvent` → SSE bridge** — maps all workflow event types to named SSE events
- **`RunSseLoopAsync(terminalState)`** — reusable state-machine await loop with interrupt-race handling
- **`AgentModelFactory`** — registry-based provider configuration from `IConfiguration`
- **`ProviderProfile`** — immutable settings record with `CreateAgentModel()` / `CreateEmbeddingModel()`
- **`InMemorySessionStore<T>`** — `ConcurrentDictionary`-backed thread-safe session store

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Orchestration` | Workflow builder, agents, `ChatSessionEvent`, `StreamingChatWorkflow` |
| `Ananke.StateMachine` | State machine engine with `OnEnter` work and interrupt support |
| `Ananke.Orchestration.OpenAI` | OpenAI `IStreamingAgentModel` + `IEmbeddingModel` provider |
| `Ananke.Orchestration.Google` | Google Gemini `IStreamingAgentModel` + `IEmbeddingModel` provider |
| `Ananke.Orchestration.Anthropic` | Anthropic / Claude `IStreamingAgentModel` provider |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
