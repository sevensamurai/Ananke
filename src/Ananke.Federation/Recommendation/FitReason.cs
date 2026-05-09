namespace Ananke.Federation.Recommendation;

/// <summary>
/// A single human-readable reason that contributed to a platform's fit score.
/// </summary>
public sealed record FitReason
{
    /// <summary>Whether this reason is a positive signal, negative signal, or hard blocker.</summary>
    public required FitReasonKind Kind { get; init; }

    /// <summary>Human-readable description of the signal.</summary>
    public required string Message { get; init; }

    /// <summary>Platform-native capability name, when applicable.</summary>
    public string? Capability { get; init; }

    /// <summary>Job or tool name that triggered this reason, when applicable.</summary>
    public string? Component { get; init; }
}
