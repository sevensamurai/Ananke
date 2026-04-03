using Ananke.Orchestration.Tools;

namespace Ananke.Skills;

/// <summary>
/// Extension methods for <see cref="ToolKit"/> to populate tools from an <see cref="ISkillCatalog"/>.
/// </summary>
public static class ToolKitSkillExtensions
{
    /// <summary>
    /// Searches the catalog for skills matching <paramref name="query"/> and resolves
    /// them into <see cref="ToolDefinition"/> entries in this toolkit.
    /// The catalog search handles score-based ranking; this method resolves all returned skills.
    /// </summary>
    /// <param name="toolkit">The toolkit to populate.</param>
    /// <param name="catalog">The skill catalog to search.</param>
    /// <param name="query">Natural language search query (e.g. <c>"airbnb search lodging"</c>).</param>
    /// <param name="tags">Optional tag filter to narrow results.</param>
    /// <param name="limit">Maximum number of skills to resolve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The same <paramref name="toolkit"/> for fluent chaining.</returns>
    public static async Task<ToolKit> AddFromCatalogAsync(
        this ToolKit toolkit,
        ISkillCatalog catalog,
        string query,
        IReadOnlyList<string>? tags = null,
        int limit = 5,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(toolkit);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var skills = await catalog.SearchAsync(query, tags, limit, ct).ConfigureAwait(false);

        foreach (var skill in skills)
        {
            var tool = await catalog.ResolveAsync(skill, ct).ConfigureAwait(false);
            toolkit.AddTool(tool);
        }

        return toolkit;
    }
}
