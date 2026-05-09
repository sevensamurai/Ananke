namespace Ananke.Federation.Validation;

/// <summary>
/// Severity level of a deployment diagnostic finding.
/// </summary>
public enum DeployDiagnosticSeverity
{
    /// <summary>Informational finding — does not block deployment.</summary>
    Info,

    /// <summary>Warning — deployment may succeed but behavior could be degraded.</summary>
    Warning,

    /// <summary>Error — deployment will fail or produce incorrect behavior.</summary>
    Error
}
