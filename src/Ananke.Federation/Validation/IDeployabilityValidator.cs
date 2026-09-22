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
    /// <param name="modelCatalogue">
    /// Optional model-alias catalogue used to resolve <see cref="ModelDefinition.Ref"/>. Typically
    /// another manifest's <c>models:</c> section — <c>WorkflowManifest.Load(path).Models</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Resolution happens here, not in the parser</b>. A parsed manifest keeps
    /// meaning exactly what its file says; a caller that holds a catalogue supplies it at the point
    /// of validation. With no catalogue, a <c>ref</c> is reported as unresolved rather than guessed
    /// at — which is a true statement about the call rather than an assumption about the model.
    /// </para>
    /// </remarks>
    DeployabilityReport Validate(
        WorkflowManifest manifest,
        ToolKit toolKit,
        string targetPlatform,
        IReadOnlyDictionary<string, ModelDefinition>? modelCatalogue = null);
}
