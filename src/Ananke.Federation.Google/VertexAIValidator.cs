using Ananke.Design;
using Ananke.Federation.Credentials;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Google;

/// <summary>
/// Live platform validator for Gemini Enterprise Agent Platform. Checks credentials, model availability,
/// and tool compatibility by interacting with the Google Cloud APIs.
/// </summary>
public sealed class VertexAIValidator : IPlatformValidator
{
    private readonly IFederationCredentialProvider _credentialProvider;
    private readonly VertexAIModelMapper _modelMapper;

    /// <summary>
    /// Creates a Gemini Enterprise Agent Platform validator.
    /// </summary>
    public VertexAIValidator(VertexAICredentialProvider credentialProvider, VertexAIModelMapper? modelMapper = null)
        : this((IFederationCredentialProvider)credentialProvider, modelMapper) { }

    internal VertexAIValidator(IFederationCredentialProvider credentialProvider, VertexAIModelMapper? modelMapper = null)
    {
        ArgumentNullException.ThrowIfNull(credentialProvider);
        _credentialProvider = credentialProvider;
        _modelMapper = modelMapper ?? new VertexAIModelMapper();
    }

    /// <inheritdoc />
    public string Platform => AgentPlatformConstants.Platform;

    /// <inheritdoc />
    public async Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(toolKit);

        var diagnostics = new List<DeployDiagnostic>();

        // Check credentials
        var credential = await _credentialProvider.GetCredentialAsync(AgentPlatformConstants.Platform, ct);
        if (credential is null)
        {
            diagnostics.Add(new DeployDiagnostic
            {
                Severity = DeployDiagnosticSeverity.Error,
                Code = "FED030",
                Message = "Failed to obtain Google Cloud credentials via Application Default Credentials (ADC).",
                Suggestion = "Run 'gcloud auth application-default login' or set GOOGLE_APPLICATION_CREDENTIALS."
            });
            // Cannot proceed without credentials
            return new DeployabilityReport { Diagnostics = diagnostics };
        }

        // Validate models
        foreach (var (jobName, job) in manifest.Jobs)
        {
            if (!string.Equals(job.Type, "agent", StringComparison.OrdinalIgnoreCase))
                continue;

            if (job.ModelAlias is null || !manifest.Models.TryGetValue(job.ModelAlias, out var modelDef))
                continue; // Structural validator catches this

            var mapped = _modelMapper.Map(modelDef);
            if (mapped is null)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Error,
                    Code = "FED031",
                    Message = $"Model '{modelDef.Provider}/{modelDef.Model}' (alias '{job.ModelAlias}') has no Gemini Enterprise Agent Platform equivalent.",
                    Component = jobName,
                    Suggestion = "Use a model with a known Gemini mapping, or set provider to 'google'."
                });
            }
        }

        // Validate tools
        foreach (var tool in toolKit.Tools.Values)
        {
            if (tool.ExecutionMode == ToolExecutionMode.Local)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Error,
                    Code = "FED032",
                    Message = $"Tool '{tool.Name}' uses Local execution mode and cannot be deployed to Gemini Enterprise Agent Platform.",
                    Component = tool.Name,
                    Suggestion = "Use .Callback(uri), .Mcp(uri), .OpenApi(uri), or .PlatformNative() on the ToolBuilder."
                });
            }

            if (tool.ExecutionMode == ToolExecutionMode.PlatformNative &&
                tool.PlatformCapability is not null &&
                !PlatformCapabilities.GetForPlatform("vertex-ai").Contains(tool.PlatformCapability))
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Warning,
                    Code = "FED034",
                    Message = $"Tool '{tool.Name}' declares platform capability '{tool.PlatformCapability}' which is not a recognized Gemini Enterprise Agent Platform built-in. It will be passed through as-is — the platform API will reject it if invalid.",
                    Component = tool.Name,
                    Suggestion = $"Known Google capabilities: {string.Join(", ", PlatformCapabilities.GetForPlatform("vertex-ai"))}. If this is a new capability, this warning can be ignored."
                });
            }
        }

        return new DeployabilityReport { Diagnostics = diagnostics };
    }
}
