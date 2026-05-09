using System.Text.Json;
using System.Text.Json.Nodes;
using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Prompts;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Anthropic.Translators;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Anthropic;

/// <summary>
/// Deploys an Ananke workflow manifest to Claude Managed Agents (Beta).
/// Creates one <c>/v1/environments</c> per workflow and one <c>/v1/agents</c>
/// per agent job, then persists the returned IDs via <see cref="IDeploymentRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each manifest job of type <c>"agent"</c> becomes a Claude managed agent with:
/// </para>
/// <list type="bullet">
///   <item>Model name resolved by <see cref="ClaudeModelMapper"/></item>
///   <item>Tool definitions translated by <see cref="AnthropicToolSchemaTranslator"/></item>
///   <item>System prompt compiled by <see cref="ISystemPromptCompiler"/> (XML-structured)</item>
/// </list>
/// <para>
/// Status: <b>Preview</b> — uses Beta endpoints pinned to
/// <c>anthropic-beta: agents-2025-05-14</c>. Schemas may change before GA.
/// </para>
/// </remarks>
public sealed class ClaudeDeployer(
    ClaudeCredentialProvider credentialProvider,
    IDeploymentRegistry deploymentRegistry,
    ClaudeModelMapper? modelMapper = null,
    AnthropicToolSchemaTranslator? toolSchemaTranslator = null,
    ISystemPromptCompiler? systemPromptCompiler = null,
    Func<string, ClaudeManagedAgentsClient>? clientFactory = null) : IFederationDeployer
{
    private readonly ClaudeCredentialProvider _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
    private readonly IDeploymentRegistry _deploymentRegistry = deploymentRegistry ?? throw new ArgumentNullException(nameof(deploymentRegistry));
    private readonly ClaudeModelMapper _modelMapper = modelMapper ?? new();
    private readonly AnthropicToolSchemaTranslator _toolSchemaTranslator = toolSchemaTranslator ?? new();
    private readonly ISystemPromptCompiler _systemPromptCompiler = systemPromptCompiler ?? new ClaudeSystemPromptCompiler();
    private readonly Func<string, ClaudeManagedAgentsClient> _clientFactory =
        clientFactory ?? (key => new ClaudeManagedAgentsClient(key));

    /// <inheritdoc />
    public string Platform => "claude";

    /// <inheritdoc />
    public async Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest, ToolKit toolKit, CancellationToken ct = default)
    {
        var validator = new ClaudeValidator(_credentialProvider, _modelMapper);
        return await validator.ValidateAsync(manifest, toolKit, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Creates one <c>/v1/environments</c> resource for the workflow and one
    /// <c>/v1/agents</c> resource per agent job. The returned IDs are serialised as
    /// JSON into <see cref="DeploymentRecord.PlatformResourceId"/> for use by
    /// <see cref="TeardownAsync"/>.
    /// </remarks>
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
            throw new InvalidOperationException($"Manifest is not deployable to Claude: {errors}");
        }

        var deploymentId = $"claude-{manifest.Name}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
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

            var apiKey = await _credentialProvider.GetCredentialAsync(Platform, ct) as string
                ?? throw new InvalidOperationException("Failed to obtain Anthropic API key.");

            using var client = _clientFactory(apiKey);
            var toolsJson = (System.Text.Json.Nodes.JsonArray)_toolSchemaTranslator.Translate(toolKit.Tools.Values);

            // One environment per workflow
            var environmentId = await client.CreateEnvironmentAsync(manifest.Name, ct);

            // One agent per agent job
            var agentIds = new List<string>();
            foreach (var (jobName, job) in manifest.Jobs)
            {
                if (!string.Equals(job.Type, "agent", StringComparison.OrdinalIgnoreCase))
                    continue;

                var model = ResolveModel(manifest, job);
                var systemPrompt = _systemPromptCompiler.Compile(manifest, jobName);
                var agentName = $"{manifest.Name}-{jobName}";

                var agentId = await client.CreateAgentAsync(
                    agentName, model, systemPrompt, toolsJson, environmentId, ct);

                agentIds.Add(agentId);
            }

            var platformResourceId = SerializeResourceIds(environmentId, agentIds);

            var activeRecord = record with
            {
                Status = DeploymentStatus.Active,
                PlatformResourceId = platformResourceId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _deploymentRegistry.UpdateAsync(activeRecord, ct);

            return activeRecord;
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
    /// <remarks>
    /// Deletes every agent and the environment recorded in
    /// <see cref="DeploymentRecord.PlatformResourceId"/>, then marks the deployment
    /// as <see cref="DeploymentStatus.Stopped"/>. Safe to call when
    /// <c>PlatformResourceId</c> is absent (e.g. a deployment that never reached Active).
    /// </remarks>
    public async Task TeardownAsync(string deploymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);

        var record = await _deploymentRegistry.GetAsync(deploymentId, ct)
            ?? throw new KeyNotFoundException($"Deployment '{deploymentId}' not found.");

        if (record.PlatformResourceId is not null)
        {
            var apiKey = await _credentialProvider.GetCredentialAsync(Platform, ct) as string
                ?? throw new InvalidOperationException("Failed to obtain Anthropic API key.");

            using var client = _clientFactory(apiKey);
            var (environmentId, agentIds) = DeserializeResourceIds(record.PlatformResourceId);

            foreach (var agentId in agentIds)
                await client.DeleteAgentAsync(agentId, ct);

            if (environmentId is not null)
                await client.DeleteEnvironmentAsync(environmentId, ct);
        }

        await _deploymentRegistry.UpdateStatusAsync(deploymentId, DeploymentStatus.Stopped, ct);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task MarkFailedAsync(string deploymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);
        return _deploymentRegistry.UpdateStatusAsync(deploymentId, DeploymentStatus.Failed, ct);
    }

    private string ResolveModel(WorkflowManifest manifest, JobDefinition job)
    {
        if (job.ModelAlias is not null && manifest.Models.TryGetValue(job.ModelAlias, out var modelDef))
        {
            return _modelMapper.Map(modelDef)
                ?? throw new InvalidOperationException(
                    $"Model '{modelDef.Provider}/{modelDef.Model}' has no Claude equivalent.");
        }

        return Models.Anthropic.Sonnet4;
    }

    /// <summary>
    /// Serialises environment and agent IDs as a compact JSON string for storage in
    /// <see cref="DeploymentRecord.PlatformResourceId"/>.
    /// Format: <c>{"env":"env-xxx","agents":["agent-aaa","agent-bbb"]}</c>
    /// </summary>
    internal static string SerializeResourceIds(string environmentId, IEnumerable<string> agentIds)
    {
        var obj = new JsonObject
        {
            ["env"] = environmentId,
            ["agents"] = new JsonArray(agentIds.Select(id => JsonValue.Create(id)).ToArray<JsonNode?>())
        };
        return obj.ToJsonString();
    }

    internal static (string? EnvironmentId, IReadOnlyList<string> AgentIds) DeserializeResourceIds(string json)
    {
        try
        {
            var obj = JsonNode.Parse(json) as JsonObject;
            var envId = obj?["env"]?.GetValue<string>();
            var agents = obj?["agents"] as JsonArray;
            var agentIds = agents?.Select(n => n?.GetValue<string>())
                                  .Where(s => s is not null)
                                  .Select(s => s!)
                                  .ToList()
                          ?? [];
            return (envId, agentIds);
        }
        catch
        {
            return (null, []);
        }
    }

    internal static string BuildAgentRequestBody(string model, string instructions, JsonArray tools)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["system"] = instructions,
            ["tools"] = tools.DeepClone()
        };

        return body.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
