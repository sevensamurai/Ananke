using Ananke.Orchestration.Agents.Context;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// One entry in a coordinator's table: a halt it recognises, and the contract that answers it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A rule describes a halt, not a node.</b> Both filters are optional and both narrow: a rule with
/// neither answers the first halt of any kind, which is what a plan whose revisions were written in
/// advance actually wants.
/// </para>
/// <para>
/// <b>The table holds re-rulings only.</b> Retrying and stopping are what a coordinator does when the
/// table has nothing to say, so they are policy around the table rather than entries in it — there is
/// no such thing as a table of things not to do.
/// </para>
/// </remarks>
public sealed record HaltRule
{
    /// <summary>The node that stopped, or <see langword="null"/> for any node.</summary>
    public string? NodeId { get; init; }

    /// <summary>What replaces the contract.</summary>
    public required AgentContract Contract { get; init; }

    /// <summary>The replacement decomposition. <see langword="null"/> keeps the node's children.</summary>
    public IReadOnlyList<(string Id, AgentContract Contract)>? Children { get; init; }

    /// <summary>
    /// The node to re-rule. <see langword="null"/> means the one that halted.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="NodeId"/> because the node that stops and the node whose contract has
    /// to change are routinely different: a contradiction surfaces in a child while the shape that
    /// caused it belongs to the parent.
    /// </remarks>
    public string? ReruleNodeId { get; init; }

    /// <summary>Why this replacement answers that halt, attributed to whoever wrote the table.</summary>
    public PlanRationale? Rationale { get; init; }

    /// <summary>Whether this rule speaks to <paramref name="coordination"/>.</summary>
    internal bool Matches(PlanCoordination coordination) =>
        NodeId is null || string.Equals(NodeId, coordination.NodeId, StringComparison.Ordinal);
}

/// <summary>
/// A coordinator that decides from a table written in advance, and never calls a model.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this ships beside the model-backed one.</b> Steering a plan is not inherently a judgement
/// call. A run whose failure modes are known — a flaky deployment step, a format that needs a second
/// parser, a check that is wrong on a known input — wants a decision that is repeatable, free, and
/// reviewable before the run rather than after it. A coordinator seat that only a model could fill
/// would exclude every consumer who already knows what to do.
/// </para>
/// <para>
/// <b>Two behaviours, in order.</b> The first unspent rule that matches the halt replans the plan;
/// otherwise it asks, with nothing to offer. Reading in that order is deliberate — a table entry is
/// something a person decided in advance, and it outranks silence.
/// </para>
/// <para>
/// <b>A rule is spent when it fires.</b> Handing back the same replacement at every halt is a change
/// of plan nobody authored, and it would exhaust the run's change-of-plan budget re-applying an
/// answer that has already been tried.
/// </para>
/// <para>
/// <b>Retrying a transient failure is not this seat's any more.</b> R30: a provider outage or a reply
/// that would not parse is a loop-level policy (E9), never a coordinator steering — so this type no
/// longer counts retries or matches on how a halt happened, only on which node it was.
/// </para>
/// </remarks>
public sealed class DeterministicPlanSupervisor
{
    private readonly List<HaltRule> _rules;

    /// <summary>Creates a coordinator over <paramref name="rules"/>, matched in the order given.</summary>
    /// <param name="rules">The table. First match wins, and each entry fires at most once.</param>
    public DeterministicPlanSupervisor(IEnumerable<HaltRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _rules = [.. rules];
    }

    /// <summary>Reads the halt and answers from the table.</summary>
    /// <exception cref="InvalidOperationException">The plan did not halt, so there is nothing to decide.</exception>
    public Task<PlanDecision> DecideAsync(
        PlanCoordination coordination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(coordination);
        ct.ThrowIfCancellationRequested();

        if (coordination.NodeId is null)
            throw new InvalidOperationException(
                "A DeterministicPlanSupervisor was asked about a plan that did not halt. Nothing "
                + "stopped, so there is no contract to replace.");

        if (_rules.FirstOrDefault(rule => rule.Matches(coordination)) is { } matched)
        {
            _rules.Remove(matched);

            return Task.FromResult(PlanDecision.Replan(
                matched.Contract,
                matched.Rationale,
                matched.Children?.Select(c => new AuthoredStep { Id = c.Id, Contract = c.Contract }),
                matched.ReruleNodeId));
        }

        return Task.FromResult(PlanDecision.Ask([]));
    }

    /// <summary>This coordinator as the delegate a supervision takes.</summary>
    public PlanSupervisor AsCoordinator() => DecideAsync;
}
