using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;

namespace Ananke.Orchestration.Planning;

/// <summary>What a coordinator model is asked to produce: the contract that replaces a disputed one.</summary>
/// <remarks>
/// Deliberately small. A model authoring a plan is authoring <em>work</em>, so it is asked for a goal
/// and criteria — not for a status, a priority or an estimate, none of which anything in this tier
/// would read.
/// </remarks>
public sealed record PlanRevision
{
    /// <summary>What the work item should now achieve.</summary>
    public string Goal { get; init; } = string.Empty;

    /// <summary>What would show it was achieved. Empty means the model had nothing to propose.</summary>
    public IReadOnlyList<string> Criteria { get; init; } = [];

    /// <summary>Why the replacement answers what went wrong.</summary>
    /// <remarks>
    /// <b>Not why the old contract stopped being right</b> — that was observed at the halt and the
    /// model did not witness it. This is the model's argument for what it just wrote, which is the
    /// one thing here it is actually in a position to know, and it is recorded attributed to the
    /// planner rather than merged into the version's reason.
    /// </remarks>
    public string Rationale { get; init; } = string.Empty;
}

/// <summary>
/// A coordinator that asks a model what a halted plan should become.
/// </summary>
/// <remarks>
/// <para>
/// <b>It runs on the <see cref="PlanRoles.Supervisor"/> model, not the one doing the work.</b> A node's
/// work is repetitive and gated by checks that will catch a bad answer; deciding that a plan is wrong
/// and authoring its replacement happens once and is checked by nothing. The two roles are read from
/// the supervision, so which model makes the expensive judgement is visible where the plan is
/// configured rather than buried in whichever model reference was nearest.
/// </para>
/// <para>
/// <b>Constraints are carried over, never re-authored.</b> They are what the level above already
/// decided, and a redesign free to drop one could always resolve a dispute by deleting the rule the
/// node hit — which is not a plan changing its mind, it is a plan giving up while reporting success.
/// The iteration bound carries over for the same reason.
/// </para>
/// </remarks>
public sealed class AgentPlanSupervisor(SupervisionOptions supervision, string? persona = null)
{
    /// <summary>Enough rounds to try a candidate, be told it does not work, and try another.</summary>
    private const int DefaultToolRounds = 12;

    private const string DefaultPersona =
        "You own a plan. One work item has reported that the contract you gave it cannot be met, and "
        + "it stopped rather than delivering something adjacent. Author the contract that replaces "
        + "it: keep the intent, remove the contradiction, and stay inside the constraints you are "
        + "shown. Criteria must be statements a check could decide. You are not asked what went "
        + "wrong — that is already recorded, in the words of whatever stopped. You are asked what "
        + "the work should now be, and why your replacement answers it.";

    /// <summary>Reads the halt, asks the planner, and returns what to do.</summary>
    /// <exception cref="InvalidOperationException">The plan did not halt, so there is nothing to decide.</exception>
    public async Task<PlanDecision> DecideAsync(PlanCoordination coordination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(coordination);

        if (coordination.Node is not { } node)
            throw new InvalidOperationException(
                "An AgentPlanSupervisor was asked about a plan that did not halt. Nothing stopped, "
                + "so there is no contract to replace.");

        var model = supervision.ModelFor(PlanRoles.Supervisor)
            ?? throw new InvalidOperationException(
                "An AgentPlanSupervisor needs a model for the 'supervisor' role: set Supervisor on the "
                + "supervision, or add it to Models.");

        PlanRevision? revision = null;

        var builder = AgentJobFactory.Create<PlanCoordination, PlanRevision>("plan-coordinator", model)
            .WithSystemPrompt(persona ?? DefaultPersona)
            .WithPrompt(Describe)
            .MapResult((halt, produced) =>
            {
                revision = produced;
                return halt;
            });

        // What answers "would this work?" — the same hook the advisor reads, so the two jobs that
        // run on this role are never unequally informed about the world they are re-planning.
        if (supervision.SupervisorTools is { } tools)
            builder = builder
                .WithTools(tools)
                .WithMaxToolRounds(supervision.SupervisorToolRounds ?? DefaultToolRounds);

        var job = builder.Build();

        try
        {
            await job.ExecuteAsync(coordination, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException exhausted) when (AgentPlanAdvisor.OutOfRounds(exhausted))
        {
            // A coordinator that ran out of rounds decided nothing, which the halt standing already
            // expresses. Faulting instead ends the run at the halt with a message about tool
            // budgets — true, and about the framework rather than about the plan. Asking with nothing
            // to offer is the honest way to say so now that Stop is not a verb (R30) — the run pauses
            // rather than looping, and a person's cancel or refusal is what ends it.
            return PlanDecision.Ask([]);
        }

        // Nothing proposed is not a re-ruling with an empty contract. A plan whose replacement has no
        // criteria could never be shown to have worked, so the honest outcome is asking with nothing
        // to offer — the same as running out of rounds, above.
        if (revision is null || revision.Criteria.Count == 0)
            return PlanDecision.Ask([]);

        return PlanDecision.Replan(
            new AgentContract
            {
                Goal = revision.Goal,
                AcceptanceCriteria = [.. revision.Criteria],
                Constraints = node.Contract.Constraints
            },
            // Attributed to the role, not to the model: it is the attribution that stays true when
            // the model behind the role changes, and the model has no identity to name anyway.
            string.IsNullOrWhiteSpace(revision.Rationale)
                ? null
                : new PlanRationale { By = PlanRoles.Supervisor, Text = revision.Rationale });
    }

    /// <summary>This coordinator as the delegate a supervision takes.</summary>
    public PlanSupervisor AsCoordinator() => DecideAsync;

    /// <summary>
    /// What the planner is shown: the contract, what the node did, what it decided, its dispute, and
    /// the plan around it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The verdicts matter as much as the dispute. A model asked only *"this contract is disputed,
    /// write another"* is being asked to explain a failure it did not witness, and will oblige.
    /// </para>
    /// <para>
    /// The node's own account is shown <b>labelled as its account</b>, not as fact. It is the only
    /// description of what was actually attempted, and a planner reasoning from structure alone has
    /// nothing to reason from — but it is a claim by the thing whose work is in question, and the
    /// prompt says so rather than letting a model read it as a finding.
    /// </para>
    /// </remarks>
    private string Describe(PlanCoordination coordination) =>
        DescribeHalt(
            coordination,
            AgentPlanAdvisor.Check(supervision)
            + """
            Reply with the replacement goal, the replacement criteria, and your rationale for why
            the replacement answers that. Do not restate what went wrong; it is already kept.
            If nothing would fix it, reply with no criteria.
            """);

    /// <summary>
    /// The halt as anything asking a model about it needs to see it, without the ask itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shared with <see cref="AgentPlanAdvisor"/>, which puts a different question to the same
    /// evidence. Two descriptions of one halt would drift, and the part most worth not drifting is
    /// the framing: what is shown here is evidence, and a node's own narration of its work is not
    /// among it.
    /// </para>
    /// <para>
    /// <b>The plan projection is unbounded, and that is deliberate rather than an oversight.</b> This
    /// is a judgement made from outside the node, the same seat a verifier rules from — and a judge
    /// reading the node's own trimmed view would be ruling on what the node happened to be shown
    /// rather than on the record. Reusing the node's own budget here was found live: nothing was
    /// wrong with the mechanism, the budget was simply the wrong one to hand to a second reader.
    /// </para>
    /// </remarks>
    internal static string DescribeHalt(PlanCoordination coordination, string ask)
    {
        var node = coordination.Node!;
        var projection = PlanTreeProjection.Project(coordination.Result.Tree, coordination.NodeId!);

        return $"""
            The plan it belongs to:
            {projection.Text}

            The work item is '{coordination.NodeId}'.

            Its goal: {node.Contract.Goal}
            Its criteria: {string.Join(" | ", node.Contract.AcceptanceCriteria)}
            Its constraints, which you may not drop: {string.Join(" | ", node.Contract.Constraints)}

            What it decided:
            {Verdicts(node)}

            {Contradiction(coordination)}

            What already stands recorded as why this stopped, in the words of whatever stopped:
            {coordination.HaltReason}

            {ask}
            """;
    }

    /// <summary>How much of the evidence section a halt's prompt may spend, in characters.</summary>
    /// <remarks>
    /// A guess, in the same spirit as the cap on one command's captured output — long enough that a
    /// real failure is legible, short enough that many failing criteria cannot balloon the prompt.
    /// Whole verdicts are dropped rather than a basis cut mid-sentence, so what remains is never a
    /// truncated half of something; what was dropped is said, the same convention the plan
    /// projection already keeps for an ancestor or a record it could not fit.
    /// </remarks>
    private const int EvidenceCap = 8_000;

    /// <summary>
    /// What the node decided, criterion by criterion — and for the ones that did not hold, why.
    /// </summary>
    /// <remarks>
    /// Only a failing verdict's <see cref="CriterionVerdict.Basis"/> is shown. A verdict that passed
    /// needs nothing explained, and the budget above exists for the failures a decision is actually
    /// being asked about.
    /// </remarks>
    private static string Verdicts(PlanNode node)
    {
        if (node.LatestVerdicts.Count == 0)
            return "  (nothing was decided about it)";

        var lines = new List<string>();
        var spent = 0;
        var dropped = 0;

        foreach (var verdict in node.LatestVerdicts)
        {
            var line = $"  {(verdict.Passed ? "met" : "not met")}: {verdict.Criterion}  ({verdict.Oracle})";

            if (!verdict.Passed && !string.IsNullOrWhiteSpace(verdict.Basis))
                line += $"\n    {verdict.Basis}";

            // At least one verdict always survives, even alone over budget: a section that renders
            // nothing is the case an omission notice cannot rescue, because there is no notice
            // without a first line to attach it to.
            if (lines.Count > 0 && spent + line.Length > EvidenceCap)
            {
                dropped++;
                continue;
            }

            lines.Add(line);
            spent += line.Length;
        }

        if (dropped > 0)
        {
            lines.Add(
                $"  ({dropped} further verdict(s) not shown here; the plan still holds them.)");
        }

        return string.Join('\n', lines);
    }

    private static string Contradiction(PlanCoordination coordination) =>
        coordination.Dispute is { } dispute
            ? $"""
               It disputes: "{dispute.Criterion}"
               Because: {dispute.Reason}
               """
            : "It raised no dispute; it simply did not get there.";
}
