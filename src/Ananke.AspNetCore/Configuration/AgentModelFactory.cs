using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Knowledge;
using Microsoft.Extensions.Configuration;

namespace Ananke.AspNetCore.Configuration;

/// <summary>
/// Reads LLM provider settings from <see cref="IConfiguration"/> and creates
/// <see cref="IStreamingAgentModel"/> and <see cref="IEmbeddingModel"/> instances
/// via registered factory functions.
/// <para>
/// Configuration layout (secrets.json / appsettings.json):
/// <code>
/// {
///   "Provider": "OpenAI",            // or "Google", "Anthropic", etc.
///   "OpenAI": {
///     "ApiKey": "sk-...",
///     "Model": "gpt-4.1-mini",       // optional — uses provider default
///     "EmbeddingModel": "text-embedding-3-small"  // optional
///   }
/// }
/// </code>
/// </para>
/// <para>
/// Register provider factories before calling <see cref="AgentModelFactory.FromConfiguration"/>:
/// <code>
/// AgentModelFactory.RegisterProvider("OpenAI",
///     defaultModel: "gpt-4.1-mini",
///     agentFactory: (key, model) => OpenAIChatAgentModel.Create(key, model),
///     embeddingFactory: (key, model) => OpenAIEmbeddingModel.Create(key, model));
/// </code>
/// </para>
/// </summary>
public sealed record ProviderProfile
{
    /// <summary>Provider name (e.g. <c>"OpenAI"</c>, <c>"Google"</c>).</summary>
    public required string Provider { get; init; }

    /// <summary>API key for the provider.</summary>
    public required string ApiKey { get; init; }

    /// <summary>Model name (e.g. <c>"gpt-4.1-mini"</c>, <c>"gemini-2.5-flash"</c>).</summary>
    public required string Model { get; init; }

    /// <summary>Optional embedding model name. <see langword="null"/> if not configured.</summary>
    public string? EmbeddingModel { get; init; }

    /// <summary>
    /// Creates an <see cref="IStreamingAgentModel"/> using the registered factory for this provider.
    /// </summary>
    public IStreamingAgentModel CreateAgentModel() =>
        AgentModelFactory.CreateAgentModel(Provider, ApiKey, Model);

    /// <summary>
    /// Creates an <see cref="IEmbeddingModel"/> using the registered factory for this provider.
    /// Returns <see langword="null"/> if <see cref="EmbeddingModel"/> is not configured or
    /// no embedding factory is registered for this provider.
    /// </summary>
    public IEmbeddingModel? CreateEmbeddingModel() =>
        string.IsNullOrWhiteSpace(EmbeddingModel)
            ? null
            : AgentModelFactory.CreateEmbeddingModel(Provider, ApiKey, EmbeddingModel);
}

/// <summary>
/// Registry of provider factory functions and configuration reader.
/// Register providers at startup, then call <see cref="FromConfiguration"/> to
/// read settings and create model instances.
/// </summary>
public static class AgentModelFactory
{
    private static readonly Dictionary<string, ProviderRegistration> Providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a provider with its factory functions and default model name.
    /// </summary>
    /// <param name="providerName">Provider name used in configuration (e.g. <c>"OpenAI"</c>).</param>
    /// <param name="defaultModel">Default model name when not specified in configuration.</param>
    /// <param name="agentFactory">Factory that creates an <see cref="IStreamingAgentModel"/> from (apiKey, model).</param>
    /// <param name="embeddingFactory">
    /// Optional factory that creates an <see cref="IEmbeddingModel"/> from (apiKey, model).
    /// <see langword="null"/> if the provider does not support embeddings.
    /// </param>
    public static void RegisterProvider(
        string providerName,
        string defaultModel,
        Func<string, string, IStreamingAgentModel> agentFactory,
        Func<string, string, IEmbeddingModel>? embeddingFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModel);
        ArgumentNullException.ThrowIfNull(agentFactory);

        Providers[providerName] = new ProviderRegistration(defaultModel, agentFactory, embeddingFactory);
    }

    /// <summary>
    /// Reads provider settings from <see cref="IConfiguration"/> and returns a <see cref="ProviderProfile"/>.
    /// <para>
    /// Reads <c>Provider</c> (default <c>"OpenAI"</c>), then <c>{Provider}:ApiKey</c>,
    /// <c>{Provider}:Model</c>, and <c>{Provider}:EmbeddingModel</c>.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the API key is missing or the provider is not registered.
    /// </exception>
    public static ProviderProfile FromConfiguration(IConfiguration config)
    {
        var provider = config["Provider"] ?? "OpenAI";
        var section = config.GetSection(provider);

        var apiKey = section["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"{provider}:ApiKey is not configured. " +
                $"Add it to secrets.json: {{ \"{provider}\": {{ \"ApiKey\": \"...\" }} }}");

        if (!Providers.TryGetValue(provider, out var registration))
            throw new InvalidOperationException(
                $"Provider '{provider}' is not registered. " +
                $"Call AgentModelFactory.RegisterProvider(\"{provider}\", ...) before reading configuration. " +
                $"Registered providers: {string.Join(", ", Providers.Keys)}");

        return new ProviderProfile
        {
            Provider = provider,
            ApiKey = apiKey,
            Model = section["Model"] ?? registration.DefaultModel,
            EmbeddingModel = section["EmbeddingModel"]
        };
    }

    internal static IStreamingAgentModel CreateAgentModel(string provider, string apiKey, string model)
    {
        if (!Providers.TryGetValue(provider, out var registration))
            throw new InvalidOperationException($"Unknown provider: {provider}");

        return registration.AgentFactory(apiKey, model);
    }

    internal static IEmbeddingModel? CreateEmbeddingModel(string provider, string apiKey, string model)
    {
        if (!Providers.TryGetValue(provider, out var registration))
            throw new InvalidOperationException($"Unknown provider: {provider}");

        return registration.EmbeddingFactory?.Invoke(apiKey, model);
    }

    private sealed record ProviderRegistration(
        string DefaultModel,
        Func<string, string, IStreamingAgentModel> AgentFactory,
        Func<string, string, IEmbeddingModel>? EmbeddingFactory);
}
