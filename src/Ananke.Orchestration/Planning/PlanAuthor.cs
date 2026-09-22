using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Streaming;

namespace Ananke.Orchestration.Planning;

/// <summary>One step a re-authored plan would have.</summary>
public sealed record AuthoredStep
{
    /// <summary>Its id, which the domain decides the meaning of.</summary>
    public required string Id { get; init; }

    /// <summary>What it is asked to do.</summary>
    public required AgentContract Contract { get; init; }
}

/// <summary>A plan as the Planner would now write it.</summary>
/// <remarks>
/// <b>The whole plan, every time.</b> Admission is a program with no I/O and no model, so re-checking
/// what was already accepted costs nothing worth saving — and incremental admission is more code that
/// can miss an interaction between a step that changed and one that did not.
/// </remarks>
public sealed record AuthoredPlan
{
    /// <summary>What the plan as a whole now asks for.</summary>
    public required AgentContract Contract { get; init; }

    /// <summary>The steps, in the order they should be attempted.</summary>
    public required IReadOnlyList<AuthoredStep> Steps { get; init; }

    /// <summary>Why the plan is being re-authored, in the Planner's words.</summary>
    public string? Rationale { get; init; }
}

/// <summary>
/// Re-authors a plan when a halt is one no single step can absorb.
/// </summary>
/// <remarks>
/// <para>
/// <b>The seat R2 named and nothing filled.</b> A Supervisor may replace a contract; only this may
/// decide which steps there are. It is reached when the Supervisor has nothing to offer — which is a
/// statement about the <em>shape</em> of the plan, made by the one role that has seen the halt, the
/// tools and the tree (R25).
/// </para>
/// <para>
/// <b>It is told what was refused.</b> Everything the Supervisor tried and every reason it could not
/// be used travels with the ask, because that is the most specific evidence anybody has about why the
/// present shape does not work.
/// </para>
/// <para>
/// Returning <see langword="null"/> is a real answer: the plan is what it should be, and the halt
/// stands.
/// </para>
/// </remarks>
public delegate Task<AuthoredPlan?> PlanAuthor(
    PlanCoordination coordination, IReadOnlyList<string> refused, CancellationToken ct);

/// <summary>The Planner re-authored the plan, and this is what it wrote.</summary>
public sealed record PlanReauthored : PlanEvent
{
    /// <summary>The steps the plan now has.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    /// <summary>Each step as the Planner wrote it, with its contract.</summary>
    public IReadOnlyList<AuthoredStep> Authored { get; init; } = [];

    /// <summary>What was in it before and is not now.</summary>
    public IReadOnlyList<string> DroppedNodeIds { get; init; } = [];

    /// <summary>Why, in the Planner's words.</summary>
    public string? Rationale { get; init; }
}

/// <summary>The Planner was asked to re-author the plan and would not.</summary>
public sealed record PlanNotReauthored : PlanEvent
{
    /// <summary>Why nothing was written — a refusal from admission, or the Planner's own answer.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Hands a halt nobody could propose against to the Planner, and applies what it writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>It runs on exactly one signal: a proposal that kept nothing.</b> Not on a proposal that was
/// exhausted — that is a tool budget, not a plan — and not on a halt somebody has been asked about,
/// because a person choosing is the answer and this is what happens when there was nothing to choose.
/// </para>
/// <para>
/// <b>What it may not do is un-abandon.</b> Work somebody gave up stays given up: R10 in its narrowest
/// form, and it holds by construction — a re-listed node keeps the decision recorded on it, and one
/// left out is dropped rather than revived.
/// </para>
/// </remarks>
public sealed class PlanAuthorJob<TState>(
    string name,
    PlanAuthor author,
    SupervisionOptions supervision,
    Func<TState, PlanCoordination?> read,
    Func<TState, PlanCoordination, TState> write) : IJob<TState>
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct = default)
    {
        if (read(state) is not { } coordination
            || coordination.Result.HaltedAt is not { } haltedAt
            || coordination.Question is not null
            || coordination.Decision is not (null or PlanDecision.ReplanPlan { Contract: null }))
        {
            return state;
        }

        var refused = coordination.Refused;
        var tree = coordination.Result.Tree;
        var (workflowName, executionId) = PlanEvent.Identity();

        var authored = await author(coordination, refused, ct).ConfigureAwait(false);

        if (authored is null || authored.Steps.Count == 0)
        {
            await Report(Nothing("the planner was asked and wrote nothing")).ConfigureAwait(false);

            return write(state, coordination with { Decision = null });
        }

        var steps = authored.Steps.Select(s => (s.Id, s.Contract)).ToList();

        // Admitted before it runs, exactly as an authored plan is: a plan whose criteria name checks
        // that do not exist, or that could never hold, is refused rather than executed to find out.
        var proposed = tree.Rerule(
            tree.Root.Id, authored.Contract, coordination.HaltReason ?? "re-planned", steps,
            supervision.TimeProvider);

        if (Refusals(proposed) is { Count: > 0 } faults)
        {
            await Report(Nothing("the plan it wrote would not run: " + string.Join("; ", faults)))
                .ConfigureAwait(false);

            return write(state, coordination with { Decision = null });
        }

        var applied = await supervision.ReruleAsync(
            tree.PlanId,
            tree.Root.Id,
            authored.Contract,
            coordination.HaltReason ?? $"nothing could be proposed for '{haltedAt}'",
            authored.Steps,
            string.IsNullOrWhiteSpace(authored.Rationale)
                ? null
                : new PlanRationale { By = PlanRoles.Planner, Text = authored.Rationale! },
            ct).ConfigureAwait(false);

        await Report(new PlanReauthored
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = tree.PlanId,
            PlanVersion = tree.Current.Number + 1,
            Steps = [.. steps.Select(s => s.Id)],
            Authored = authored.Steps,
            DroppedNodeIds = proposed.Current.DroppedNodeIds,
            Rationale = authored.Rationale
        }).ConfigureAwait(false);

        // The halt is answered: the plan is different now, and the next pass runs it. No Decision to
        // record, because the re-ruling has already been applied and a Replan that claimed to carry
        // one would be a second copy of something already in the tree — R30's "an answer arrives,
        // plan unchanged" is the absence of a verb, not one more, and clearing HaltedAt is what tells
        // the topology's routing to fall through to the plan rather than reading this as stuck.
        // The applied tree goes back into the coordination. The next pass would reload it from
        // the store anyway, but anything reading the state in between — a narrator, a person
        // looking at where the run stands — would otherwise be shown the plan this just replaced.
        return write(state, coordination with
        {
            Result = coordination.Result with { Tree = applied, HaltedAt = null },
            Decision = null,
            Question = null,
            Refused = []
        });

        ValueTask Report(PlanEvent evt) => WorkflowEventReporting.ReportAsync(evt, ct);

        PlanNotReauthored Nothing(string reason) => new()
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = tree.PlanId,
            PlanVersion = tree.Current.Number,
            Reason = reason
        };
    }

    /// <summary>Why the re-authored plan may not run, or nothing when it may.</summary>
    private IReadOnlyList<string> Refusals(PlanTree proposed)
    {
        List<string> faults =
        [
            .. PlanAdmission.Faults(proposed, supervision.Checks).Select(f => f.ToString())
        ];

        if (supervision.AdmitCriterion is { } admit)
        {
            faults.AddRange(
                from node in proposed.Current.Nodes
                where node.Key != proposed.Current.RootId
                from criterion in node.Value.Contract.AcceptanceCriteria
                let refused = admit(node.Key, criterion)
                where refused is not null
                select $"'{node.Key}' may not be asked \"{criterion}\": {refused}");
        }

        return faults;
    }
}
