using Ananke.Organics.Division;
using Ananke.Orchestration.Tools;

namespace Ananke.Organics.Kernel.Snapshots;

/// <summary>
/// Fluent builder for <see cref="WorkflowSnapshot"/>. Provides sensible defaults
/// for the common pattern of an agent job feeding into a code job, reducing
/// the verbose record initialization required for snapshots.
/// </summary>
/// <remarks>
/// <para>
/// <b>Minimal usage — single agent cell:</b>
/// </para>
/// <code>
/// var snap = new WorkflowSnapshotBuilder("bookstore-catalog", "catalog")
///     .Tools(catalogToolKit)
///     .Build();
/// </code>
/// <para>
/// This produces a cell with a <c>handle-request</c> agent job using a
/// <c>default</c> model alias (<c>openai/gpt-4o-mini</c>), a <c>respond</c>
/// code job, and a <c>handle-request -&gt; respond</c> connection.
/// </para>
/// <para>
/// <b>Division child with lineage and memory:</b>
/// </para>
/// <code>
/// var snap = new WorkflowSnapshotBuilder("bookstore-orders", "orders")
///     .Tools(orderToolKit)
///     .SplitFrom("bookstore-general")
///     .Memory(["orders", "general"], lineageTags: ["bookstore"])
///     .Build();
/// </code>
/// </remarks>
public sealed class WorkflowSnapshotBuilder
{
    private readonly string _name;
    private readonly string _domain;
    private string? _splitFrom;
    private IReadOnlyList<string> _tools = [];
    private IReadOnlyList<string> _connections = ["handle-request -> respond"];
    private Dictionary<string, JobSnapshot> _jobs = new()
    {
        ["handle-request"] = new() { Type = "agent", ModelAlias = "default" },
        ["respond"] = new() { Type = "code" }
    };
    private Dictionary<string, ModelSnapshot> _models = new()
    {
        ["default"] = new() { Provider = "openai", Model = "gpt-4o-mini" }
    };
    private MemoryProfile? _memoryProfile;

    /// <summary>
    /// Creates a new builder with required cell name and domain.
    /// </summary>
    /// <param name="name">Cell name (unique within the kernel).</param>
    /// <param name="domain">Primary domain this cell serves.</param>
    public WorkflowSnapshotBuilder(string name, string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        _name = name;
        _domain = domain;
    }

    /// <summary>Sets the tool names from a <see cref="ToolKit"/>.</summary>
    public WorkflowSnapshotBuilder Tools(ToolKit toolKit)
    {
        ArgumentNullException.ThrowIfNull(toolKit);
        _tools = toolKit.Tools.Keys.ToList();
        return this;
    }

    /// <summary>Sets the tool names from an explicit list.</summary>
    public WorkflowSnapshotBuilder Tools(IReadOnlyList<string> toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        _tools = toolNames;
        return this;
    }

    /// <summary>Sets the parent cell this was divided from.</summary>
    public WorkflowSnapshotBuilder SplitFrom(string parentWorkflowName)
    {
        _splitFrom = parentWorkflowName;
        return this;
    }

    /// <summary>Sets the memory profile for domain-affine recall.</summary>
    public WorkflowSnapshotBuilder Memory(
        IReadOnlyList<string> domains,
        IReadOnlyList<string>? lineageTags = null)
    {
        ArgumentNullException.ThrowIfNull(domains);
        _memoryProfile = new MemoryProfile
        {
            Domains = domains,
            LineageTags = lineageTags ?? []
        };
        return this;
    }

    /// <summary>
    /// Replaces the default model alias. Call before <see cref="AgentJob"/>
    /// if the alias name differs from <c>"default"</c>.
    /// </summary>
    public WorkflowSnapshotBuilder Model(string alias, string provider, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        _models[alias] = new ModelSnapshot { Provider = provider, Model = model };
        return this;
    }

    /// <summary>Adds or replaces an agent job with the given model alias.</summary>
    public WorkflowSnapshotBuilder AgentJob(
        string jobName,
        string modelAlias = "default",
        string? systemPrompt = null,
        int maxToolRounds = 3)
    {
        _jobs[jobName] = new JobSnapshot
        {
            Type = "agent",
            ModelAlias = modelAlias,
            SystemPrompt = systemPrompt,
            MaxToolRounds = maxToolRounds
        };
        return this;
    }

    /// <summary>Adds or replaces a code job.</summary>
    public WorkflowSnapshotBuilder CodeJob(string jobName)
    {
        _jobs[jobName] = new JobSnapshot { Type = "code" };
        return this;
    }

    /// <summary>Replaces the default connection topology.</summary>
    public WorkflowSnapshotBuilder Connections(params string[] dslLines)
    {
        _connections = dslLines;
        return this;
    }

    /// <summary>Builds the immutable <see cref="WorkflowSnapshot"/>.</summary>
    public WorkflowSnapshot Build() => new()
    {
        Name = _name,
        Domain = _domain,
        SplitFrom = _splitFrom,
        Tools = _tools,
        Connections = _connections,
        Jobs = _jobs,
        Models = _models,
        MemoryProfile = _memoryProfile
    };
}
