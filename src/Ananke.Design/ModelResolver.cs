using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;

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
/// For providers that support custom endpoints (e.g. Ollama, LM Studio, vLLM, Microsoft Foundry),
/// use the three-parameter <see cref="Register(string,string,Func{string,string,Uri?,IAgentModel})"/>
/// overload. The endpoint is resolved from the YAML <c>endpoint:</c> field or from
/// <c>{configSection}:Endpoint</c> in configuration.
/// </para>
/// <para>
/// <b>An endpoint makes the API key optional.</b> When a model declares an endpoint and no
/// <c>{configSection}:ApiKey</c> is configured, <see cref="PlaceholderApiKey"/> is supplied instead
/// of throwing, because a self-hosted server generally has no key to give. A configured key is
/// always preferred — hosted OpenAI-compatible providers require a real one. With no endpoint and no
/// key, resolution still fails: that combination can only mean a hosted provider.
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
    /// <summary>
    /// Sent as the API key when a model declares a custom endpoint and configuration supplies no
    /// key of its own — the self-hosted case, where the server ignores the credential but the
    /// client SDK still requires a non-empty one.
    /// </summary>
    /// <remarks>
    /// Deliberately readable rather than a plausible-looking secret: it shows up verbatim in an
    /// <c>Authorization</c> header, and a reader of a request log should be able to tell at a glance
    /// that no credential was configured — not wonder which one leaked.
    /// </remarks>
    public const string PlaceholderApiKey = "no-key-required";

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
            // Provider and Model carry defaults, so an alias that is only a reference would
            // otherwise resolve to a live openai/gpt-5.4-mini client — the silent substitution
            // the parser and the validator already refuse. Refused here too, or the
            // inconsistency simply moves to the third consumer.
            if (def.Ref is { } reference)
                throw new InvalidOperationException(
                    $"Model alias '{alias}' is an unresolved reference to '{reference}' and cannot "
                    + "be turned into a model. Supply a model catalogue that declares it, or "
                    + "declare the alias inline with a provider and model.");

            if (!_providers.TryGetValue(def.Provider, out var registration))
                throw new InvalidOperationException(
                    $"No factory registered for provider '{def.Provider}' (model alias '{alias}'). " +
                    $"Call Register(\"{def.Provider}\", ...) before resolving.");

            var section = registration.ConfigSection;

            var modelName = configLookup($"{section}:Model") ?? def.Model;

            // Endpoint priority: YAML definition > config section > null (default)
            var endpointStr = def.Endpoint ?? configLookup($"{section}:Endpoint");
            var endpoint = endpointStr is not null ? new Uri(endpointStr) : null;

            // Resolved after the endpoint, deliberately: a self-hosted OpenAI-compatible server
            // usually has no key to supply, and demanding one made the local-first path require
            // inventing a secret before it would run at all. A configured key always wins — hosted
            // compatible endpoints do need a real one — but its absence is only fatal when there is
            // no endpoint either, which means a hosted provider that certainly does.
            var apiKey = configLookup($"{section}:ApiKey")
                ?? (endpoint is not null
                    ? PlaceholderApiKey
                    : throw new InvalidOperationException(
                        $"{section}:ApiKey not found in configuration (required by model alias '{alias}')."));

            resolved[alias] = registration.Factory(apiKey, modelName, endpoint);
        }

        return resolved;
    }

    private sealed record ProviderRegistration(string ConfigSection, Func<string, string, Uri?, IAgentModel> Factory);
}
