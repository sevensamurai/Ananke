# Ananke.OpenTelemetry — Architecture

> OpenTelemetry tracing — one-liner OTLP export for workflow and
> state machine spans.

## Role

Provides `IWorkflowTracer` implementation via OpenTelemetry `ActivitySource`,
plus a builder for configuring OTLP export to BetterStack, Jaeger, Grafana Tempo,
or any OTLP-compatible backend.

## Dependencies

- `Ananke.Abstractions` (project)
- `OpenTelemetry`
- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol`
- `Microsoft.Extensions.DependencyInjection`

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `OtelTracingBuilder` | Static class | Fluent builder: `OtelTracingBuilder.Build(o => o.ServiceName("x").UseOtlp(...))` |
| `OtelTracingOptions` | Class | Configuration: service name, OTLP endpoint, headers, BetterStack token |
| `ActivitySourceTracer` | Class | `IWorkflowTracer` backed by `System.Diagnostics.ActivitySource` |
| `TracingPipeline` | Class | `IJobMiddleware` that creates spans around job executions |
| `Sources` | Static class | Well-known `ActivitySource` names for Ananke tracing |

## Usage

```csharp
using var tracing = OtelTracingBuilder.Build(o =>
{
    o.ServiceName = "MyAgent";
    o.UseBetterStack("source-token");
});
```
