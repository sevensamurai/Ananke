namespace Ananke.Tool.Diagnostics;

/// <summary>
/// A structured diagnostic produced by <c>nnke validate</c>, <c>nnke inspect</c>,
/// or other commands that analyze manifests and projects.
/// </summary>
/// <remarks>
/// Each diagnostic has a stable <see cref="Code"/> (e.g. <c>ANANKE_TOPO_001</c>)
/// that agents can branch on without string-matching error messages.
/// </remarks>
internal sealed record Diagnostic
{
    /// <summary>Stable error code (e.g. <c>ANANKE_TOPO_001</c>).</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable error message.</summary>
    public required string Message { get; init; }

    /// <summary>One-sentence fix suggestion.</summary>
    public required string Hint { get; init; }

    /// <summary>Optional <c>nnke docs</c> reference for more detail.</summary>
    public string? DocsRef { get; init; }
}
