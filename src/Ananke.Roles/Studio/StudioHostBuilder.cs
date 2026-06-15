using Ananke.Design;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Healing;
using Ananke.Organics.Kernel;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Sensing;
using Ananke.Roles.Roles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Ananke.Roles.Studio;

/// <summary>
/// Fluent builder for wiring studio roles, workflows, and supporting services into a service collection.
/// </summary>
public sealed class StudioHostBuilder(StudioOptions? options = null)
{
    private readonly Dictionary<string, AgentRole> _roles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _workflowPaths = new(StringComparer.OrdinalIgnoreCase);
    private StudioOptions _options = options ?? new StudioOptions();
    private bool _divisionDisabled;
    private Type? _approvalGateType;

    /// <summary>
    /// Adds a role definition to the studio catalog.
    /// </summary>
    public StudioHostBuilder AddRole(AgentRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (!_roles.TryAdd(role.Name, role))
            throw new InvalidOperationException($"A role named '{role.Name}' is already registered.");

        return this;
    }

    /// <summary>
    /// Registers a workflow manifest file for later loading.
    /// </summary>
    public StudioHostBuilder UseWorkflow(string name, string yamlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlPath);

        _workflowPaths[name] = yamlPath;
        return this;
    }

    /// <summary>
    /// Replaces the studio options used during registration.
    /// </summary>
    public StudioHostBuilder WithOptions(StudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        return this;
    }

    /// <summary>
    /// Sets the <see cref="IDivisionApprovalGate"/> implementation that will be registered
    /// in the service collection. Must be called before <see cref="Build"/>.
    /// </summary>
    /// <typeparam name="TGate">
    /// Concrete <see cref="IDivisionApprovalGate"/> type. Use
    /// <see cref="AutoApprovalGate"/> only in supervised local workflows where
    /// automatic approval is intentional.
    /// </typeparam>
    public StudioHostBuilder UseApprovalGate<TGate>() where TGate : class, IDivisionApprovalGate
    {
        _approvalGateType = typeof(TGate);
        return this;
    }

    /// <summary>
    /// Disables division-specific policy registration for the built service collection.
    /// </summary>
    public StudioHostBuilder DisableDivision()
    {
        _divisionDisabled = true;
        return this;
    }

    /// <summary>
    /// Registers the configured studio services.
    /// </summary>
    public IServiceCollection Build(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (_roles.Count == 0 && _workflowPaths.Count == 0)
            throw new InvalidOperationException("At least one role or workflow must be registered before building the studio host.");

        if (_approvalGateType is null)
            throw new InvalidOperationException(
                "An IDivisionApprovalGate must be configured. " +
                "Call UseApprovalGate<TGate>() before Build(). " +
                "Use AutoApprovalGate only in supervised local workflows where automatic approval is intentional.");

        var roleCatalog = new AgentRoleCatalog();
        foreach (var role in _roles.Values)
            roleCatalog.Add(role);

        var workflowRegistry = new StudioWorkflowRegistry(new Dictionary<string, string>(_workflowPaths, StringComparer.OrdinalIgnoreCase));
        var defaultWorkflowName = ResolveDefaultWorkflowName();
        var keywordMap = BuildKeywordMap();

        services.AddSingleton(_options);
        services.AddSingleton<IAgentRoleCatalog>(roleCatalog);
        services.AddSingleton(roleCatalog);
        services.AddSingleton<RoleManifestFactory>(_ => new RoleManifestFactory(_options.ModelAliasMap));
        services.AddSingleton(workflowRegistry);

        services.TryAddSingleton<ICapabilityMap>(sp =>
        {
            sp.GetService<ILoggerFactory>()
              ?.CreateLogger("Ananke.StudioHost")
              .LogWarning(
                  "[Ananke] ICapabilityMap is backed by InMemoryCapabilityMap — " +
                  "colony mesh state will be lost on restart. " +
                  "Register a persistent ICapabilityMap before StudioHostBuilder.Build to suppress this warning.");
            return new InMemoryCapabilityMap();
        });
        services.TryAddSingleton<IMeshAggregator>(sp =>
        {
            sp.GetService<ILoggerFactory>()
              ?.CreateLogger("Ananke.StudioHost")
              .LogWarning(
                  "[Ananke] IMeshAggregator is backed by InMemoryMeshAggregator — " +
                  "metabolic signals will be lost on restart. " +
                  "Register a persistent IMeshAggregator before StudioHostBuilder.Build to suppress this warning.");
            return new InMemoryMeshAggregator();
        });
        services.TryAddSingleton<ILineageStore>(sp =>
        {
            sp.GetService<ILoggerFactory>()
              ?.CreateLogger("Ananke.StudioHost")
              .LogWarning(
                  "[Ananke] ILineageStore is backed by InMemoryLineageStore — " +
                  "cell lineage records will be lost on restart. " +
                  "Register a persistent ILineageStore before StudioHostBuilder.Build to suppress this warning.");
            return new InMemoryLineageStore();
        });
        services.AddSingleton<IHealthMonitor, WorkflowExecutionMonitor>();
        services.AddSingleton(typeof(IDivisionApprovalGate), _approvalGateType!);
        services.AddSingleton<IWorkflowHost, InProcessWorkflowHost>();
        services.AddSingleton<KeywordRequestRouter>();
        services.AddSingleton<StudioRouter>(sp => new StudioRouter(
            sp.GetRequiredService<KeywordRequestRouter>(),
            keywordMap,
            defaultWorkflowName));
        services.AddSingleton<IRequestRouter>(sp => sp.GetRequiredService<StudioRouter>());

        if (!_divisionDisabled)
            services.AddSingleton<IDivisionPolicy, ThresholdDivisionPolicy>();

        services.AddSingleton(sp => new OrganicGrowthOptions
        {
            Policy = _divisionDisabled
                ? DisabledDivisionPolicy.Instance
                : sp.GetRequiredService<IDivisionPolicy>(),
            ApprovalGate = sp.GetRequiredService<IDivisionApprovalGate>(),
            Monitor = sp.GetRequiredService<IHealthMonitor>(),
            MeshAggregator = sp.GetRequiredService<IMeshAggregator>(),
            Lineage = sp.GetRequiredService<ILineageStore>(),
            ManifestFactory = workflowName => ResolveManifest(
                workflowName,
                workflowRegistry,
                roleCatalog,
                sp.GetRequiredService<RoleManifestFactory>())
        });

        services.AddSingleton<OrganicHost>(sp => new OrganicHost(
            sp.GetRequiredService<IWorkflowHost>(),
            sp.GetRequiredService<ICapabilityMap>(),
            sp.GetRequiredService<OrganicGrowthOptions>()));

        return services;
    }

    private static WorkflowManifest ResolveManifest(
        string workflowName,
        StudioWorkflowRegistry workflows,
        AgentRoleCatalog roles,
        RoleManifestFactory factory)
    {
        if (workflows.Paths.TryGetValue(workflowName, out var yamlPath) && File.Exists(yamlPath))
            return WorkflowManifest.Load(yamlPath);

        if (roles.TryGet(workflowName, out var role))
            return factory.CreateManifest(role);

        throw new KeyNotFoundException($"No workflow or role named '{workflowName}' is registered in the studio host.");
    }

    private Dictionary<string, string> BuildKeywordMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in _roles.Values)
        {
            foreach (var tag in role.DomainTags)
                map.TryAdd(tag, role.Name);
        }

        return map;
    }

    private string ResolveDefaultWorkflowName()
    {
        if (_workflowPaths.Count > 0)
            return _workflowPaths.Keys.First();

        return _roles.Keys.First();
    }

    private sealed record StudioWorkflowRegistry(IReadOnlyDictionary<string, string> Paths);

    private sealed class DisabledDivisionPolicy : IDivisionPolicy
    {
        public static DisabledDivisionPolicy Instance { get; } = new();

        public Task<DivisionPlan?> EvaluateAsync(
            ComplexitySnapshot snapshot,
            WorkflowManifest manifest,
            CancellationToken ct = default) =>
            Task.FromResult<DivisionPlan?>(null);
    }
}
