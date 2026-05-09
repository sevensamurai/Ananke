# Ananke.Orchestration.Google

[![NuGet](https://img.shields.io/nuget/v/Ananke.Orchestration.Google.svg)](https://www.nuget.org/packages/Ananke.Orchestration.Google)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Google Gemini provider for [Ananke.Orchestration](https://www.nuget.org/packages/Ananke.Orchestration) — `IStreamingAgentModel` implementation with tool calling, structured output, and token-level streaming via the official [Google GenAI SDK](https://googleapis.github.io/dotnet-genai/). Supports both the Gemini Developer API and Gemini Enterprise Agent Platform (via ADC).

## Install

```bash
dotnet add package Ananke.Orchestration.Google
```

## Quick start

```csharp
using Ananke.Orchestration.Google;

// Gemini Developer API (API key)
IStreamingAgentModel model = GeminiAgentModel.Create(apiKey, "gemini-2.5-flash");

// Gemini Enterprise Agent Platform (project + location, uses Application Default Credentials)
IStreamingAgentModel model = GeminiAgentModel.CreateVertexAI(project, location, "gemini-2.5-flash");

// Or from an existing Google.GenAI.Client
var client = new Google.GenAI.Client(apiKey: apiKey);
IStreamingAgentModel model = new GeminiAgentModel(client, "gemini-2.5-flash");
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

### YAML manifest (Ananke.Design)

```yaml
models:
  gemini:
    provider: gemini
    model: gemini-2.5-flash
```

```csharp
var models = new ModelResolver()
    .Register("gemini", "Gemini", GeminiAgentModel.Create)
    .Resolve(manifest, key => config[key]);
```

## Features

- Full `IStreamingAgentModel` implementation (streaming + non-streaming)
- Tool calling with automatic function dispatch
- Structured JSON output via `ResponseSchema`
- Automatic JSON Schema to Google `Schema` conversion
- Gemini Developer API (API key) and Gemini Enterprise Agent Platform (ADC) support

## Requirements

- `Ananke.Orchestration` (transitive)
- `Google.GenAI` SDK ≥ 1.1.0

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
