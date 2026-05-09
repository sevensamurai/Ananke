using Ananke.Design;
using Ananke.Federation.Credentials;
using Ananke.Federation.Deployment;
using Ananke.Federation.Google.AgentRuntime;
using Ananke.Federation.Prompts;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Google.Translators;
using Ananke.Orchestration.Tools;
using Google.GenAI.Types;

namespace Ananke.Federation.Google;

/// <summary>
/// Deploys an Ananke workflow manifest to Gemini Enterprise Agent Platform (Agent Runtime).
/// Creates, updates, and deletes agents from manifests.
/// </summary>
/// <remarks>
/// This deployer translates an Ananke manifest into Agent Platform's agent configuration:
/// <list type="bullet">
///   <item>Model selection via <see cref="VertexAIModelMapper"/></item>
///   <item>Tool schema translation via <see cref="GeminiToolSchemaTranslator"/></item>
///   <item>System prompt compilation via <see cref="ISystemPromptCompiler"/></item>
/// </list>
/// </remarks>
public sealed class VertexAIDeployer : IFederationDeployer
{
    private readonly IFederationCredentialProvider _credentialProvider;
    private readonly IDeploymentRegistry _deploymentRegistry;
    private readonly VertexAIModelMapper _modelMapper;
    private readonly GeminiToolSchemaTranslator _toolSchemaTranslator;
    private readonly ISystemPromptCompiler _systemPromptCompiler;
    private readonly IAgentRuntimeClient? _agentRuntimeClient;

    /// <summary>
    /// Creates a deployer with production defaults. An <see cref="AgentRuntimeClient"/>
    /// backed by Application Default Credentials is constructed on first use.
    /// </summary>
    public VertexAIDeployer(
        VertexAICredentialProvider credentialProvider,
        IDeploymentRegistry deploymentRegistry,
        VertexAIModelMapper? modelMapper = null,
        GeminiToolSchemaTranslator? toolSchemaTranslator = null,
        ISystemPromptCompiler? systemPromptCompiler = null)
        : this((IFederationCredentialProvider)credentialProvider, deploymentRegistry, modelMapper, toolSchemaTranslator, systemPromptCompiler, agentRuntimeClient: null) { }

    /// <summary>
    /// Creates a deployer with an explicit <see cref="IAgentRuntimeClient"/> seam for testing.
    /// Accepts any <see cref="IFederationCredentialProvider"/> so tests can inject a fake.
    /// </summary>
    internal VertexAIDeployer(
        IFederationCredentialProvider credentialProvider,
        IDeploymentRegistry deploymentRegistry,
        VertexAIModelMapper? modelMapper,
        GeminiToolSchemaTranslator? toolSchemaTranslator,
        ISystemPromptCompiler? systemPromptCompiler,
        IAgentRuntimeClient? agentRuntimeClient)
    {
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _deploymentRegistry = deploymentRegistry ?? throw new ArgumentNullException(nameof(deploymentRegistry));
        _modelMapper = modelMapper ?? new();
        _toolSchemaTranslator = toolSchemaTranslator ?? new();
        _systemPromptCompiler = systemPromptCompiler ?? new VertexAISystemPromptCompiler();
        _agentRuntimeClient = agentRuntimeClient;
    }

    /// <inheritdoc />
    public string Platform => AgentPlatformConstants.Platform;

    /// <inheritdoc />
    public async Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest, ToolKit toolKit, CancellationToken ct = default)
    {
        var validator = new VertexAIValidator(_credentialProvider, _modelMapper);
        return await validator.ValidateAsync(manifest, toolKit, ct);
    }

    /// <inheritdoc />
    public async Task<DeploymentRecord> DeployAsync(
        WorkflowManifest manifest, ToolKit toolKit, DeployOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(toolKit);
        ArgumentNullException.ThrowIfNull(options);

        // Validate first
        var report = await ValidateAsync(manifest, toolKit, ct);
        if (!report.IsDeployable)
        {
            var errors = string.Join("; ", report.Errors.Select(e => $"[{e.Code}] {e.Message}"));
            throw new InvalidOperationException($"Manifest is not deployable to Gemini Enterprise Agent Platform: {errors}");
        }

        // Create deployment record in Pending state
        var deploymentId = $"{AgentPlatformConstants.Platform}-{manifest.Name}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
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

            // Resolve credentials — fail fast if ADC is not configured
            var credential = await _credentialProvider.GetCredentialAsync(AgentPlatformConstants.Platform, ct)
                ?? throw new InvalidOperationException("Failed to obtain Gemini Enterprise Agent Platform credentials.");

            // Resolve the Agent Runtime client — use the injected seam (tests) or build
            // a production client from the credential provider's project/location.
            var runtimeClient = _agentRuntimeClient
                ?? BuildRuntimeClient();

            // Deploy each agent-type job as a separate Agent Runtime agent.
            string? lastResourceName = null;
            foreach (var (jobName, job) in manifest.Jobs)
            {
                if (!string.Equals(job.Type, "agent", StringComparison.OrdinalIgnoreCase))
                    continue;

                var modelName = ResolveModel(manifest, job);
                var tools = (IReadOnlyList<Tool>)_toolSchemaTranslator.Translate(toolKit.Tools.Values);
                var instructions = _systemPromptCompiler.Compile(manifest, jobName);

                var definition = new AgentDefinition
                {
                    DisplayName = $"{manifest.Name}/{jobName}",
                    Model = modelName,
                    SystemInstructions = instructions,
                    Tools = tools
                };

                lastResourceName = await runtimeClient.CreateAgentAsync(definition, ct);
            }

            var platformResourceId = lastResourceName ?? $"projects/-/locations/-/agents/{manifest.Name}";
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
            var runtimeClient = _agentRuntimeClient
                ?? BuildRuntimeClient();

            await runtimeClient.DeleteAgentAsync(record.PlatformResourceId, ct);
        }

        await _deploymentRegistry.UpdateStatusAsync(deploymentId, DeploymentStatus.Stopped, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task MarkFailedAsync(string deploymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);
        return _deploymentRegistry.UpdateStatusAsync(deploymentId, DeploymentStatus.Failed, ct);
    }

    private IAgentRuntimeClient BuildRuntimeClient()
    {
        if (_credentialProvider is VertexAICredentialProvider p)
            return new AgentRuntimeClient(p.Project, p.Location);

        throw new InvalidOperationException(
            "Cannot construct AgentRuntimeClient: credential provider is not a VertexAICredentialProvider. " +
            "Inject an IAgentRuntimeClient via the internal constructor.");
    }

    private string ResolveModel(WorkflowManifest manifest, JobDefinition job)
    {
        if (job.ModelAlias is not null && manifest.Models.TryGetValue(job.ModelAlias, out var modelDef))
        {
            return _modelMapper.Map(modelDef)
                ?? throw new InvalidOperationException(
                    $"Model '{modelDef.Provider}/{modelDef.Model}' has no Gemini Enterprise Agent Platform equivalent.");
        }

        return Models.Google.Gemini31Flash; // sensible default matching the new platform baseline
    }
}
