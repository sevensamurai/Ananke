<!-- topic: faq-providers, tags: faq, llm, providers, openai, anthropic, google, ollama, azure, bedrock, foundry -->
# FAQ — LLM Providers

← [Back to all FAQs](../faq.md)

---

### Which LLM providers does Ananke support?

| Provider | Package | Example models |
|---|---|---|
| OpenAI | `Ananke.Orchestration.OpenAI` | GPT-4.1, GPT-4o, o1, o3, text-embedding-3-small/large |
| Anthropic | `Ananke.Orchestration.Anthropic` | Claude Sonnet, Claude Haiku, Claude Opus |
| Google Gemini | `Ananke.Orchestration.Google` | Gemini 2.5 Pro, Gemini Flash |
| Amazon Bedrock | `Ananke.Orchestration.Bedrock` | Most Bedrock-hosted models — **not** Nova or Titan; see the [enterprise clouds guide](../guides/21-enterprise-clouds.md) |
| Any OpenAI-compatible | `Ananke.Orchestration.OpenAI` | Ollama, LM Studio, vLLM, Microsoft Foundry, Groq, Deepseek, Together AI |

### Does Ananke support Ollama (local models)?

Yes. Use `OpenAIChatAgentModel.Create` with a custom `endpoint` pointing to your Ollama server — and
no API key is needed when an endpoint is set. See
[17 — Local & self-hosted models](../guides/17-local-models.md) for the configuration and, more
importantly, for the four ways a small self-hosted model behaves differently from a hosted one.

### Can I run embeddings locally too?

Yes — `OpenAIEmbeddingModel` takes the same `endpoint` parameter, and this is often the stronger
privacy case, since a knowledge base is where the private documents are.

Two things to plan for. **Vector size**: local embedders commonly emit 768 or 1024 dimensions against
OpenAI's 1536, and a Qdrant collection is created with a fixed size, so it must match the model you
actually use. **Changing embedder invalidates the store**: vectors from different models are not
comparable, and nothing silently re-embeds — switching means re-ingesting. See
[06 — Memory](../guides/06-memory.md).

### Does Ananke support Microsoft Foundry (formerly Azure OpenAI)?

Yes, with no extra package — Foundry's `/openai/v1/` route is OpenAI-shaped and needs no
`api-version`. Point `OpenAIChatAgentModel` at your resource with either an API key or, for keyless
tenants, a Microsoft Entra ID bearer policy. Recipes for both are in
[21 — Enterprise clouds](../guides/21-enterprise-clouds.md).

### Does Ananke support Amazon Bedrock?

Yes, through `Ananke.Orchestration.Bedrock`, which supplies SigV4 signing, Bedrock API-key auth and
endpoint construction — the requests themselves are served by the OpenAI and Anthropic adapters
against Bedrock's compatible APIs.

**Amazon's own Nova and Titan families are not reachable**, nor are older Claude models: those speak
only Bedrock's native Converse and Invoke APIs, which Ananke does not implement. See
[21 — Enterprise clouds](../guides/21-enterprise-clouds.md).

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
provider implementations (`OpenAIChatAgentModel`, `AnthropicAgentModel`, `GeminiAgentModel`,
`A2AAgentModel`) implement this interface. You can also implement it yourself to wrap any
model or API.

---

← [Back to all FAQs](../faq.md) · [Feature Index](../reference/features.md) · [Getting Started](../guides/01-getting-started.md)
