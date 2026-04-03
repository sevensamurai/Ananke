using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Ananke.Orchestration.Knowledge.Catalog;

namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// A named section within a <see cref="KnowledgeBase"/>. Pairs an <see cref="IKnowledgeStore"/>
/// with a descriptive name so that consumers can address specific knowledge domains
/// (e.g. "pets", "policies", "faq") without passing multiple store parameters.
/// </summary>
public sealed record KnowledgeSection(string Name, IKnowledgeStore Store);

/// <summary>
/// A search result from <see cref="KnowledgeBase.SearchAsync"/> that includes which
/// section the chunk came from.
/// </summary>
public sealed record KnowledgeBaseResult
{
    /// <summary>The section name this result originated from.</summary>
    public required string Section { get; init; }

    /// <summary>The matched knowledge chunk.</summary>
    public required KnowledgeChunk Chunk { get; init; }
}

/// <summary>
/// Groups one or more named <see cref="KnowledgeSection"/>s with a shared
/// <see cref="IKnowledgeCatalog"/> into a single dependency. Pass one <see cref="KnowledgeBase"/>
/// instead of threading N stores + 1 catalog through every constructor.
/// </summary>
/// <remarks>
/// <para>
/// Sections are accessed by name via the indexer (<c>kb["pets"]</c>) or the
/// <see cref="TryGetSection"/> method. The <see cref="Catalog"/> is always available.
/// </para>
/// <para>
/// <see cref="SearchAsync"/> fans out to all sections in parallel and returns
/// results merged by score — a single search across the entire knowledge base.
/// </para>
/// <para>
/// Implements <see cref="IEnumerable{KnowledgeSection}"/> for easy iteration over all sections.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var kb = new KnowledgeBase(
///     [new("pets", petStore), new("knowledge", knowledgeStore)],
///     catalog);
///
/// // Search across all sections
/// var results = await kb.SearchAsync("family-friendly dogs");
/// foreach (var r in results)
///     Console.WriteLine($"[{r.Section}] {r.Chunk.Text} (score: {r.Chunk.Score:F3})");
///
/// // Or target a specific section
/// var petResults = await kb["pets"].Store.SearchAsync("golden retriever");
/// </code>
/// </example>
public sealed class KnowledgeBase : IEnumerable<KnowledgeSection>
{
    private readonly Dictionary<string, KnowledgeSection> _sections;

    /// <summary>
    /// Creates a knowledge base from the given sections and catalog.
    /// </summary>
    /// <param name="sections">Named knowledge store sections. Names must be unique.</param>
    /// <param name="catalog">Shared document-level catalog across all sections.</param>
    /// <exception cref="ArgumentException">Thrown when duplicate section names are provided.</exception>
    public KnowledgeBase(IEnumerable<KnowledgeSection> sections, IKnowledgeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(catalog);

        _sections = new Dictionary<string, KnowledgeSection>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            if (!_sections.TryAdd(section.Name, section))
                throw new ArgumentException($"Duplicate section name: '{section.Name}'.", nameof(sections));
        }

        Catalog = catalog;
    }

    /// <summary>Shared document-level catalog for browsing and discovery across all sections.</summary>
    public IKnowledgeCatalog Catalog { get; }

    /// <summary>Number of sections in this knowledge base.</summary>
    public int Count => _sections.Count;

    /// <summary>
    /// Gets the section with the specified name.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when no section with the given name exists.</exception>
    public KnowledgeSection this[string name] =>
        _sections.TryGetValue(name, out var section)
            ? section
            : throw new KeyNotFoundException(
                $"Knowledge section '{name}' not found. Available: {string.Join(", ", _sections.Keys)}.");

    /// <summary>
    /// Tries to get the section with the specified name.
    /// </summary>
    public bool TryGetSection(string name, [NotNullWhen(true)] out KnowledgeSection? section) =>
        _sections.TryGetValue(name, out section);

    /// <summary>
    /// Searches all sections in parallel and returns results merged by descending score.
    /// Each result is tagged with the section it came from.
    /// </summary>
    /// <param name="query">Natural language search query.</param>
    /// <param name="options">
    /// Search options applied to each section independently. <see cref="SearchOptions.TopK"/>
    /// is applied per-section; the merged result list may contain up to
    /// <c>TopK × section count</c> entries.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<KnowledgeBaseResult>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        var tasks = _sections.Values.Select(async section =>
        {
            var results = await section.Store.SearchAsync(query, options, ct);
            return results.Select(chunk => new KnowledgeBaseResult
            {
                Section = section.Name,
                Chunk = chunk
            });
        });

        var allResults = await Task.WhenAll(tasks);
        return allResults
            .SelectMany(r => r)
            .OrderByDescending(r => r.Chunk.Score)
            .ToList();
    }

    /// <inheritdoc />
    public IEnumerator<KnowledgeSection> GetEnumerator() => _sections.Values.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
