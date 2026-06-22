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

Tool executions within agent workflows emit span attributes:

| Attribute | Description |
|---|---|
| `output_length` | Character count of the tool result |
| `tool.error` | Set to `true` when `ToolResult.IsError` |

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
