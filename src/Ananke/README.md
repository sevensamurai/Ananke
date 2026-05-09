# Ananke

[![NuGet](https://img.shields.io/nuget/v/Ananke.svg?color=5B4FCF)](https://www.nuget.org/packages/Ananke)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)

**Meta-package** — install once, get the full Ananke framework: distributed state machine, workflow orchestration, and the bridge integration layer.

## Install

```bash
dotnet add package Ananke
```

Add an LLM provider if you need AI agents:

```bash
dotnet add package Ananke.Orchestration.OpenAI    # or Ananke.Orchestration.Anthropic
```

## What's included

| Dependency | What it provides |
|---|---|
| `Ananke.Abstractions` | Shared interfaces (`IDistributedLock`, `IChannelReader/Writer`, `IWorkflowTracer`) |
| `Ananke.StateMachine` | Distributed FSM with RedLock coordination, middleware, fault/reset |
| `Ananke.Orchestration` | Workflow builder, runner, agents, knowledge pipeline, checkpointing, tracing |

The bridge glue code (`StateMachineTriggerJob`, `WorkflowTriggerAction`, `WorkflowCompletionTrigger`) is included directly — it connects state machine transitions to workflow executions and vice versa.

## Quick start

```csharp
using Ananke.StateMachine.Extensions;
using Ananke.Orchestration.Extensions;

// Register everything via DI
services.AddStateMachine();
services.AddWorkflowOrchestration(o => o.UseCheckpointing());

// Optional infrastructure — call order doesn't matter
services.AddRedis(c => { c.Host = "localhost"; });
```

## Optional add-ons

| Package | What it adds |
|---|---|
| `Ananke.Orchestration.OpenAI` | OpenAI / GPT provider |
| `Ananke.Orchestration.Anthropic` | Anthropic / Claude provider |
| `Ananke.Orchestration.Google` | Google Gemini + Gemini Enterprise Agent Platform provider |
| `Ananke.MCP` | Expose tools and workflows as MCP server capabilities |
| `Ananke.Redis` | Redis-backed distributed lock and key-value store |
| `Ananke.MQTT` | MQTT-backed pub/sub channels |
| `Ananke.Documents` | PDF + Markdown document extractors for the knowledge pipeline |
| `Ananke.Qdrant` | Qdrant vector database for persistent long-term memory |
| `Ananke.OpenTelemetry` | OTLP tracing export |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
