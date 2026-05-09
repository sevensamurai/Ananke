using Ananke.Design;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Anthropic;

/// <summary>
/// Live platform validator for Claude Managed Agents. Checks credentials,
/// model availability, and tool compatibility.
/// </summary>
public sealed class ClaudeValidator : IPlatformValidator
{
    private readonly ClaudeCredentialProvider _credentialProvider;
    private readonly ClaudeModelMapper _modelMapper;

    /// <summary>
    /// Creates a Claude platform validator.
    /// </summary>
    public ClaudeValidator(ClaudeCredentialProvider credentialProvider, ClaudeModelMapper? modelMapper = null)
    {
        ArgumentNullException.ThrowIfNull(credentialProvider);
        _credentialProvider = credentialProvider;
        _modelMapper = modelMapper ?? new ClaudeModelMapper();
    }

    /// <inheritdoc />
    public string Platform => "claude";

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
        var credential = await _credentialProvider.GetCredentialAsync("claude", ct);
        if (credential is not string apiKey || string.IsNullOrWhiteSpace(apiKey))
        {
            diagnostics.Add(new DeployDiagnostic
            {
                Severity = DeployDiagnosticSeverity.Error,
                Code = "FED050",
                Message = "Failed to obtain Anthropic API key.",
                Suggestion = "Set the ANTHROPIC_API_KEY environment variable or pass the key to ClaudeCredentialProvider."
            });
            return new DeployabilityReport { Diagnostics = diagnostics };
        }

        // Validate models
        foreach (var (jobName, job) in manifest.Jobs)
        {
            if (!string.Equals(job.Type, "agent", StringComparison.OrdinalIgnoreCase))
                continue;

            if (job.ModelAlias is null || !manifest.Models.TryGetValue(job.ModelAlias, out var modelDef))
                continue;

            var mapped = _modelMapper.Map(modelDef);
            if (mapped is null)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Error,
                    Code = "FED051",
                    Message = $"Model '{modelDef.Provider}/{modelDef.Model}' (alias '{job.ModelAlias}') has no Claude equivalent.",
                    Component = jobName,
                    Suggestion = "Use a model with a known Claude mapping, or set provider to 'anthropic'."
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
                    Code = "FED052",
                    Message = $"Tool '{tool.Name}' uses Local execution mode and cannot be deployed to Claude.",
                    Component = tool.Name,
                    Suggestion = "Use .Callback(uri), .Mcp(uri), .OpenApi(uri), or .PlatformNative() on the ToolBuilder, or add a deployment profile."
                });
            }

            if (tool.ExecutionMode == ToolExecutionMode.OpenApi)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Warning,
                    Code = "FED053",
                    Message = $"Tool '{tool.Name}' uses OpenApi execution mode. Claude does not natively support OpenAPI tools; it will be mapped to a custom tool with the endpoint called via the MCP connector or callback.",
                    Component = tool.Name,
                    Suggestion = "Consider using .Callback(uri) or .Mcp(uri) for Claude deployments."
                });
            }

            if (tool.ExecutionMode == ToolExecutionMode.PlatformNative &&
                tool.PlatformCapability is not null &&
                !PlatformCapabilities.GetForPlatform("claude").Contains(tool.PlatformCapability))
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Warning,
                    Code = "FED054",
                    Message = $"Tool '{tool.Name}' declares platform capability '{tool.PlatformCapability}' which is not a recognized Claude built-in tool type. It will be passed through as-is — the platform API will reject it if invalid.",
                    Component = tool.Name,
                    Suggestion = $"Known Claude capabilities: {string.Join(", ", PlatformCapabilities.GetForPlatform("claude"))}. If this is a new capability, this warning can be ignored."
                });
            }
        }

        return new DeployabilityReport { Diagnostics = diagnostics };
    }
}
