# Ananke.Orchestration.OpenAI — Architecture

> OpenAI provider — implements `IStreamingAgentModel` via the official OpenAI .NET SDK.

## Role

Bridges the OpenAI ChatClient (and any OpenAI-compatible API) to Ananke's
`IAgentModel`/`IStreamingAgentModel` interface. Supports tool calling,
streaming, structured output (JSON schema), and custom endpoints.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `OpenAIChatAgentModel` — the `IStreamingAgentModel` implementation that wraps
   `ChatClient`; the package's sole entry point — `src/Ananke.Orchestration.OpenAI/OpenAIChatAgentModel.cs`

---

## Dependencies

- `Ananke.Orchestration` (project)
- `OpenAI` (NuGet — official SDK)

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `OpenAIChatAgentModel` | Sealed class | `IStreamingAgentModel` implementation. Wraps `ChatClient`. |

## Factory

```csharp
// Default OpenAI endpoint
var model = OpenAIChatAgentModel.Create("sk-...", "gpt-4.1-mini");

// Custom endpoint (Ollama, LM Studio, Azure OpenAI, vLLM)
var model = OpenAIChatAgentModel.Create("ollama", "llama3.2",
    endpoint: new Uri("http://localhost:11434/v1"));
```

## Message Mapping

| Ananke | OpenAI |
|--------|--------|
| `AgentMessage` (User/System/Assistant/Tool) | `ChatMessage` subtypes |
| `ContentPart` (Text/Image/Audio) | `ChatMessageContentPart` |
| `AgentToolCall` | `ChatToolCall` |
| `AgentTool` | `ChatTool.CreateFunctionTool()` |
| `AgentResponseFormat` | `ChatResponseFormat.CreateJsonSchemaFormat()` |
