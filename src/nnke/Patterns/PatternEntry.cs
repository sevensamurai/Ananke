namespace Ananke.Tool.Patterns;

/// <summary>
/// Describes a recognized workflow or agentic pattern that Ananke supports.
/// Used by <c>nnke patterns</c> and <c>nnke inspect</c> for pattern display and detection.
/// </summary>
internal sealed record PatternEntry
{
    /// <summary>Short key used in CLI flags and scaffold (e.g. <c>review-critique</c>).</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable title (e.g. <c>Review and Critique (Generator-Critic)</c>).</summary>
    public required string Title { get; init; }

    /// <summary>Whether this pattern is manifest-driven (<c>manifest</c>) or code-driven (<c>code</c>).</summary>
    public required string Style { get; init; }

    /// <summary>Short topology summary (e.g. <c>generator → critic → [loop until approved] → End</c>).</summary>
    public required string Topology { get; init; }

    /// <summary>DSL equivalent if the pattern can be expressed in the manifest DSL, otherwise <c>null</c>.</summary>
    public string? DslEquivalent { get; init; }

    /// <summary>C# API entry point (e.g. <c>AgenticPattern.ReviewCritique&lt;TState&gt;(name)</c>).</summary>
    public required string ApiEntryPoint { get; init; }

    /// <summary>Typical use cases for this pattern.</summary>
    public required IReadOnlyList<string> UseCases { get; init; }

    /// <summary>Scaffold command to generate a project using this pattern.</summary>
    public required string ScaffoldCommand { get; init; }

    /// <summary>Reference to the relevant documentation topic.</summary>
    public required string DocsRef { get; init; }

    /// <summary>Multi-line description of how the pattern works.</summary>
    public required string Description { get; init; }

    /// <summary>Short code example showing the builder API.</summary>
    public required string ApiExample { get; init; }
}
