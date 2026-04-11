<!-- topic: faq-providers, tags: faq, llm, providers, openai, anthropic, google, ollama, azure -->
# FAQ — LLM Providers

← [Back to all FAQs](../faq.md)

---

### Which LLM providers does Ananke support?

| Provider | Package | Example models |
|---|---|---|
| OpenAI | `Ananke.Orchestration.OpenAI` | GPT-4.1, GPT-4o, o1, o3, text-embedding-3-small/large |
| Anthropic | `Ananke.Orchestration.Anthropic` | Claude Sonnet, Claude Haiku, Claude Opus |
| Google Gemini | `Ananke.Orchestration.Google` | Gemini 2.5 Pro, Gemini Flash |
| Any OpenAI-compatible | `Ananke.Orchestration.OpenAI` | Ollama, LM Studio, vLLM, Azure OpenAI, Groq, Deepseek, Together AI |

### Does Ananke support Ollama (local models)?

Yes. Use `OpenAIChatAgentModel` with a custom `baseUri` pointing to your Ollama server.
See [Advanced Agent Features](../guides/11-advanced-agents.md) for the exact configuration.

### Does Ananke support Azure OpenAI?

Yes. Azure OpenAI exposes an OpenAI-compatible API. Configure `OpenAIChatAgentModel` with
your Azure endpoint URL and API key. See [Advanced Agent Features](../guides/11-advanced-agents.md).

### Can I use multiple LLM providers in the same workflow?

Yes. Each `AgentJob` takes its own `IStreamingAgentModel`, so different jobs in the same
workflow can use different providers or models. `CapabilityModelRouter` lets you route
requests to models based on declared capabilities (e.g., vision support, context window size,
reasoning tier).

### Can I swap providers without changing my workflow?

Yes. Workflows, state types, tool definitions, and routing rules are all expressed in terms of
Ananke's own interfaces — not any provider's SDK. Switching from one provider to another is a
one-line configuration change.

### What is `IStreamingAgentModel`?

`IStreamingAgentModel` is Ananke's provider-agnostic interface for LLM interaction. All
provider implementations (`OpenAIChatAgentModel`, `AnthropicAgentModel`, `GoogleAgentModel`,
`A2AAgentModel`) implement this interface. You can also implement it yourself to wrap any
model or API.

---

← [Back to all FAQs](../faq.md) · [Feature Index](../reference/features.md) · [Getting Started](../guides/01-getting-started.md)
