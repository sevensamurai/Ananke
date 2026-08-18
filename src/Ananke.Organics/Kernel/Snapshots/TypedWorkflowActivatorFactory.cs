using Ananke.Abstractions.Agents;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Orchestration;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Division;

namespace Ananke.Organics.Kernel.Snapshots;

/// <summary>
/// Typed implementation of <see cref="IWorkflowActivatorFactory"/> that uses a
/// <see cref="WorkflowActivator{TState}"/> to produce runnable workflow loops.
/// </summary>
/// <remarks>
/// <para>
/// This class bridges the generic <see cref="WorkflowActivator{TState}"/> (which
/// knows <c>TState</c>) with the non-generic <see cref="IWorkflowActivatorFactory"/>
/// interface (which the <see cref="IWorkflowDivider"/> consumes). It is configured
/// once at setup time with all the typed callbacks (prompt builder, result mapper,
/// model factory) and then produces loops for any <see cref="WorkflowSnapshot"/>.
/// </para>
/// <para>
/// When an <see cref="OrganicHost"/> is configured, created workflows are
/// automatically joined via <see cref="OrganicWorkflowExtensions.JoinHost{TState}"/>
/// so that child cells participate in recursive complexity monitoring and can
/// themselves trigger further divisions.
/// </para>
/// <para>
/// When a <see cref="IEmpiricalMemory"/> is configured, the factory wraps it
/// with a <see cref="DomainAffinityMemory"/> decorator based on the provided
/// <see cref="MemoryProfile"/>, making the memory available to agent tools.
/// </para>
/// </remarks>
/// <typeparam name="TState">Workflow state type for activated cells.</typeparam>
public sealed class TypedWorkflowActivatorFactory<TState> : IWorkflowActivatorFactory
{
    private readonly List<ToolKit> _toolKits = [];
    private Func<ModelSnapshot, IAgentModel>? _modelFactory;
    private Func<TState, string, string>? _promptBuilder;
    private Func<TState, string, string, TState>? _resultMapper;
    private Func<TState, CancellationToken, Task<TState>>? _codeJobHandler;
    private Func<TState>? _initialStateFactory;
    private OrganicHost? _organicHost;
    private IEmpiricalMemory? _sharedMemory;

    /// <summary>
    /// Registers a <see cref="ToolKit"/> whose tools can be assigned to agent jobs.
    /// Multiple kits can be registered; tools are resolved by name across all kits.
    /// </summary>
    public TypedWorkflowActivatorFactory<TState> WithTools(ToolKit toolKit)
    {
        ArgumentNullException.ThrowIfNull(toolKit);
        _toolKits.Add(toolKit);
        return this;
    }

    /// <summary>
    /// Sets the factory that creates live <see cref="IAgentModel"/> instances from
    /// a <see cref="ModelSnapshot"/>.
    /// </summary>
    public TypedWorkflowActivatorFactory<TState> WithModelFactory(Func<ModelSnapshot, IAgentModel> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _modelFactory = factory;
        return this;
    }

    /// <summary>
    /// Sets the function that builds the user prompt from the workflow state.
    /// The second parameter is the job name, allowing different prompts per job.
    /// </summary>
    public TypedWorkflowActivatorFactory<TState> WithPromptBuilder(Func<TState, string, string> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _promptBuilder = builder;
        return this;
    }

    /// <summary>
    /// Sets the function that maps an agent job's text response back into the state.
    /// Parameters: (currentState, jobName, responseText) → newState.
    /// </summary>
    public TypedWorkflowActivatorFactory<TState> WithResultMapper(Func<TState, string, string, TState> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _resultMapper = mapper;
        return this;
    }

    /// <summary>
    /// Sets a default handler for <c>code</c>-type jobs. If not set, code jobs
    /// pass the state through unchanged.
    /// </summary>
    public TypedWorkflowActivatorFactory<TState> WithCodeJobHandler(Func<TState, CancellationToken, Task<TState>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _codeJobHandler = handler;
        return this;
    }

    /// <summary>
    /// Sets the factory that produces the initial state for each workflow execution.
    /// Called once per loop iteration to create a fresh initial state.
    /// </summary>
    public TypedWorkflowActivatorFactory<TState> WithInitialStateFactory(Func<TState> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _initialStateFactory = factory;
        return this;
    }

    /// <summary>
    /// When set, created workflows are automatically joined to this host for
    /// recursive complexity monitoring. Children spawned via division will
    /// themselves be observed and can trigger further divisions.
    /// </summary>
    public TypedWorkflowActivatorFactory<TState> WithOrganicHost(OrganicHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _organicHost = host;
        return this;
    }

    /// <summary>
    /// Sets the shared empirical memory. When a <see cref="MemoryProfile"/> is
    /// provided to <see cref="CreateLoop"/>, this memory is wrapped with a
    /// <see cref="DomainAffinityMemory"/> decorator for the child cell.
    /// </summary>
    public TypedWorkflowActivatorFactory<TState> WithSharedMemory(IEmpiricalMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _sharedMemory = memory;
        return this;
    }

    /// <summary>
    /// Creates a workflow loop from a snapshot and optional memory profile.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="WithInitialStateFactory"/> was not called.
    /// </exception>
    public Func<CancellationToken, Task> CreateLoop(
        WorkflowSnapshot snapshot,
        MemoryProfile? memoryProfile = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_initialStateFactory is null)
            throw new InvalidOperationException(
                "No initial state factory configured. Call WithInitialStateFactory() before creating loops.");

        // Build the activator with the configured callbacks
        var activator = new WorkflowActivator<TState>();

        foreach (var toolKit in _toolKits)
            activator.WithTools(toolKit);

        // If we have shared memory and a memory profile, create a domain-affine toolkit
        // that wraps the memory for this child's domain
        if (_sharedMemory is not null && memoryProfile is not null)
        {
            List<string> combinedDomains = [.. memoryProfile.Domains, .. memoryProfile.LineageTags];
            var domainMemory = new DomainAffinityMemory(
                _sharedMemory,
                combinedDomains.Distinct().ToList());

            var memoryToolKit = EmpiricalMemoryTools.Create(domainMemory, snapshot.Name);
            activator.WithTools(memoryToolKit);
        }

        if (_modelFactory is not null)
            activator.WithModelFactory(_modelFactory);
        if (_promptBuilder is not null)
            activator.WithPromptBuilder(_promptBuilder);
        if (_resultMapper is not null)
            activator.WithResultMapper(_resultMapper);
        if (_codeJobHandler is not null)
            activator.WithCodeJobHandler(_codeJobHandler);

        // Activate the snapshot into a workflow
        var workflow = activator.Hydrate(snapshot);

        // Build tool kit for organic host registration (structural profile)
        ToolKit? registrationToolKit = null;
        if (_toolKits.Count > 0)
        {
            registrationToolKit = new ToolKit($"{snapshot.Name}-tools");
            var allTools = _toolKits.SelectMany(k => k.Tools);
            foreach (var (name, tool) in allTools)
            {
                if (snapshot.Tools.Contains(name))
                    registrationToolKit.AddTool(tool);
            }
        }

        // Capture the initial state factory and organic wrapper for the loop closure
        var stateFactory = _initialStateFactory;
        var host = _organicHost;

        if (host is not null)
        {
            var organic = workflow.JoinHost(host, registrationToolKit);
            return async ct =>
            {
                while (!ct.IsCancellationRequested)
                {
                    var state = stateFactory();
                    await organic.RunAsync(state, ct);
                }
            };
        }

        return async ct =>
        {
            while (!ct.IsCancellationRequested)
            {
                var state = stateFactory();
                await workflow.RunAsync(state, ct);
            }
        };
    }
}
