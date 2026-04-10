using Ananke.Abstractions.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ananke.Orchestration.Knowledge.Linking;

/// <summary>
/// DI registration extensions for optional cross-document linking in the knowledge pipeline.
/// When enabled, newly ingested documents can be automatically linked to existing related
/// documents, and search results are expanded through the link graph.
/// </summary>
/// <remarks>
/// <para>
/// This implements ADR-012's "Option C: Plugin registration via DI". The linking layer
/// is entirely opt-in — the base <see cref="IKnowledgeStore"/> contract is unchanged.
/// </para>
/// <para>
/// The extension registers:
/// <list type="bullet">
///   <item><see cref="IDocumentLinkGraph"/> — in-memory by default, replaceable with a persistent store</item>
///   <item><see cref="LinkedKnowledgeStore"/> — decorator that expands search via graph traversal</item>
///   <item><see cref="DocumentLinkExtractor"/> — optional LLM-based post-ingestion linker</item>
/// </list>
/// </para>
/// <para>
/// Composes with <see cref="Catalog.CatalogAwareKnowledgeStore"/>. Call order does not matter —
/// decorators are stacked in registration order.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddSingleton&lt;IKnowledgeStore&gt;(sp =>
///     new InMemoryKnowledgeStore(sp.GetRequiredService&lt;IEmbeddingModel&gt;()));
///
/// services.AddKnowledgeLinking(options =>
/// {
///     options.AutoLinkOnIngest = true;
///     options.SimilarityThreshold = 0.75f;
///     options.SearchOptions = new LinkedSearchOptions
///     {
///         ExpansionSeeds = 5,
///         MaxHops = 2,
///         GraphScoreDiscount = 0.7f
///     };
/// });
/// </code>
/// </example>
public static class KnowledgeLinkingExtensions
{
    /// <summary>
    /// Adds optional cross-document linking to the knowledge pipeline.
    /// Registers an <see cref="IDocumentLinkGraph"/>, decorates the existing
    /// <see cref="IKnowledgeStore"/> with <see cref="LinkedKnowledgeStore"/>,
    /// and optionally registers a <see cref="DocumentLinkExtractor"/> for
    /// LLM-based post-ingestion linking.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional configuration callback. When <see langword="null"/>, defaults are used.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKnowledgeLinking(
        this IServiceCollection services,
        Action<KnowledgeLinkingOptions>? configure = null)
    {
        var options = new KnowledgeLinkingOptions();
        configure?.Invoke(options);

        // Register the link graph (skip if already registered by user)
        services.TryAddSingleton<IDocumentLinkGraph, InMemoryDocumentLinkGraph>();

        // Register options so they're available to the decorator
        services.AddSingleton(options.SearchOptions);

        if (options.AutoLinkOnIngest)
        {
            services.AddSingleton(sp =>
            {
                var model = sp.GetRequiredService<IAgentModel>();
                var store = sp.GetRequiredService<IKnowledgeStore>();
                var graph = sp.GetRequiredService<IDocumentLinkGraph>();
                return new DocumentLinkExtractor(
                    model, store, graph, options.SimilarityThreshold);
            });
        }

        // Decorate the registered IKnowledgeStore with LinkedKnowledgeStore.
        // We capture the current registration and replace it with the decorator.
        var existingDescriptor = FindServiceDescriptor<IKnowledgeStore>(services);
        if (existingDescriptor is not null)
        {
            services.Remove(existingDescriptor);

            services.AddSingleton<IKnowledgeStore>(sp =>
            {
                var inner = CreateInstance<IKnowledgeStore>(sp, existingDescriptor);
                var graph = sp.GetRequiredService<IDocumentLinkGraph>();
                var searchOptions = sp.GetRequiredService<LinkedSearchOptions>();
                return new LinkedKnowledgeStore(inner, graph, searchOptions);
            });
        }

        return services;
    }

    private static ServiceDescriptor? FindServiceDescriptor<T>(IServiceCollection services) =>
        services.LastOrDefault(d => d.ServiceType == typeof(T));

    private static T CreateInstance<T>(IServiceProvider sp, ServiceDescriptor descriptor) where T : notnull
    {
        if (descriptor.ImplementationInstance is T instance)
            return instance;

        if (descriptor.ImplementationFactory is not null)
            return (T)descriptor.ImplementationFactory(sp);

        if (descriptor.ImplementationType is not null)
            return (T)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);

        throw new InvalidOperationException(
            $"Cannot create instance for service {typeof(T).Name} from descriptor.");
    }
}

/// <summary>
/// Options for configuring the knowledge linking pipeline registered via
/// <see cref="KnowledgeLinkingExtensions.AddKnowledgeLinking"/>.
/// </summary>
public sealed class KnowledgeLinkingOptions
{
    /// <summary>
    /// When <see langword="true"/>, a <see cref="DocumentLinkExtractor"/> is registered
    /// and available for post-ingestion linking. Requires an <see cref="IAgentModel"/>
    /// to be registered in DI. Default is <see langword="true"/>.
    /// </summary>
    public bool AutoLinkOnIngest { get; set; } = true;

    /// <summary>
    /// Options controlling how search results are expanded through the link graph.
    /// </summary>
    public LinkedSearchOptions SearchOptions { get; set; } = new();

    /// <summary>
    /// Minimum vector similarity score for a chunk to be considered a link candidate
    /// during <see cref="DocumentLinkExtractor"/> analysis. Default is <c>0.7</c>.
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.7f;
}
