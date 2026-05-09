namespace Ananke.Federation.Validation;

/// <summary>
/// Aggregated result of deployment validation. Contains all diagnostics
/// and a computed <see cref="IsDeployable"/> property.
/// </summary>
public sealed record DeployabilityReport
{
    /// <summary>All diagnostic findings from validation.</summary>
    public required IReadOnlyList<DeployDiagnostic> Diagnostics { get; init; }

    /// <summary>
    /// <see langword="true"/> when no <see cref="DeployDiagnosticSeverity.Error"/>
    /// diagnostics are present; <see langword="false"/> otherwise.
    /// </summary>
    public bool IsDeployable => !Diagnostics.Any(d => d.Severity == DeployDiagnosticSeverity.Error);

    /// <summary>Diagnostics filtered to errors only.</summary>
    public IReadOnlyList<DeployDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == DeployDiagnosticSeverity.Error).ToList();

    /// <summary>Diagnostics filtered to warnings only.</summary>
    public IReadOnlyList<DeployDiagnostic> Warnings =>
        Diagnostics.Where(d => d.Severity == DeployDiagnosticSeverity.Warning).ToList();

    /// <summary>Creates an empty report with no diagnostics (deployable).</summary>
    public static DeployabilityReport Ok() => new() { Diagnostics = [] };
}
