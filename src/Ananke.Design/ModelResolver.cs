using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;

namespace Ananke.Design;

/// <summary>
/// Resolves <see cref="ModelDefinition"/> entries from a <see cref="WorkflowManifest"/>
/// into live <see cref="IAgentModel"/> instances using registered provider factories.
/// </summary>
/// <remarks>
/// <para>
/// The resolver decouples manifest parsing from provider SDK dependencies.
/// Each provider package ships a static <c>Create(apiKey, model)</c> factory;
/// the consumer registers these factories at startup.
/// </para>
/// <para>
/// For providers that support custom endpoints (e.g. Ollama, LM Studio, Azure OpenAI),
/// use the three-parameter <see cref="Register(string,string,Func{string,string,Uri?,IAgentModel})"/>
/// overload. The endpoint is resolved from the YAML <c>endpoint:</c> field or from
/// <c>{configSection}:Endpoint</c> in configuration.
/// </para>
/// <para>
/// Config lookup is a <c>Func&lt;string, string?&gt;</c> so the resolver works with
/// <c>IConfiguration</c>, environment variables, or any other config source.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var models = new ModelResolver()
///     .Register("openai", "OpenAI", OpenAIChatAgentModel.Create)
///     .Register("anthropic", "Anthropic", AnthropicAgentModel.Create)
///     .Resolve(manifest, key => config[key]);
/// </code>
/// </example>
public sealed class ModelResolver
{
    private readonly Dictionary<string, ProviderRegistration> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a provider factory that does not support custom endpoints.
    /// </summary>
    /// <param name="provider">
    /// Provider name as used in the YAML <c>models:</c> section (e.g. <c>"openai"</c>, <c>"anthropic"</c>).
    /// Matched case-insensitively.
    /// </param>
    /// <param name="configSection">
    /// Configuration section prefix for this provider (e.g. <c>"OpenAI"</c>).
    /// Used to look up <c>{configSection}:ApiKey</c> and <c>{configSection}:Model</c>.
    /// </param>
    /// <param name="factory">
    /// Factory function: <c>(apiKey, modelName) → IAgentModel</c>.
    /// Typically a static <c>Create</c> method from the provider package.
    /// </param>
    public ModelResolver Register(string provider, string configSection, Func<string, string, IAgentModel> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);
        ArgumentNullException.ThrowIfNull(factory);

        _providers[provider] = new ProviderRegistration(configSection, (apiKey, model, _) => factory(apiKey, model));
        return this;
    }

    /// <summary>
    /// Registers a provider factory that supports custom endpoints (e.g. Ollama, LM Studio, Azure OpenAI).
    /// </summary>
    /// <param name="provider">
    /// Provider name as used in the YAML <c>models:</c> section. Matched case-insensitively.
    /// </param>
    /// <param name="configSection">
    /// Configuration section prefix. Used to look up <c>{configSection}:ApiKey</c>,
    /// <c>{configSection}:Model</c>, and <c>{configSection}:Endpoint</c>.
    /// </param>
    /// <param name="factory">
    /// Factory function: <c>(apiKey, modelName, endpoint) → IAgentModel</c>.
    /// <c>endpoint</c> is <see langword="null"/> when no custom endpoint is configured.
    /// </param>
    public ModelResolver Register(string provider, string configSection, Func<string, string, Uri?, IAgentModel> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);
        ArgumentNullException.ThrowIfNull(factory);

        _providers[provider] = new ProviderRegistration(configSection, factory);
        return this;
    }

    /// <summary>
    /// Resolves all model aliases from the manifest into <see cref="IAgentModel"/> instances.
    /// </summary>
    /// <param name="manifest">The parsed workflow manifest containing model definitions.</param>
    /// <param name="configLookup">
    /// Configuration lookup function. Receives keys like <c>"OpenAI:ApiKey"</c>, <c>"OpenAI:Model"</c>,
    /// <c>"OpenAI:Endpoint"</c>. Typically <c>key =&gt; config[key]</c>.
    /// </param>
    /// <returns>Dictionary of model alias → resolved <see cref="IAgentModel"/>.</returns>
    public Dictionary<string, IAgentModel> Resolve(WorkflowManifest manifest, Func<string, string?> configLookup)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(configLookup);

        var resolved = new Dictionary<string, IAgentModel>(manifest.Models.Count);

        foreach (var (alias, def) in manifest.Models)
        {
            if (!_providers.TryGetValue(def.Provider, out var registration))
                throw new InvalidOperationException(
                    $"No factory registered for provider '{def.Provider}' (model alias '{alias}'). " +
                    $"Call Register(\"{def.Provider}\", ...) before resolving.");

            var section = registration.ConfigSection;

            var apiKey = configLookup($"{section}:ApiKey")
                ?? throw new InvalidOperationException(
                    $"{section}:ApiKey not found in configuration (required by model alias '{alias}').");

            var modelName = configLookup($"{section}:Model") ?? def.Model;

            // Endpoint priority: YAML definition > config section > null (default)
            var endpointStr = def.Endpoint ?? configLookup($"{section}:Endpoint");
            var endpoint = endpointStr is not null ? new Uri(endpointStr) : null;

            resolved[alias] = registration.Factory(apiKey, modelName, endpoint);
        }

        return resolved;
    }

    private sealed record ProviderRegistration(string ConfigSection, Func<string, string, Uri?, IAgentModel> Factory);
}
