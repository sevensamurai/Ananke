# Ananke.Orchestration.Conformance.Tests

Contract test suite for `IStreamingAgentModel`, `IToolSchemaTranslator`, and
`IJsonSchemaTranslator`.

## Purpose

Adapter drift is invisible until a user hits it at runtime. This project
provides a shared set of scenarios that every provider implementation must
pass, so regressions are caught in CI rather than production.

## Structure

| File | What it tests |
|------|---------------|
| `StreamingAgentModelConformanceTests.cs` | Text, tools, structured output, streaming, multimodal, token usage, system-prompt + JSON schema fusion |
| `ToolSchemaTranslatorConformanceTests.cs` | `IToolSchemaTranslator` — basic translation, idempotency, local-tool rejection |
| `JsonSchemaTranslatorConformanceTests.cs` | `IJsonSchemaTranslator` — basic translation, idempotency, type preservation |
| `FakeConformanceModel.cs` | Reference `IStreamingAgentModel` that makes the suite self-validating in CI without credentials |

## Extending for a Provider

1. Create a class in the provider's test project that inherits
   `StreamingAgentModelConformanceTests`.
2. Override `CreateModel()` to return the real provider model wired to a
   sandbox/fake API key.
3. The full scenario set runs automatically.

```csharp
[TestFixture]
public sealed class OpenAIConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel() =>
        new OpenAIAgentModel(new OpenAIClient("sk-test"), "gpt-4o-mini");
}
```

Similarly subclass `ToolSchemaTranslatorConformanceTests` and
`JsonSchemaTranslatorConformanceTests` for each provider's translator.
