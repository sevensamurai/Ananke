# Ananke.Orchestration.Google — Architecture

> Google provider — implements `IStreamingAgentModel` via Google GenAI SDK.

## Role

Bridges Google's Gemini models to Ananke's `IAgentModel`/`IStreamingAgentModel`
interface. Supports tool calling and streaming.

## Dependencies

- `Ananke.Orchestration` (project)
- `Google.GenAI` (NuGet)

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `GoogleAgentModel` | Sealed class | `IStreamingAgentModel` implementation. Wraps the Google GenAI client. |
