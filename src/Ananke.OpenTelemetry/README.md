# Ananke.OpenTelemetry

[![NuGet](https://img.shields.io/nuget/v/Ananke.OpenTelemetry.svg)](https://www.nuget.org/packages/Ananke.OpenTelemetry)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

OpenTelemetry tracing infrastructure for Ananke — one-liner OTLP export to BetterStack, Jaeger, Grafana Tempo, or any OTLP-compatible backend.

## Install

```bash
dotnet add package Ananke.OpenTelemetry
```

## Quick start

```csharp
using Ananke.OpenTelemetry;

services.AddTracingPipeline(o =>
{
    o.ServiceName    = "my-service";
    o.ServiceVersion = "1.0.0";
    o.UseBetterStack(sourceToken);
});
```

### Custom OTLP endpoint (Jaeger, Grafana Tempo, etc.)

```csharp
services.AddTracingPipeline(o =>
{
    o.ServiceName    = "my-service";
    o.ServiceVersion = "1.0.0";
    o.UseOtlp(
        new Uri("http://localhost:4318/v1/traces"),
        headers: null);
});
```

### Add state machine tracing

```csharp
services.AddTracingPipeline(o =>
{
    o.ServiceName = "my-service";
    o.AddSource(Ananke.OpenTelemetry.Sources.StateMachine);
    o.UseBetterStack(token);
});
```

## What it registers

| Service | Implementation |
|---|---|
| `IWorkflowTracer` | `ActivitySourceTracer` — creates OpenTelemetry spans for workflow execution |
| OpenTelemetry pipeline | OTLP exporter with configurable endpoint, headers, and activity sources |

## Features

- **One-liner setup** — `AddTracingPipeline()` wires resource, sources, exporter, and `IWorkflowTracer`
- **BetterStack** — built-in `UseBetterStack(token)` convenience method
- **Any OTLP backend** — `UseOtlp(endpoint, headers)` for Jaeger, Grafana Tempo, etc.
- **Multiple activity sources** — orchestration tracing by default, add state machine tracing with `AddSource()`
- **Works with both** `Ananke.Orchestration` and `Ananke.StateMachine`

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
