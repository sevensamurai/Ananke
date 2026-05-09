namespace Ananke.Federation.Validation;

/// <summary>
/// A single diagnostic finding from deployment validation.
/// Each diagnostic has a unique code (FED001–FED023) for programmatic handling.
/// </summary>
public sealed record DeployDiagnostic
{
    /// <summary>Severity of this finding.</summary>
    public required DeployDiagnosticSeverity Severity { get; init; }

    /// <summary>Diagnostic code (e.g. <c>"FED001"</c>) for programmatic identification.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable description of the issue.</summary>
    public required string Message { get; init; }

    /// <summary>Component or tool name that triggered the diagnostic.</summary>
    public string? Component { get; init; }

    /// <summary>Suggested action to resolve the issue.</summary>
    public string? Suggestion { get; init; }
}
