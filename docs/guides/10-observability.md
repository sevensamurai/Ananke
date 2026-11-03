<!-- topic: observability, tags: opentelemetry, tracing, otlp, metrics, spans -->
# 10 — Observability

Add distributed tracing to workflows, agents, and state machines with
OpenTelemetry and OTLP export.

---

## Setup

```bash
dotnet add package Ananke.OpenTelemetry
```

One call wires up the full tracing pipeline:

```csharp
using Ananke.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

var services = new ServiceCollection();

services.AddTracingPipeline(o =>
{
    o.ServiceName    = "my-service";
    o.ServiceVersion = "1.0.0";
    o.UseOtlp(endpoint, $"Authorization=Bearer {token}");
});

using var sp = services.BuildServiceProvider();
```

---

## Compatible Backends

The OTLP exporter works with any OpenTelemetry-compatible backend:

| Backend | Type |
|---|---|
| **BetterStack** | Cloud (managed) |
| **Jaeger** | Self-hosted |
| **Grafana Tempo** | Self-hosted / Grafana Cloud |
| **Honeycomb** | Cloud (managed) |
| **Datadog** | Cloud (managed) |
| **Azure Monitor** | Cloud (managed) |

---

## Workflow Tracing

Add tracing to any workflow with `UseTracing`:

```csharp
using Ananke.Abstractions.Tracing;

var tracer = sp.GetRequiredService<IWorkflowTracer>();

var execution = await workflow
    .UseTracing(tracer)
    .RunAsync(initialState);
```

This automatically creates spans for:
- Workflow start/end
- Each job start/end with duration
- State transitions between jobs

### Streaming Chat with Tracing

```csharp
var execution = await StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You are a helpful assistant.")
    .WithTools(tools)
    .OnTextDelta(async delta => Console.Write(delta))
    .Build()
    .UseTracing(tracer)
    .RunAsync(new StreamingChatState { Messages = messages });
```

---

## State Machine Tracing

`AbstractStateMachine` has a built-in `ActivitySource` that emits a `"transition"` span for
every transition attempt:

- Tags: `Ananke.context_id`, `Ananke.transition`, `Ananke.from_state`, `Ananke.to_state`, `Ananke.success`
- Failed transitions set the span status to `ActivityStatusCode.Error` with `result.ErrorMessage`
  as the description; an unhandled exception additionally adds an `"exception"` event with
  `exception.type` / `exception.message` tags.

No additional configuration is needed — the spans are emitted automatically
when an `ActivityListener` is registered (which `AddTracingPipeline` does).

---

## Tool Span Attributes

Each tool call within an agent job is one span, named `execute_tool {tool}` after the
OpenTelemetry GenAI conventions. `ActivitySourceTracer` sends numbers and flags as typed values.

| Attribute | Description |
|---|---|
| `gen_ai.operation.name` | Always `execute_tool` |
| `gen_ai.tool.name` | The tool the model asked for |
| `gen_ai.tool.call.id` | The id the model gave the call — the same as `AgentToolCalled.CallId` |
| `error.type` | `_OTHER` when `ToolResult.IsError`; absent otherwise |
| `tool.execution_mode` | How a registered tool is reached: `local`, `callback`, `mcp`, `openapi` or `platformnative` |
| `tool.target` | A remote tool's endpoint host — never its path or query |
| `tool_round` | The tool round the call was made in, counted from 1 |
| `output_length` | Character count of the tool result |
| `tool.error` | `true` when `ToolResult.IsError` |

---

## Tool-Call Content and Personal Data

Every tool call an agent job makes reaches the workflow's event stream as `AgentToolCalled`: its id,
round, duration, result length and whether it failed. **What the call was asked and what came back are off by
default.** A workflow turns them on:

```csharp
var workflow = BuildWorkflow()
    .RecordContent(true);   // or OrchestrationOptions.RecordContent(true) for the registered runner
```

With content on, mark the properties that hold personal data with `[Redact]`, on the types the
workflow's tools take and return. Ananke replaces a marked value with `[redacted]` wherever it
records the value; the tool is still handed it, and the model is still sent it.

```csharp
using Ananke.Abstractions;

public sealed record Guest
{
    [Redact] public string Phone { get; init; } = "";
    public string Name { get; init; } = "";
}

tools.AddTool("book", "Books a room.", tool => tool
    .ParamList<Guest>("guests", "Who stays.")              // recorded with each Phone masked
    .OnExecute(args => ToolResult.Json(Booking(args))));   // a marked property of the result too
```

A positional record marks a property with `[property: Redact]`. A typed result is written again for
the record exactly as the model was sent it, with marked properties masked. Arguments are the
model's own text, read by the parameter's type; arguments that cannot be read as that type — that do
not parse, or hold a value where the type has an object or a list — are recorded as `[redacted]`
whole.

**The workflow's state.** Events such as `StateUpdated<TState>` carry the state itself. A sink that
writes them with `AnankeJson.Record` writes each marked property as `[redacted]`; the options keep
the serializer's default names, and `JsonLinesEventSink` uses them. They are for writing only: a
masked value cannot be read back, so never use them for a checkpoint.

```csharp
public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
{
    _writer.WriteLine(JsonSerializer.Serialize<object>(evt, AnankeJson.Record));
    return ValueTask.CompletedTask;
}
```

**What is not masked.** Text has no property to mark, so it is recorded as written:

- a result a tool returns as a string (`ToolResult.Ok`), an MCP server's result, an argument declared
  with `Param` or `Param<T>`, and an error a tool returns;
- an exception's message and stack trace on a span, and `WorkflowFaulted.Exception`, the exception
  itself;
- the plan's text on its events: goals, a step's summary, a dispute's reason, a question and its
  answer;
- anything a model or a tool wrote that quotes a marked value.

Checkpoints and conversation memory hold every value whole, because a resumed run needs them. Keep
content off, or mask free text in your own sink, where it may hold personal data.

---

## Retry Event Reporting

When using `ResilientAgentModel` (see [Guide 11](11-advanced-agents.md)), each
retry attempt records an event on the active span:

```
Event: llm.rate_limit_retry
  retry.attempt:    2
  retry.delay_ms:   2150
  exception.type:   System.ClientModel.ClientResultException
  exception.message: HTTP 429 (Too Many Requests)
```

The cumulative retry count is set as a span tag (`llm.retries`), making
rate-limit pressure visible in your tracing dashboard.

---

## Flushing Before Exit

Always flush the tracer provider before your application exits to ensure
all pending spans are exported:

```csharp
using OpenTelemetry.Trace;

var tracerProvider = sp.GetRequiredService<TracerProvider>();

// ... run your application ...

// Flush remaining spans before exit
tracerProvider.ForceFlush(5_000);
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [11 — Advanced Agents](11-advanced-agents.md) | Resilient retries with OTel integration, response caching |
| [14 — Testing](14-testing.md) | Test without infrastructure using in-memory implementations |

---

← [Back to Learning Path](../learning-path.md)
