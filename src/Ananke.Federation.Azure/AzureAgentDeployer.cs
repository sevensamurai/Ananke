using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Prompts;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Azure.AI.Projects.Agents;

namespace Ananke.Federation.Azure;

/// <summary>
/// Deploys an Ananke workflow manifest to Azure AI Agent Service.
/// Creates, updates, and deletes agents via <see cref="AgentAdministrationClient"/>
/// using JSON-serialized <c>DeclarativeAgentDefinition</c> payloads.
/// </summary>
/// <remarks>
/// Each manifest job of type <c>"agent"</c> becomes a declarative agent with:
/// <list type="bullet">
///   <item>Model name resolved by <see cref="AzureModelMapper"/></item>
///   <item>Tool definitions translated by <see cref="AzureToolSchemaTranslator"/> into function/code-interpreter/bing/search JSON</item>
///   <item>System prompt compiled by <see cref="ISystemPromptCompiler"/></item>
/// </list>
/// </remarks>
public sealed class AzureAgentDeployer(
    AzureAgentCredentialProvider credentialProvider,
    IDeploymentRegistry deploymentRegistry,
    AzureModelMapper? modelMapper = null,
    AzureToolSchemaTranslator? toolSchemaTranslator = null,
    ISystemPromptCompiler? systemPromptCompiler = null) : IFederationDeployer
{
    private readonly AzureAgentCredentialProvider _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
    private readonly IDeploymentRegistry _deploymentRegistry = deploymentRegistry ?? throw new ArgumentNullException(nameof(deploymentRegistry));
    private readonly AzureModelMapper _modelMapper = modelMapper ?? new();
    private readonly AzureToolSchemaTranslator _toolSchemaTranslator = toolSchemaTranslator ?? new();
    private readonly ISystemPromptCompiler _systemPromptCompiler = systemPromptCompiler ?? new AzureSystemPromptCompiler();

    /// <inheritdoc />
    public string Platform => "azure-ai";

    /// <inheritdoc />
    public async Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest, ToolKit toolKit, CancellationToken ct = default)
    {
        var validator = new AzureAgentValidator(_credentialProvider, _modelMapper);
        return await validator.ValidateAsync(manifest, toolKit, ct);
    }

    /// <inheritdoc />
    public async Task<DeploymentRecord> DeployAsync(
        WorkflowManifest manifest, ToolKit toolKit, DeployOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(toolKit);
        ArgumentNullException.ThrowIfNull(options);

        var report = await ValidateAsync(manifest, toolKit, ct);
        if (!report.IsDeployable)
        {
            var errors = string.Join("; ", report.Errors.Select(e => $"[{e.Code}] {e.Message}"));
            throw new InvalidOperationException($"Manifest is not deployable to Azure AI Agent Service: {errors}");
        }

        var deploymentId = $"azure-ai-{manifest.Name}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var record = new DeploymentRecord
        {
            DeploymentId = deploymentId,
            WorkflowName = manifest.Name,
            Platform = Platform,
            Version = options.Tags.Count > 0 ? options.Tags[0] : "1.0.0",
            Status = DeploymentStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Tags = options.Tags
        };

        await _deploymentRegistry.RegisterAsync(record, ct);

        try
        {
            await _deploymentRegistry.UpdateStatusAsync(deploymentId, DeploymentStatus.Deploying, ct);

            var client = await GetClientAsync(ct);
            var toolsJson = _toolSchemaTranslator.Translate(toolKit.Tools.Values);

            string? lastAgentId = null;
            foreach (var (jobName, job) in manifest.Jobs)
            {
                if (!string.Equals(job.Type, "agent", StringComparison.OrdinalIgnoreCase))
                    continue;

                var modelName = ResolveModel(manifest, job);
                var instructions = _systemPromptCompiler.Compile(manifest, jobName);

                var requestBody = BuildAgentRequestBody(modelName, instructions, toolsJson);
                var content = BinaryContent.Create(BinaryData.FromString(requestBody));

                var response = await client.CreateAgentAsync(content, manifest.Name);
                var responseJson = response.GetRawResponse().Content.ToString();
                var responseDoc = JsonDocument.Parse(responseJson);
                lastAgentId = responseDoc.RootElement.TryGetProperty("id", out var idProp)
                    ? idProp.GetString()
                    : null;
            }

            var platformResourceId = lastAgentId ?? $"azure-ai/agents/{manifest.Name}";

            await _deploymentRegistry.UpdateStatusAsync(deploymentId, DeploymentStatus.Active, ct);

            return record with
            {
                Status = DeploymentStatus.Active,
                PlatformResourceId = platformResourceId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            await _deploymentRegistry.UpdateStatusAsync(deploymentId, DeploymentStatus.Failed, ct);
            throw;
        }
        catch (Exception)
        {
            await _deploymentRegistry.UpdateStatusAsync(deploymentId, DeploymentStatus.Failed, CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task TeardownAsync(string deploymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);

        var record = await _deploymentRegistry.GetAsync(deploymentId, ct)
            ?? throw new KeyNotFoundException($"Deployment '{deploymentId}' not found.");

        if (record.PlatformResourceId is not null)
        {
            var client = await GetClientAsync(ct);
            await client.DeleteAgentAsync(record.PlatformResourceId, ct);
        }

        await _deploymentRegistry.UpdateStatusAsync(deploymentId, DeploymentStatus.Stopped, ct);
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(string deploymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);
        return _deploymentRegistry.UpdateStatusAsync(deploymentId, DeploymentStatus.Failed, ct);
    }

    private async Task<AgentAdministrationClient> GetClientAsync(CancellationToken ct)
    {
        var credential = await _credentialProvider.GetCredentialAsync(Platform, ct);
        return credential as AgentAdministrationClient
            ?? throw new InvalidOperationException("Failed to obtain Azure AI Agent Service client.");
    }

    private string ResolveModel(WorkflowManifest manifest, JobDefinition job)
    {
        if (job.ModelAlias is not null && manifest.Models.TryGetValue(job.ModelAlias, out var modelDef))
        {
            return _modelMapper.Map(modelDef)
                ?? throw new InvalidOperationException(
                    $"Model '{modelDef.Provider}/{modelDef.Model}' has no Azure AI equivalent.");
        }

        return Models.OpenAI.Gpt54Mini; // sensible default
    }

    internal static string BuildAgentRequestBody(string model, string instructions, JsonArray tools)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["instructions"] = instructions,
            ["tools"] = tools.DeepClone()
        };

        return body.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
