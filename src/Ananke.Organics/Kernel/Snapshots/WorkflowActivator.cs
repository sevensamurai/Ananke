using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;

namespace Ananke.Organics.Kernel.Snapshots;

/// <summary>
/// Hydrates a <see cref="WorkflowSnapshot"/> into a runnable <see cref="Workflow{TState}"/>
/// that can be spawned into a kernel. This is the bridge between the declarative
/// snapshot format and live cell execution.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tool resolution:</b> The snapshot stores tool <em>names</em>, not implementations.
/// Callers must supply a <see cref="ToolKit"/> registry that contains the actual tool
/// implementations. Tools are matched by name; missing tools cause a clear error.
/// </para>
/// <para>
/// <b>Model resolution:</b> The snapshot stores model aliases (provider + model name).
/// Callers supply a <c>Func&lt;ModelSnapshot, IAgentModel&gt;</c> factory that creates
/// live model instances. This keeps the hydrator decoupled from any specific provider
/// (OpenAI, Anthropic, Ollama, etc.).
/// </para>
/// <para>
/// <b>Usage pattern — dynamic workflow from snapshot:</b>
/// </para>
/// <code>
/// var hydrator = new WorkflowActivator&lt;ChatState&gt;()
///     .WithTools(myToolKit)
///     .WithModelFactory(snap => CreateModel(snap.Provider, snap.Model))
///     .WithPromptBuilder((state, job) => state.UserMessage)
///     .WithResultMapper((state, job, text) => state with { Response = text });
///
/// var workflow = hydrator.Hydrate(cellSnapshot);
/// mesh.Start(cellSnapshot.Name, async ct => await workflow.RunAsync(initialState, ct));
/// </code>
/// </remarks>
/// <typeparam name="TState">Workflow state type for the hydrated cell.</typeparam>
public sealed class WorkflowActivator<TState>
{
    private readonly Dictionary<string, ToolKit> _toolKits = [];
    private Func<ModelSnapshot, IAgentModel>? _modelFactory;
    private Func<TState, string, string>? _promptBuilder;
    private Func<TState, string, string, TState>? _resultMapper;
    private Func<TState, CancellationToken, Task<TState>>? _codeJobHandler;

    /// <summary>
    /// Registers a <see cref="ToolKit"/> whose tools can be assigned to agent jobs.
    /// Multiple kits can be registered; tools are resolved by name across all kits.
    /// </summary>
    public WorkflowActivator<TState> WithTools(ToolKit toolKit)
    {
        ArgumentNullException.ThrowIfNull(toolKit);
        _toolKits[toolKit.Name] = toolKit;
        return this;
    }

    /// <summary>
    /// Sets the factory that creates live <see cref="IAgentModel"/> instances from
    /// a <see cref="ModelSnapshot"/>. Called once per model alias in the cell snapshot.
    /// </summary>
    public WorkflowActivator<TState> WithModelFactory(Func<ModelSnapshot, IAgentModel> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _modelFactory = factory;
        return this;
    }

    /// <summary>
    /// Sets the function that builds the user prompt from the workflow state.
    /// The second parameter is the job name, allowing different prompts per job.
    /// </summary>
    public WorkflowActivator<TState> WithPromptBuilder(Func<TState, string, string> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _promptBuilder = builder;
        return this;
    }

    /// <summary>
    /// Sets the function that maps an agent job's text response back into the state.
    /// Parameters: (currentState, jobName, responseText) → newState.
    /// </summary>
    public WorkflowActivator<TState> WithResultMapper(Func<TState, string, string, TState> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _resultMapper = mapper;
        return this;
    }

    /// <summary>
    /// Sets a default handler for <c>code</c>-type jobs. If not set, code jobs
    /// pass the state through unchanged.
    /// </summary>
    public WorkflowActivator<TState> WithCodeJobHandler(Func<TState, CancellationToken, Task<TState>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _codeJobHandler = handler;
        return this;
    }

    /// <summary>
    /// Hydrates a <see cref="WorkflowSnapshot"/> into a runnable <see cref="Workflow{TState}"/>.
    /// </summary>
    /// <param name="cell">The cell snapshot to hydrate.</param>
    /// <returns>A fully configured, ready-to-run workflow.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if required configuration (model factory, prompt builder, result mapper) is missing,
    /// or if the snapshot references tools/models that cannot be resolved.
    /// </exception>
    public Workflow<TState> Hydrate(WorkflowSnapshot cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        // Resolve models
        var models = new Dictionary<string, IAgentModel>();
        foreach (var (alias, modelSnap) in cell.Models)
        {
            if (_modelFactory is null)
                throw new InvalidOperationException(
                    $"Cell '{cell.Name}' declares model '{alias}' but no model factory was configured. " +
                    "Call WithModelFactory() before hydrating.");

            models[alias] = _modelFactory(modelSnap);
        }

        // Build a merged ToolKit containing only the tools this cell needs
        var cellToolKit = new ToolKit($"{cell.Name}-tools");
        var allAvailableTools = _toolKits.Values
            .SelectMany(k => k.Tools)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        foreach (var toolName in cell.Tools)
        {
            if (!allAvailableTools.TryGetValue(toolName, out var toolDef))
                throw new InvalidOperationException(
                    $"Cell '{cell.Name}' references tool '{toolName}' but it was not found " +
                    $"in any registered ToolKit. Available: [{string.Join(", ", allAvailableTools.Keys)}]");

            cellToolKit.AddTool(toolDef);
        }

        // Parse topology and bind jobs
        var scaffold = WorkflowScaffold.Parse<TState>(cell.Name, cell.Connections);

        foreach (var (jobName, jobSnap) in cell.Jobs)
        {
            if (!scaffold.JobNames.Contains(jobName))
                continue; // Job declared but not referenced in connections — skip silently

            if (jobSnap.Type.Equals("agent", StringComparison.OrdinalIgnoreCase))
            {
                BindAgentJob(scaffold, jobName, jobSnap, models, cellToolKit, cell);
            }
            else
            {
                // Code job — use custom handler or pass-through
                var handler = _codeJobHandler ?? ((state, _) => Task.FromResult(state));
                scaffold.Bind(jobName, handler);
            }
        }

        return scaffold.Build();
    }

    private void BindAgentJob(
        WorkflowScaffold<TState> scaffold,
        string jobName,
        JobSnapshot jobSnap,
        Dictionary<string, IAgentModel> models,
        ToolKit cellToolKit,
        WorkflowSnapshot cell)
    {
        if (_promptBuilder is null)
            throw new InvalidOperationException(
                $"Cell '{cell.Name}' has agent job '{jobName}' but no prompt builder was configured. " +
                "Call WithPromptBuilder() before hydrating.");

        if (_resultMapper is null)
            throw new InvalidOperationException(
                $"Cell '{cell.Name}' has agent job '{jobName}' but no result mapper was configured. " +
                "Call WithResultMapper() before hydrating.");

        var modelAlias = jobSnap.ModelAlias ?? "default";
        if (!models.TryGetValue(modelAlias, out var model))
            throw new InvalidOperationException(
                $"Agent job '{jobName}' in cell '{cell.Name}' references model alias '{modelAlias}' " +
                $"but it was not resolved. Available: [{string.Join(", ", models.Keys)}]");

        var builder = AgentJobFactory.Create<TState>(jobName, model)
            .WithPrompt(state => _promptBuilder(state, jobName))
            .MapResult((state, text) => _resultMapper(state, jobName, text))
            .WithMaxToolRounds(jobSnap.MaxToolRounds);

        if (jobSnap.SystemPrompt is not null)
            builder.WithSystemPrompt(jobSnap.SystemPrompt);

        if (cellToolKit.Tools.Count > 0)
            builder.WithTools(cellToolKit);

        scaffold.Bind(jobName, builder.Build());
    }
}
