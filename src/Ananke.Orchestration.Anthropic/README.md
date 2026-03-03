# Ananke.Orchestration.Anthropic

[![NuGet](https://img.shields.io/nuget/v/Ananke.Orchestration.Anthropic.svg)](https://www.nuget.org/packages/Ananke.Orchestration.Anthropic)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Anthropic / Claude provider for [Ananke.Orchestration](https://www.nuget.org/packages/Ananke.Orchestration) — `IStreamingAgentModel` implementation with tool calling and token-level streaming.

## Install

```bash
dotnet add package Ananke.Orchestration.Anthropic
```

## Quick start

```csharp
using Ananke.Orchestration.Anthropic;
using Anthropic;

// From AnthropicClient
var client = new AnthropicClient();
IStreamingAgentModel model = new AnthropicAgentModel(client, "claude-sonnet-4-20250514");

// Or use the convenience factory
IStreamingAgentModel model = AnthropicAgentModel.Create(apiKey, "claude-sonnet-4-20250514");
```

### Use in a workflow agent job

```csharp
var agentJob = AgentJobFactory
    .Create<MyState, MyResponse>("analyze", model)
    .WithSystemPrompt("You are a research analyst.")
    .WithTools(searchTools)
    .WithPrompt(state => $"Analyze: {state.Query}")
    .MapResult((state, response) => state with { Analysis = response.Text })
    .Build();
```

## Features

- Full `IStreamingAgentModel` implementation (streaming + non-streaming)
- Tool calling with automatic function dispatch
- Configurable max tokens (default 4096)
- Works with Claude Opus, Sonnet, and Haiku models

## Requirements

- `Ananke.Orchestration` (transitive)
- `Anthropic` SDK ≥ 12.8.0

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
