# Ananke.Orchestration.Anthropic — Architecture

> Anthropic provider — implements `IStreamingAgentModel` via the Anthropic .NET SDK.

## Role

Bridges the Anthropic Messages API to Ananke's `IAgentModel`/`IStreamingAgentModel`
interface. Supports tool calling and streaming.

## Dependencies

- `Ananke.Orchestration` (project)
- `Anthropic` (NuGet)

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `AnthropicAgentModel` | Sealed class | `IStreamingAgentModel` implementation. Wraps the Anthropic client. |
