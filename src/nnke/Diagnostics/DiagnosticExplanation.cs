namespace Ananke.Tool.Diagnostics;

/// <summary>
/// Detailed explanation for a diagnostic code, including a problem description,
/// illustrative examples, fix guidance, and a <c>nnke docs</c> reference.
/// </summary>
internal sealed record DiagnosticExplanation
{
    /// <summary>Stable error code (e.g. <c>ANANKE_TOPO_003</c>).</summary>
    public required string Code { get; init; }

    /// <summary>Short title for the error class.</summary>
    public required string Title { get; init; }

    /// <summary>Multi-line explanation of what causes this error.</summary>
    public required string Description { get; init; }

    /// <summary>DSL or YAML snippet showing a problematic configuration.</summary>
    public required string BadExample { get; init; }

    /// <summary>DSL or YAML snippet showing the corrected configuration.</summary>
    public required string FixExample { get; init; }

    /// <summary><c>nnke docs</c> topic reference.</summary>
    public required string DocsRef { get; init; }
}
