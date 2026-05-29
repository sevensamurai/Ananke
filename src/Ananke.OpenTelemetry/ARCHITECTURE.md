# Ananke.OpenTelemetry — Architecture

> OpenTelemetry tracing plus listener-backed budget metering for workflow
> and state machine applications.

## Role

Provides `IWorkflowTracer` implementation via OpenTelemetry `ActivitySource`,
plus helpers for configuring OTLP export and registering a listener-backed
`IBudgetMeter` that consumes federation token/cost counters.

## Dependencies

- `Ananke.Abstractions` (project)
- `Ananke.Organics` (project)
- `OpenTelemetry`
- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol`
- `Microsoft.Extensions.DependencyInjection`

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `OtelTracingBuilder` | Static class | Fluent builder: `OtelTracingBuilder.Build(o => o.ServiceName("x").UseOtlp(...))` |
| `OtelTracingOptions` | Class | Configuration: service name, OTLP endpoint, headers, BetterStack token |
| `BudgetMeterOptions` | Class | Rolling-window and cap configuration for the OpenTelemetry budget listener |
| `ActivitySourceTracer` | Class | `IWorkflowTracer` backed by `System.Diagnostics.ActivitySource` |
| `OpenTelemetryBudgetMeter` | Class | `IBudgetMeter` backed by OpenTelemetry federation counters |
| `TracingPipeline` | Class | `IJobMiddleware` that creates spans around job executions |
| `Sources` | Static class | Well-known `ActivitySource` and `Meter` names for Ananke tracing and metrics |
| `TracingExtensions` | Static class | DI extensions for tracing and budget-meter registration |

## Usage

```csharp
using var tracing = OtelTracingBuilder.Build(o =>
{
    o.ServiceName = "MyAgent";
    o.UseBetterStack("source-token");
});

services.AddBudgetMeter(o =>
{
    o.DefaultTokenCap = 100_000;
});
```
