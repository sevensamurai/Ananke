using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Microsoft.Extensions.Configuration;

namespace Ananke.AspNetCore.Configuration;

/// <summary>
/// Resolved provider settings from <see cref="IConfiguration"/> paired with the
/// <see cref="AgentModelFactory"/> that produced them — so <see cref="CreateAgentModel"/>
/// and <see cref="CreateEmbeddingModel"/> can delegate back without static state.
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
/// </summary>
public sealed record ProviderProfile
{
    private readonly AgentModelFactory _factory;

    internal ProviderProfile(AgentModelFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

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
        _factory.CreateAgentModel(Provider, ApiKey, Model);

    /// <summary>
    /// Creates an <see cref="IEmbeddingModel"/> using the registered factory for this provider.
    /// Returns <see langword="null"/> if <see cref="EmbeddingModel"/> is not configured or
    /// no embedding factory is registered for this provider.
    /// </summary>
    public IEmbeddingModel? CreateEmbeddingModel() =>
        string.IsNullOrWhiteSpace(EmbeddingModel)
            ? null
            : _factory.CreateEmbeddingModel(Provider, ApiKey, EmbeddingModel);
}

/// <summary>
/// Registry of provider factory functions and configuration reader.
/// Create an instance at startup (or register as a singleton in DI), register
/// providers, then call <see cref="FromConfiguration"/> to read settings and
/// create model instances.
/// </summary>
/// <remarks>
/// <para>
/// This is an instance class so it can be scoped, replaced in tests, and
/// registered in DI without global mutable state. For DI registration:
/// <code>
/// services.AddSingleton(new AgentModelFactory()
///     .RegisterProvider("OpenAI", "gpt-4.1-mini",
///         (key, model) =&gt; OpenAIChatAgentModel.Create(key, model),
///         (key, model) =&gt; OpenAIEmbeddingModel.Create(key, model)));
/// </code>
/// </para>
/// </remarks>
public sealed class AgentModelFactory
{
    private readonly Dictionary<string, ProviderRegistration> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a provider with its factory functions and default model name.
    /// Returns <see langword="this"/> for fluent chaining.
    /// </summary>
    /// <param name="providerName">Provider name used in configuration (e.g. <c>"OpenAI"</c>).</param>
    /// <param name="defaultModel">Default model name when not specified in configuration.</param>
    /// <param name="agentFactory">Factory that creates an <see cref="IStreamingAgentModel"/> from (apiKey, model).</param>
    /// <param name="embeddingFactory">
    /// Optional factory that creates an <see cref="IEmbeddingModel"/> from (apiKey, model).
    /// <see langword="null"/> if the provider does not support embeddings.
    /// </param>
    public AgentModelFactory RegisterProvider(
        string providerName,
        string defaultModel,
        Func<string, string, IStreamingAgentModel> agentFactory,
        Func<string, string, IEmbeddingModel>? embeddingFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModel);
        ArgumentNullException.ThrowIfNull(agentFactory);

        _providers[providerName] = new ProviderRegistration(defaultModel, agentFactory, embeddingFactory);
        return this;
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
    public ProviderProfile FromConfiguration(IConfiguration config)
    {
        var provider = config["Provider"] ?? "OpenAI";
        var section = config.GetSection(provider);

        var apiKey = section["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"{provider}:ApiKey is not configured. " +
                $"Add it to secrets.json: {{ \"{provider}\": {{ \"ApiKey\": \"...\" }} }}");

        if (!_providers.TryGetValue(provider, out var registration))
            throw new InvalidOperationException(
                $"Provider '{provider}' is not registered. " +
                $"Call RegisterProvider(\"{provider}\", ...) before reading configuration. " +
                $"Registered providers: {string.Join(", ", _providers.Keys)}");

        return new ProviderProfile(this)
        {
            Provider = provider,
            ApiKey = apiKey,
            Model = section["Model"] ?? registration.DefaultModel,
            EmbeddingModel = section["EmbeddingModel"]
        };
    }

    internal IStreamingAgentModel CreateAgentModel(string provider, string apiKey, string model)
    {
        if (!_providers.TryGetValue(provider, out var registration))
            throw new InvalidOperationException($"Unknown provider: {provider}");

        return registration.AgentFactory(apiKey, model);
    }

    internal IEmbeddingModel? CreateEmbeddingModel(string provider, string apiKey, string model)
    {
        if (!_providers.TryGetValue(provider, out var registration))
            throw new InvalidOperationException($"Unknown provider: {provider}");

        return registration.EmbeddingFactory?.Invoke(apiKey, model);
    }

    private sealed record ProviderRegistration(
        string DefaultModel,
        Func<string, string, IStreamingAgentModel> AgentFactory,
        Func<string, string, IEmbeddingModel>? EmbeddingFactory);
}
