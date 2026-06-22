# Ananke.Orchestration.Anthropic — Architecture

> Anthropic provider — implements `IStreamingAgentModel` via the Anthropic .NET SDK.

## Role

Bridges the Anthropic Messages API to Ananke's `IAgentModel`/`IStreamingAgentModel`
interface. Supports tool calling and streaming.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `AnthropicAgentModel` — the sole entry point: `IStreamingAgentModel` implementation
   wrapping the Anthropic client — `src/Ananke.Orchestration.Anthropic/AnthropicAgentModel.cs`

---

## Dependencies

- `Ananke.Orchestration` (project)
- `Anthropic` (NuGet)

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `AnthropicAgentModel` | Sealed class | `IStreamingAgentModel` implementation. Wraps the Anthropic client. |
