using Ananke.Design;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Azure.AI.Projects.Agents;

namespace Ananke.Federation.Azure;

/// <summary>
/// Live platform validator for Azure AI Agent Service. Checks credentials by
/// attempting to create an <see cref="AgentAdministrationClient"/>, validates
/// model mappings, and checks tool compatibility.
/// </summary>
public sealed class AzureAgentValidator : IPlatformValidator
{
    private readonly AzureAgentCredentialProvider _credentialProvider;
    private readonly AzureModelMapper _modelMapper;

    /// <summary>
    /// Creates an Azure AI Agent Service platform validator.
    /// </summary>
    public AzureAgentValidator(AzureAgentCredentialProvider credentialProvider, AzureModelMapper? modelMapper = null)
    {
        ArgumentNullException.ThrowIfNull(credentialProvider);
        _credentialProvider = credentialProvider;
        _modelMapper = modelMapper ?? new AzureModelMapper();
    }

    /// <inheritdoc />
    public string Platform => "azure-ai";

    /// <inheritdoc />
    public async Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(toolKit);

        var diagnostics = new List<DeployDiagnostic>();

        // Check credentials — attempt to create a client
        var credential = await _credentialProvider.GetCredentialAsync("azure-ai", ct);
        if (credential is not AgentAdministrationClient)
        {
            diagnostics.Add(new DeployDiagnostic
            {
                Severity = DeployDiagnosticSeverity.Error,
                Code = "FED040",
                Message = "Failed to create Azure AI Agent Service client from the configured endpoint.",
                Suggestion = $"Verify the endpoint URI '{_credentialProvider.Endpoint}' and ensure DefaultAzureCredential is configured."
            });
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
                    Code = "FED041",
                    Message = $"Model '{modelDef.Provider}/{modelDef.Model}' (alias '{job.ModelAlias}') has no Azure AI equivalent.",
                    Component = jobName,
                    Suggestion = "Use an OpenAI model (e.g. 'openai/gpt-4.1-mini') or a model available on Azure AI."
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
                    Code = "FED042",
                    Message = $"Tool '{tool.Name}' uses Local execution mode and cannot be deployed to Azure AI Agent Service.",
                    Component = tool.Name,
                    Suggestion = "Use .Callback(uri), .OpenApi(uri), or .PlatformNative() on the ToolBuilder, or add a deployment profile."
                });
            }

            if (tool.ExecutionMode == ToolExecutionMode.Mcp)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Warning,
                    Code = "FED043",
                    Message = $"Tool '{tool.Name}' uses MCP execution mode. Azure AI Agent Service does not natively support MCP; it will be mapped to a function callback.",
                    Component = tool.Name,
                    Suggestion = "Consider using .Callback(uri) or .OpenApi(uri) for native Azure support."
                });
            }

            if (tool.ExecutionMode == ToolExecutionMode.OpenApi && tool.Endpoint?.Uri is null)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Error,
                    Code = "FED044",
                    Message = $"Tool '{tool.Name}' uses OpenApi execution mode but has no endpoint URI pointing to the OpenAPI spec.",
                    Component = tool.Name,
                    Suggestion = "Set the endpoint URI to the OpenAPI spec URL using .OpenApi(new Uri(\"https://...\"))."
                });
            }

            if (tool.ExecutionMode == ToolExecutionMode.PlatformNative &&
                tool.PlatformCapability is not null &&
                !PlatformCapabilities.GetForPlatform("azure-ai").Contains(tool.PlatformCapability))
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Warning,
                    Code = "FED045",
                    Message = $"Tool '{tool.Name}' declares platform capability '{tool.PlatformCapability}' which is not a recognized Azure AI Agent Service tool type. It will be passed through as-is — the platform API will reject it if invalid.",
                    Component = tool.Name,
                    Suggestion = $"Known Azure capabilities: {string.Join(", ", PlatformCapabilities.GetForPlatform("azure-ai"))}. If this is a new capability, this warning can be ignored."
                });
            }
        }

        return new DeployabilityReport { Diagnostics = diagnostics };
    }
}
