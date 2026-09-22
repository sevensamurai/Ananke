using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;

namespace Ananke.Design;

/// <summary>
/// A plan declared as data: the decomposition, the contracts, and every re-ruling.
/// </summary>
/// <remarks>
/// <para>
/// The declarative complement to building a <see cref="PlanTree"/> in code, and the same idea as
/// <see cref="WorkflowManifest"/> one tier up. A plan that can only be written as C# can only be
/// written by whoever can build the project, which is the wrong audience for a document that says
/// what work is being done and what would count as having done it.
/// </para>
/// <para>
/// <b>It describes more than the plan's first version.</b> A format that could state only the
/// original decomposition would describe the half that matters least — the interesting half is what
/// changed and why. <c>revisions:</c> carries re-rulings in order, each naming the node, the contract
/// that replaced its own, the reason, and the children that replace its decomposition.
/// </para>
/// <para>
/// <b>Deliberately not a language.</b> There are no conditions, no expressions, no variables, no
/// includes, no anchors and no templating, and unknown keys are rejected rather than ignored. A plan
/// format that grows control flow has stopped being a description of work and become a program that
/// computes one — at which point the document nobody could read is back, in a worse notation.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// plan: offline-export
///
/// root:
///   goal: Ship offline export of reports.
///   criteria:
///     - An exported file is byte-identical to the online report.
///   constraints:
///     - No new external service dependencies.
///   children:
///     - id: discovery
///       goal: Understand what already exists before designing anything.
///
/// revisions:
///   - node: delivery
///     reason: &gt;
///       The largest tenant's report is 4 GB. Buffering cannot meet the root's criterion at
///       any buffer size, so buffering is not the work any more.
///     goal: Build offline export by streaming.
///     children:
///       - id: build-streaming-writer
///         goal: Stream rendered pages straight to storage.
/// </code>
/// </example>
public sealed record PlanManifest
{
    /// <summary>The plan's identifier, from the <c>plan:</c> field. Also the root node's id.</summary>
    public required string Plan { get; init; }

    /// <summary>The root of the decomposition, from the <c>root:</c> section.</summary>
    public required PlanNodeManifest Root { get; init; }

    /// <summary>Re-rulings, in the order they were made.</summary>
    public IReadOnlyList<PlanRevisionManifest> Revisions { get; init; } = [];

    /// <summary>Loads a plan manifest from a file.</summary>
    public static PlanManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllLines(path));
    }

    /// <summary>Parses a plan manifest from its lines.</summary>
    public static PlanManifest Parse(string[] lines) => PlanManifestParser.Parse(lines);

    /// <summary>
    /// Builds the plan as first declared — version one, before any revision.
    /// </summary>
    /// <remarks>
    /// Describing a plan is not the same as changing one, so a manifest of any depth is one version
    /// rather than one version per level. Use <see cref="ToCurrentTree"/> for the plan as it now
    /// stands, or apply revisions yourself when the run reaches the point that justified them.
    /// </remarks>
    public PlanTree ToTree(TimeProvider? timeProvider = null) => PlanTree.Create(
        Plan,
        Root.ToContract(),
        Root.Children.Select(c => c.ToSpec()),
        timeProvider);

    /// <summary>
    /// Builds the plan as it now stands: version one with every revision applied, in order.
    /// </summary>
    public PlanTree ToCurrentTree(TimeProvider? timeProvider = null)
    {
        var tree = ToTree(timeProvider);

        foreach (var revision in Revisions)
            tree = revision.ApplyTo(tree, timeProvider);

        return tree;
    }
}

/// <summary>One work item in a declared decomposition.</summary>
public sealed record PlanNodeManifest
{
    /// <summary>
    /// Its identifier, unique within the plan. <see langword="null"/> at the root, whose id is the
    /// plan's own.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>What this item is for, from <c>goal:</c>.</summary>
    public required string Goal { get; init; }

    /// <summary>Acceptance criteria, from <c>criteria:</c>. These gate.</summary>
    public IReadOnlyList<string> Criteria { get; init; } = [];

    /// <summary>Quality criteria, from <c>quality:</c>. These rank, and never gate.</summary>
    public IReadOnlyList<string> Quality { get; init; } = [];

    /// <summary>What holds for the whole of it, from <c>constraints:</c>.</summary>
    public IReadOnlyList<string> Constraints { get; init; } = [];

    /// <summary>Its decomposition, in execution order, from <c>children:</c>.</summary>
    public IReadOnlyList<PlanNodeManifest> Children { get; init; } = [];

    /// <summary>The contract this declares.</summary>
    public AgentContract ToContract() => new()
    {
        Goal = Goal,
        AcceptanceCriteria = Criteria,
        QualityCriteria = Quality,
        Constraints = Constraints
    };

    internal PlanTree.PlanNodeSpec ToSpec() => new(
        Id ?? throw new InvalidOperationException("A child node needs an id."),
        ToContract(),
        [.. Children.Select(c => c.ToSpec())]);
}

/// <summary>A re-ruling: what changed about a node's contract, and why.</summary>
public sealed record PlanRevisionManifest
{
    /// <summary>The node re-ruled, from <c>node:</c>.</summary>
    public required string Node { get; init; }

    /// <summary>Why the previous version stopped being right, from <c>reason:</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>The goal that replaced its own.</summary>
    public required string Goal { get; init; }

    /// <summary>Acceptance criteria of the replacement contract.</summary>
    public IReadOnlyList<string> Criteria { get; init; } = [];

    /// <summary>Quality criteria of the replacement contract.</summary>
    public IReadOnlyList<string> Quality { get; init; } = [];

    /// <summary>Constraints of the replacement contract.</summary>
    public IReadOnlyList<string> Constraints { get; init; } = [];

    /// <summary>
    /// The decomposition that replaces the node's own. Children not listed are dropped from the new
    /// version and stay readable in every version before it.
    /// </summary>
    public IReadOnlyList<PlanNodeManifest> Children { get; init; } = [];

    /// <summary>The replacement contract.</summary>
    public AgentContract ToContract() => new()
    {
        Goal = Goal,
        AcceptanceCriteria = Criteria,
        QualityCriteria = Quality,
        Constraints = Constraints
    };

    /// <summary>Applies this re-ruling to <paramref name="tree"/>, minting a version.</summary>
    public PlanTree ApplyTo(PlanTree tree, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(tree);

        return tree.Rerule(
            Node,
            ToContract(),
            Reason,
            Children.Count == 0
                ? null
                : Children.Select(c => (
                    c.Id ?? throw new InvalidOperationException("A replacement child needs an id."),
                    c.ToContract())),
            timeProvider);
    }

    /// <summary>
    /// Applies this revision to the plan a supervised job is running, and mints a new version.
    /// </summary>
    /// <remarks>
    /// The declared counterpart of <see cref="ApplyTo"/>: the tree comes from the store the
    /// supervision reads, the new version goes back to it, and <c>PlanVersionMinted</c> reaches the
    /// run's event stream. Whether the revision is needed is still decided outside — by a job of the
    /// consumer's, above the node that reported the contradiction.
    /// </remarks>
    public Task<PlanTree> ApplyToAsync(
        SupervisionOptions supervision, string planId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(supervision);

        return supervision.ReruleAsync(
            planId,
            Node,
            ToContract(),
            Reason,
            Children.Count == 0
                ? null
                : Children.Select(c => new AuthoredStep
                {
                    Id = c.Id ?? throw new InvalidOperationException("A replacement child needs an id."),
                    Contract = c.ToContract()
                }),
            ct: ct);
    }
}
