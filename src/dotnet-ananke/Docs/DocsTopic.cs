namespace Ananke.Tool.Docs;

/// <summary>
/// Represents a single documentation topic discovered from the <c>docs/</c> directory.
/// </summary>
internal sealed record DocsTopic
{
    /// <summary>Short topic key derived from filename (e.g. <c>getting-started</c>, <c>dsl-syntax</c>).</summary>
    public required string Key { get; init; }

    /// <summary>Category from the subdirectory (e.g. <c>guides</c>, <c>reference</c>, <c>about</c>).</summary>
    public required string Category { get; init; }

    /// <summary>Title extracted from the first <c># Heading</c> in the file.</summary>
    public required string Title { get; init; }

    /// <summary>Relative path from the docs root (e.g. <c>guides/01-getting-started.md</c>).</summary>
    public required string RelativePath { get; init; }

    /// <summary>Absolute file path on disk.</summary>
    public required string FullPath { get; init; }
}
