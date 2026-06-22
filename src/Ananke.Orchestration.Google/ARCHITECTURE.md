# Ananke.Orchestration.Google — Architecture

> Google provider — implements `IStreamingAgentModel` via Google GenAI SDK.

## Role

Bridges Google's Gemini models to Ananke's `IAgentModel`/`IStreamingAgentModel`
interface. Supports tool calling and streaming.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `GeminiAgentModel` — the sole entry point: `IStreamingAgentModel` implementation wrapping
   the Google GenAI client; `.Create(apiKey, model)` for the public Gemini API,
   `.CreateVertexAI(project, location, model)` for Vertex AI — `src/Ananke.Orchestration.Google/GeminiAgentModel.cs`

---

## Dependencies

- `Ananke.Orchestration` (project)
- `Google.GenAI` (NuGet)

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `GeminiAgentModel` | Sealed class | `IStreamingAgentModel` implementation. Wraps the Google GenAI client. `.Create(apiKey, model)` for the public Gemini API, `.CreateVertexAI(project, location, model)` for Vertex AI. | `src/Ananke.Orchestration.Google/GeminiAgentModel.cs` |
