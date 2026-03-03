# Ananke.Orchestration.OpenAI

[![NuGet](https://img.shields.io/nuget/v/Ananke.Orchestration.OpenAI.svg)](https://www.nuget.org/packages/Ananke.Orchestration.OpenAI)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

OpenAI provider for [Ananke.Orchestration](https://www.nuget.org/packages/Ananke.Orchestration) — `ChatClient`-based `IStreamingAgentModel` implementation with tool calling, structured output, and token-level streaming.

## Install

```bash
dotnet add package Ananke.Orchestration.OpenAI
```

## Quick start

```csharp
using Ananke.Orchestration.OpenAI;
using OpenAI.Chat;

// From ChatClient
var client = new ChatClient("gpt-4o", apiKey);
IStreamingAgentModel model = new OpenAIChatAgentModel(client);

// Or use the convenience factory
IStreamingAgentModel model = OpenAIChatAgentModel.Create(apiKey, "gpt-4o");
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
- Structured output via `ChatResponseFormat`
- Configurable `store: false` for completions privacy
- Works with any OpenAI-compatible API endpoint

## Requirements

- `Ananke.Orchestration` (transitive)
- `OpenAI` SDK ≥ 2.8.0

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
