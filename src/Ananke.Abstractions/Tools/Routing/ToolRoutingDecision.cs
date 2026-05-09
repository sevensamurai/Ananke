namespace Ananke.Abstractions.Tools.Routing;

/// <summary>Output of an <see cref="ISmartToolRouter"/> stage.</summary>
public sealed record ToolRoutingDecision
{
    /// <summary>If false, the frontier model should be invoked with no tools at all.</summary>
    public required bool UseTools { get; init; }

    /// <summary>
    /// Subset of <see cref="ToolRoutingRequest.Candidates"/> to keep.
    /// MUST be a subset — stages narrow, they never invent tools.
    /// </summary>
    public IReadOnlyList<ToolMemoryEntry> SelectedTools { get; init; } = [];

    /// <summary>
    /// Optional pre-extracted argument hint. v1: always null; reserved for v2.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ArgumentHint { get; init; }

    /// <summary>Self-reported confidence of this stage.</summary>
    public required RoutingConfidence Confidence { get; init; }

    /// <summary>If true, the chain stops at this stage.</summary>
    public bool Terminal { get; init; }

    /// <summary>Free-form rationale for traces / debugging only.</summary>
    public string? Rationale { get; init; }
}
