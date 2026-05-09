using Ananke.Design;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Validation;

/// <summary>
/// Live platform validation — checks credentials, model availability, and quotas
/// by contacting the target platform's APIs.
/// </summary>
public interface IPlatformValidator
{
    /// <summary>Platform identifier this validator targets.</summary>
    string Platform { get; }

    /// <summary>
    /// Validates a manifest and toolkit against the live platform.
    /// Requires network access and valid credentials.
    /// </summary>
    Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        CancellationToken ct = default);
}
