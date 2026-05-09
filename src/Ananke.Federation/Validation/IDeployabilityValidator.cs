using Ananke.Design;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Validation;

/// <summary>
/// Structural deployment validation — checks manifest and toolkit compatibility
/// with a target platform without requiring platform credentials or network access.
/// </summary>
public interface IDeployabilityValidator
{
    /// <summary>
    /// Validates a manifest and toolkit against structural deployment rules for the
    /// specified target platform. Returns diagnostics with codes FED001–FED023.
    /// </summary>
    /// <param name="manifest">The workflow manifest to validate.</param>
    /// <param name="toolKit">The toolkit bound to the workflow.</param>
    /// <param name="targetPlatform">Target platform identifier (e.g. <c>"vertex-ai"</c>).</param>
    DeployabilityReport Validate(WorkflowManifest manifest, ToolKit toolKit, string targetPlatform);
}
