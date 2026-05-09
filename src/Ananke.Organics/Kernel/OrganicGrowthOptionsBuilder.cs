using Ananke.Orchestration.Workflows;
using Ananke.Design;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Organics.Division;
using Ananke.Organics.Healing;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Sensing;

namespace Ananke.Organics.Kernel;

/// <summary>
/// Fluent builder for <see cref="OrganicGrowthOptions"/>. Uses a state machine
/// to enforce valid combinations at compile time: <see cref="WithDivider"/>
/// returns <see cref="OrganicGrowthOptionsWithDivider"/>, which requires
/// <see cref="OrganicGrowthOptionsWithDivider.WithSharedMemory"/> before Build() is available.
/// </summary>
/// <remarks>
/// Obtain an instance via <see cref="OrganicGrowthOptions.CreateBuilder"/>.
/// </remarks>
public sealed class OrganicGrowthOptionsBuilder
{
    private IDivisionPolicy? _policy;
    private IDivisionApprovalGate _gate = new AutoApprovalGate();
    private IHealthMonitor _monitor = new WorkflowExecutionMonitor();
    private int _evaluationInterval = 10;
    private IDivisionOutcomeTracker? _outcomeTracker;
    private Func<string, WorkflowManifest>? _manifestFactory;
    private IRemoteCellSource? _remoteCellSource;
    private TimeSpan _remotePollingInterval = TimeSpan.FromSeconds(60);
    private IDomainRouter? _domainRouter;
    private int _stabilizationWindowMs = 5000;
    private int _divisionShutdownTimeoutMs = 30_000;
    private IDivisionTransition? _transition;

    internal OrganicGrowthOptionsBuilder() { }

    /// <summary>Sets the division policy (required).</summary>
    public OrganicGrowthOptionsBuilder WithPolicy(IDivisionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
        return this;
    }

    /// <summary>Overrides the approval gate. Default: <see cref="AutoApprovalGate"/>.</summary>
    public OrganicGrowthOptionsBuilder WithGate(IDivisionApprovalGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        _gate = gate;
        return this;
    }

    /// <summary>Overrides the health monitor. Default: <see cref="WorkflowExecutionMonitor"/>.</summary>
    public OrganicGrowthOptionsBuilder WithMonitor(IHealthMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        _monitor = monitor;
        return this;
    }

    /// <summary>How often to evaluate complexity (in executions). Default: 10.</summary>
    public OrganicGrowthOptionsBuilder WithEvaluationInterval(int interval)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(interval);
        _evaluationInterval = interval;
        return this;
    }

    /// <summary>Attaches an outcome tracker for the fitness feedback loop.</summary>
    public OrganicGrowthOptionsBuilder WithOutcomeTracker(IDivisionOutcomeTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        _outcomeTracker = tracker;
        return this;
    }

    /// <summary>Provides manifest lookup for policy evaluation.</summary>
    public OrganicGrowthOptionsBuilder WithManifestFactory(Func<string, WorkflowManifest> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _manifestFactory = factory;
        return this;
    }

    /// <summary>Enables remote cell polling.</summary>
    public OrganicGrowthOptionsBuilder WithRemoteCellSource(IRemoteCellSource source, TimeSpan? pollingInterval = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _remoteCellSource = source;
        if (pollingInterval is { } interval)
            _remotePollingInterval = interval;
        return this;
    }

    /// <summary>Attaches a domain router updated after successful divisions.</summary>
    public OrganicGrowthOptionsBuilder WithDomainRouter(IDomainRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        _domainRouter = router;
        return this;
    }

    /// <summary>Sets a custom division transition strategy.</summary>
    public OrganicGrowthOptionsBuilder WithTransition(IDivisionTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        _transition = transition;
        return this;
    }

    /// <summary>
    /// Adds a divider and moves to <see cref="OrganicGrowthOptionsWithDivider"/>,
    /// which requires <see cref="OrganicGrowthOptionsWithDivider.WithSharedMemory"/>
    /// before <c>Build()</c> is available.
    /// </summary>
    public OrganicGrowthOptionsWithDivider WithDivider(IWorkflowDivider divider)
    {
        ArgumentNullException.ThrowIfNull(divider);
        return new OrganicGrowthOptionsWithDivider(divider, this);
    }

    /// <summary>
    /// Builds <see cref="OrganicGrowthOptions"/> without a divider (monitoring + gating only).
    /// </summary>
    public OrganicGrowthOptions Build()
    {
        if (_policy is null)
            throw new InvalidOperationException(
                $"'{nameof(WithPolicy)}' must be called before '{nameof(Build)}'.");

        var options = new OrganicGrowthOptions
        {
            Policy = _policy,
            ApprovalGate = _gate,
            Monitor = _monitor,
            EvaluationInterval = _evaluationInterval,
            OutcomeTracker = _outcomeTracker,
            ManifestFactory = _manifestFactory,
            RemoteCellSource = _remoteCellSource,
            RemotePollingInterval = _remotePollingInterval,
            DomainRouter = _domainRouter,
            StabilizationWindowMs = _stabilizationWindowMs,
            DivisionShutdownTimeoutMs = _divisionShutdownTimeoutMs,
            Transition = _transition
        };

        options.Validate();
        return options;
    }

    internal OrganicGrowthOptionsBuilder SetStabilizationWindow(int ms)
    {
        _stabilizationWindowMs = ms;
        return this;
    }

    internal OrganicGrowthOptionsBuilder SetShutdownTimeout(int ms)
    {
        _divisionShutdownTimeoutMs = ms;
        return this;
    }

    internal OrganicGrowthOptions BuildWithDivider(IWorkflowDivider divider, IEmpiricalMemory sharedMemory)
    {
        if (_policy is null)
            throw new InvalidOperationException(
                $"'{nameof(WithPolicy)}' must be called before '{nameof(Build)}'.");

        var options = new OrganicGrowthOptions
        {
            Policy = _policy,
            ApprovalGate = _gate,
            Monitor = _monitor,
            EvaluationInterval = _evaluationInterval,
            OutcomeTracker = _outcomeTracker,
            ManifestFactory = _manifestFactory,
            RemoteCellSource = _remoteCellSource,
            RemotePollingInterval = _remotePollingInterval,
            DomainRouter = _domainRouter,
            StabilizationWindowMs = _stabilizationWindowMs,
            DivisionShutdownTimeoutMs = _divisionShutdownTimeoutMs,
            Transition = _transition,
            Divider = divider,
            SharedMemory = sharedMemory
        };

        options.Validate();
        return options;
    }
}

/// <summary>
/// Intermediate builder state after <see cref="OrganicGrowthOptionsBuilder.WithDivider"/>
/// is called. Build() is only reachable after
/// <see cref="WithSharedMemory"/> provides the required memory.
/// </summary>
public sealed class OrganicGrowthOptionsWithDivider
{
    private readonly IWorkflowDivider _divider;
    private readonly OrganicGrowthOptionsBuilder _parent;

    internal OrganicGrowthOptionsWithDivider(IWorkflowDivider divider, OrganicGrowthOptionsBuilder parent)
    {
        _divider = divider;
        _parent = parent;
    }

    /// <summary>
    /// Provides shared empirical memory for RNA seeding during division.
    /// Required to unlock Build().
    /// </summary>
    public OrganicGrowthOptionsWithDividerAndMemory WithSharedMemory(IEmpiricalMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return new OrganicGrowthOptionsWithDividerAndMemory(_divider, memory, _parent);
    }
}

/// <summary>
/// Terminal builder state: divider and shared memory are both set.
/// <see cref="Build"/> is now available.
/// </summary>
public sealed class OrganicGrowthOptionsWithDividerAndMemory
{
    private readonly IWorkflowDivider _divider;
    private readonly IEmpiricalMemory _memory;
    private readonly OrganicGrowthOptionsBuilder _parent;

    internal OrganicGrowthOptionsWithDividerAndMemory(
        IWorkflowDivider divider,
        IEmpiricalMemory memory,
        OrganicGrowthOptionsBuilder parent)
    {
        _divider = divider;
        _memory = memory;
        _parent = parent;
    }

    /// <summary>Builds the final <see cref="OrganicGrowthOptions"/>.</summary>
    public OrganicGrowthOptions Build() => _parent.BuildWithDivider(_divider, _memory);
}
