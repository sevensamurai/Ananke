using Ananke.AspNetCore.Configuration;
using Ananke.Orchestration.Google;
using Ananke.Orchestration.OpenAI;

/// <summary>
/// Registers supported LLM providers at startup and delegates configuration reading
/// and model creation to <see cref="AgentModelFactory"/>.
/// </summary>
internal static class ProviderRegistration
{
    internal static AgentModelFactory CreateFactory()
    {
        return new AgentModelFactory()
            .RegisterProvider("OpenAI",
                defaultModel: "gpt-4.1-mini",
                agentFactory: (key, model) => OpenAIChatAgentModel.Create(key, model),
                embeddingFactory: (key, model) => OpenAIEmbeddingModel.Create(key, model))
            .RegisterProvider("Google",
                defaultModel: "gemini-2.5-flash",
                agentFactory: (key, model) => GeminiAgentModel.Create(key, model),
                embeddingFactory: (key, model) => GeminiEmbeddingModel.Create(key, model));
    }
}
