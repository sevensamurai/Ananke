using Ananke.Abstractions.Providers;
using Ananke.Design;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Validation;

/// <summary>
/// Default structural validator. Checks manifest and toolkit compatibility
/// with a target platform using only static analysis — no credentials or
/// network access required.
/// </summary>
/// <remarks>
/// <para>
/// Platform names and known capabilities are loaded from the embedded resource
/// <c>platform-capabilities.json</c>. Update that file before each release to
/// reflect the latest platform SDK surfaces.
/// </para>
/// <para>Diagnostic codes:</para>
/// <list type="bullet">
///   <item><c>FED001</c> — Tool has <see cref="ToolExecutionMode.Local"/> mode (not deployable)</item>
///   <item><c>FED002</c> — Remote tool endpoint is missing or unreachable</item>
///   <item><c>FED003</c> — Unknown platform-native capability (warning — passthrough still works)</item>
///   <item><c>FED010</c> — No model alias defined for an agent job</item>
///   <item><c>FED011</c> — Model alias references undefined model</item>
///   <item><c>FED012</c> — Model provider not supported on target platform <i>(reserved; not emitted)</i></item>
///   <item><c>FED016</c> — Model alias is an unresolved cross-manifest reference</item>
///   <item><c>FED013</c> — Model not available on target platform</item>
///   <item><c>FED014</c> — Custom endpoint may not be reachable from platform</item>
///   <item><c>FED015</c> — No model mapper available for target platform</item>
///   <item><c>FED020</c> — Manifest has no jobs</item>
///   <item><c>FED021</c> — Manifest has no connections (isolated jobs)</item>
///   <item><c>FED022</c> — Toolkit is empty (no tools)</item>
///   <item><c>FED023</c> — Target platform is not recognized</item>
///   <item><c>FED060</c> — Platform identifier alias resolved (e.g. <c>foundry → azure-ai</c>)</item>
/// </list>
/// </remarks>
public sealed class DeployabilityValidator : IDeployabilityValidator
{
    private readonly HashSet<string> _knownPlatforms;
    private readonly Dictionary<string, HashSet<string>> _knownCapabilities;
    private readonly IReadOnlyList<IModelMapper> _modelMappers;

    /// <summary>
    /// Creates a new <see cref="DeployabilityValidator"/> using the built-in
    /// <c>platform-capabilities.json</c> embedded resource.
    /// </summary>
    /// <param name="modelMappers">Available model mappers, one per platform.</param>
    public DeployabilityValidator(IReadOnlyList<IModelMapper>? modelMappers = null)
        : this(modelMappers, knownPlatforms: null, knownCapabilities: null)
    {
    }

    /// <summary>
    /// Creates a new <see cref="DeployabilityValidator"/> with explicit platform
    /// and capability overrides. Use this constructor for testing or when loading
    /// capabilities from an external configuration source.
    /// </summary>
    /// <param name="modelMappers">Available model mappers, one per platform.</param>
    /// <param name="knownPlatforms">Override the set of known platforms. When <see langword="null"/>, uses the embedded resource.</param>
    /// <param name="knownCapabilities">Override the per-platform capability sets. When <see langword="null"/>, uses the embedded resource.</param>
    public DeployabilityValidator(
        IReadOnlyList<IModelMapper>? modelMappers,
        HashSet<string>? knownPlatforms,
        Dictionary<string, HashSet<string>>? knownCapabilities)
    {
        _modelMappers = modelMappers ?? [];
        _knownPlatforms = knownPlatforms ?? PlatformCapabilities.Raw.Platforms;
        _knownCapabilities = knownCapabilities ?? PlatformCapabilities.Raw.Capabilities;
    }

    /// <inheritdoc />
    public DeployabilityReport Validate(
        WorkflowManifest manifest,
        ToolKit toolKit,
        string targetPlatform,
        IReadOnlyDictionary<string, ModelDefinition>? modelCatalogue = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(toolKit);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPlatform);

        var diagnostics = new List<DeployDiagnostic>();

        var resolvedPlatform = ResolvePlatformAlias(targetPlatform, diagnostics);

        ValidatePlatform(resolvedPlatform, diagnostics);
        ValidateTopology(manifest, toolKit, diagnostics);
        ValidateTools(toolKit, resolvedPlatform, diagnostics);
        ValidateModels(manifest, resolvedPlatform, diagnostics, modelCatalogue);

        return new DeployabilityReport { Diagnostics = diagnostics };
    }

    private static string ResolvePlatformAlias(string platform, List<DeployDiagnostic> diagnostics)
    {
        var canonical = PlatformIdentifiers.Resolve(platform);
        if (string.Equals(canonical, platform, StringComparison.OrdinalIgnoreCase))
            return platform;

        diagnostics.Add(new DeployDiagnostic
        {
            Severity = DeployDiagnosticSeverity.Info,
            Code = "FED060",
            Message = $"Platform identifier '{platform}' resolved to canonical identifier '{canonical}'.",
            Suggestion = $"Both '{platform}' and '{canonical}' are accepted. Existing manifests using '{canonical}' are unaffected."
        });

        return canonical;
    }

    private void ValidatePlatform(string targetPlatform, List<DeployDiagnostic> diagnostics)
    {
        if (!_knownPlatforms.Contains(targetPlatform))
        {
            diagnostics.Add(new DeployDiagnostic
            {
                Severity = DeployDiagnosticSeverity.Error,
                Code = "FED023",
                Message = $"Target platform '{targetPlatform}' is not recognized.",
                Suggestion = $"Supported platforms: {string.Join(", ", _knownPlatforms)}"
            });
        }
    }

    private static void ValidateTopology(WorkflowManifest manifest, ToolKit toolKit, List<DeployDiagnostic> diagnostics)
    {
        if (manifest.Jobs.Count == 0)
        {
            diagnostics.Add(new DeployDiagnostic
            {
                Severity = DeployDiagnosticSeverity.Error,
                Code = "FED020",
                Message = "Manifest has no jobs defined."
            });
        }

        if (manifest.Connections.Count == 0 && manifest.Jobs.Count > 1)
        {
            diagnostics.Add(new DeployDiagnostic
            {
                Severity = DeployDiagnosticSeverity.Warning,
                Code = "FED021",
                Message = "Manifest has multiple jobs but no connections — jobs are isolated.",
                Suggestion = "Add connections to define the workflow topology."
            });
        }

        if (toolKit.Tools.Count == 0)
        {
            diagnostics.Add(new DeployDiagnostic
            {
                Severity = DeployDiagnosticSeverity.Warning,
                Code = "FED022",
                Message = "Toolkit is empty — the deployed agent will have no tools.",
                Suggestion = "Add tools to the toolkit before deploying."
            });
        }
    }

    private void ValidateTools(ToolKit toolKit, string targetPlatform, List<DeployDiagnostic> diagnostics)
    {
        foreach (var tool in toolKit.Tools.Values)
        {
            switch (tool.ExecutionMode)
            {
                case ToolExecutionMode.Local:
                    diagnostics.Add(new DeployDiagnostic
                    {
                        Severity = DeployDiagnosticSeverity.Error,
                        Code = "FED001",
                        Message = $"Tool '{tool.Name}' uses Local execution mode and cannot be deployed to a remote platform.",
                        Component = tool.Name,
                        Suggestion = "Use .Callback(uri), .Mcp(uri), .OpenApi(uri), or .PlatformNative() on the ToolBuilder, or add a deployment profile."
                    });
                    break;

                case ToolExecutionMode.Callback:
                case ToolExecutionMode.Mcp:
                case ToolExecutionMode.OpenApi:
                    if (tool.Endpoint is null)
                    {
                        diagnostics.Add(new DeployDiagnostic
                        {
                            Severity = DeployDiagnosticSeverity.Error,
                            Code = "FED002",
                            Message = $"Tool '{tool.Name}' has execution mode '{tool.ExecutionMode}' but no endpoint configured.",
                            Component = tool.Name,
                            Suggestion = "Set the endpoint URI via the ToolBuilder fluent API."
                        });
                    }
                    break;

                case ToolExecutionMode.PlatformNative:
                    if (tool.PlatformCapability is not null &&
                        _knownCapabilities.TryGetValue(targetPlatform, out var capabilities) &&
                        !capabilities.Contains(tool.PlatformCapability))
                    {
                        diagnostics.Add(new DeployDiagnostic
                        {
                            Severity = DeployDiagnosticSeverity.Warning,
                            Code = "FED003",
                            Message = $"Tool '{tool.Name}' declares platform-native capability '{tool.PlatformCapability}' which is not recognized for platform '{targetPlatform}'. It will be passed through — the platform API will validate.",
                            Component = tool.Name,
                            Suggestion = $"Known capabilities for {targetPlatform}: {string.Join(", ", capabilities)}"
                        });
                    }
                    break;
            }
        }
    }

    private void ValidateModels(
        WorkflowManifest manifest,
        string targetPlatform,
        List<DeployDiagnostic> diagnostics,
        IReadOnlyDictionary<string, ModelDefinition>? modelCatalogue)
    {
        var mapper = _modelMappers.FirstOrDefault(m =>
            string.Equals(m.Platform, targetPlatform, StringComparison.OrdinalIgnoreCase));

        foreach (var (jobName, job) in manifest.Jobs)
        {
            if (!string.Equals(job.Type, "agent", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(job.ModelAlias))
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Error,
                    Code = "FED010",
                    Message = $"Agent job '{jobName}' has no model alias defined.",
                    Component = jobName,
                    Suggestion = "Add a 'model:' field referencing a model from the 'models:' section."
                });
                continue;
            }

            if (!manifest.Models.TryGetValue(job.ModelAlias, out var modelDef))
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Error,
                    Code = "FED011",
                    Message = $"Agent job '{jobName}' references model alias '{job.ModelAlias}' which is not defined in the manifest.",
                    Component = jobName
                });
                continue;
            }

            // A `ref` that nothing resolved must not reach the mapper. ModelDefinition's Provider
            // and Model carry defaults, so an alias declaring only `ref: devstral2` parses into a
            // valid-looking openai/gpt-5.4-mini — which is how a manifest pointing at a local Ollama
            // model silently became a paid frontier call, and validated clean while doing it.
            if (modelDef.Ref is { } reference)
            {
                if (modelCatalogue is null)
                {
                    diagnostics.Add(new DeployDiagnostic
                    {
                        Severity = DeployDiagnosticSeverity.Error,
                        Code = "FED016",
                        Message = $"Agent job '{jobName}' uses model alias '{job.ModelAlias}', which "
                            + $"references '{reference}' from a model catalogue, and no catalogue was "
                            + "supplied.",
                        Component = jobName,
                        Suggestion = "Supply the catalogue that declares it (for the CLI, "
                            + "--catalog <file>), or declare the model inline with 'provider:' and 'model:'."
                    });
                    continue;
                }

                if (!modelCatalogue.TryGetValue(reference, out var resolved))
                {
                    diagnostics.Add(new DeployDiagnostic
                    {
                        Severity = DeployDiagnosticSeverity.Error,
                        Code = "FED016",
                        Message = $"Agent job '{jobName}' uses model alias '{job.ModelAlias}', which "
                            + $"references '{reference}' — not present in the supplied catalogue.",
                        Component = jobName,
                        Suggestion = $"Add '{reference}' to the catalogue's models: section, or correct the reference."
                    });
                    continue;
                }

                // Resolved locally, for this validation only. The manifest is not rewritten: it keeps
                // meaning what its own file says.
                modelDef = resolved;
            }

            if (modelDef.Endpoint is not null)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Warning,
                    Code = "FED014",
                    Message = $"Model '{job.ModelAlias}' uses a custom endpoint ({modelDef.Endpoint}) which may not be reachable from the target platform.",
                    Component = jobName,
                    Suggestion = "Ensure the endpoint is network-reachable from the platform, or use a platform-native model."
                });
            }

            if (mapper is null)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Info,
                    Code = "FED015",
                    Message = $"No model mapper registered for platform '{targetPlatform}' — cannot verify model compatibility for '{job.ModelAlias}'.",
                    Component = jobName
                });
                continue;
            }

            var mapped = mapper.Map(modelDef);
            if (mapped is null)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Error,
                    Code = "FED013",
                    Message = $"Model '{modelDef.Provider}/{modelDef.Model}' (alias '{job.ModelAlias}') has no known mapping on platform '{targetPlatform}'.",
                    Component = jobName,
                    Suggestion = "Use a model that has a known equivalent on the target platform."
                });
            }
        }
    }
}
