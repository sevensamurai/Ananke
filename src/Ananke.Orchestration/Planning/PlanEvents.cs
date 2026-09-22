using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tracing;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// Something that happened while a plan ran, reported on the workflow's event stream.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not an observer interface.</b> There were already four of those, wired four different ways,
/// and a plan-shaped fifth would have hidden that rather than named it. A workflow event is the seam
/// that already exists for concerns arising below a job — <c>BudgetWarning</c> is the precedent —
/// and it costs a consumer nothing new to read.
/// </para>
/// <para>
/// <b>Carries no workflow state</b>, which is what lets it exist at all: the thing reporting is a
/// plan, and a plan knows nothing about the state of whatever workflow is running it.
/// </para>
/// </remarks>
public abstract record PlanEvent : WorkflowEvent
{
    /// <summary>The plan this is about.</summary>
    public required string PlanId { get; init; }

    /// <summary>The plan version in force when it happened.</summary>
    public required int PlanVersion { get; init; }

    /// <summary>
    /// Stamps the workflow identity from the job this is running inside, if any.
    /// </summary>
    /// <remarks>
    /// A plan run outside a workflow reports an empty workflow name and execution id rather than
    /// inventing one. The plan is identified by <see cref="PlanId"/> either way, and claiming a
    /// workflow that does not exist would be worse than saying there is none.
    /// </remarks>
    internal static (string WorkflowName, string ExecutionId) Identity() =>
        WorkflowTraceContext.Value is { } trace
            ? (trace.WorkflowName, trace.ExecutionId)
            : (string.Empty, string.Empty);
}

/// <summary>A node is about to run, and this is what it is being shown.</summary>
/// <remarks>
/// The projection figures ride here rather than being asked for later, because they describe the
/// view <em>this</em> run was given. Once the node has run, the tree records one reading and the
/// question "what did it not see" is answerable only for the most recent attempt.
/// </remarks>
public sealed record PlanNodeStarted : PlanEvent
{
    /// <summary>The node about to run.</summary>
    public required string NodeId { get; init; }

    /// <summary>What it was asked to do.</summary>
    public required string Goal { get; init; }

    /// <summary>Estimated size of the view it was given.</summary>
    public required int ProjectedTokens { get; init; }

    /// <summary>Ancestors the projection did not describe.</summary>
    public int OmittedAncestors { get; init; }

    /// <summary>Records established elsewhere that the projection did not list.</summary>
    public int OmittedRecords { get; init; }

    /// <summary>Whether it saw the whole of its surroundings.</summary>
    public bool SawEverything => OmittedAncestors == 0 && OmittedRecords == 0;
}

/// <summary>What a node said it did.</summary>
/// <remarks>
/// The one thing a report carries that the tree deliberately has no room for. A node's output is a
/// file, a commit, a change in the world — verified by its criteria, not carried in a message — so
/// the summary is narrated and goes no further.
/// </remarks>
public sealed record PlanNodeReported : PlanEvent
{
    /// <summary>The node that reported.</summary>
    public required string NodeId { get; init; }

    /// <summary>Its account of what it did. Never evidence of anything.</summary>
    public required string Summary { get; init; }
}

/// <summary>A node's contract was disputed, and the run stopped there.</summary>
/// <remarks>
/// Raised only by <see cref="PlanExecutor.DisputeAsync"/> now — an external call, never a node's own
/// report (R26). A node cannot classify its own halt, so there is nothing here for it to get wrong.
/// </remarks>
public sealed record PlanNodeDisputed : PlanEvent
{
    /// <summary>The node that was disputed.</summary>
    public required string NodeId { get; init; }

    /// <summary>The criterion believed to be wrong or unmeetable.</summary>
    public required string Criterion { get; init; }

    /// <summary>What was found that contradicts it.</summary>
    public required string Reason { get; init; }
}

/// <summary>A node's work was ruled on, from outside the node.</summary>
/// <summary>An attempt at a node did not finish.</summary>
/// <remarks>
/// Distinct from a dispute, and the difference is who is claiming what: a node that disputes has
/// decided its contract is wrong, and a node that fails has not decided anything at all.
/// </remarks>
public sealed record PlanNodeFailed : PlanEvent
{
    /// <summary>The node whose attempt died.</summary>
    public required string NodeId { get; init; }

    /// <summary>What threw, in its own words.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Whether waiting could never fix it — an account out of allowance rather than a provider
    /// having a bad minute.
    /// </summary>
    /// <remarks>
    /// On the stream because the two need different actions from whoever is watching, and a reader
    /// who has to tell them apart by reading the message is a reader who will get it wrong once.
    /// </remarks>
    public bool Terminal { get; init; }
}

public sealed record PlanNodeVerified : PlanEvent
{
    /// <summary>The node ruled on.</summary>
    public required string NodeId { get; init; }

    /// <summary>The ruling.</summary>
    public required VerificationOutcome Outcome { get; init; }

    /// <summary>Criteria nothing could decide. The gap worth watching.</summary>
    public IReadOnlyList<string> Abstained { get; init; } = [];

    /// <summary>Quality ranking in <c>[0,1]</c>, only ever present once every gate passed.</summary>
    public double? Score { get; init; }
}

/// <summary>One pass over the plan finished.</summary>
public sealed record PlanPassCompleted : PlanEvent
{
    /// <summary>Nodes that ran, in order.</summary>
    public IReadOnlyList<string> Executed { get; init; } = [];

    /// <summary>Nodes skipped because the tree already recorded them satisfied.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    /// <summary>The node that stopped the pass, if one did — by disputing, failing, or asking.</summary>
    public string? HaltedAt { get; init; }

    /// <summary>What the verdicts say about the root after the pass.</summary>
    public required ContractOutcome RootOutcome { get; init; }
}

/// <summary>A step is waiting to be told something, and the run stopped there.</summary>
/// <remarks>
/// Raised only by <see cref="PlanExecutor.AskAsync"/> now — the supervisor's own call (R31), never a
/// step reaching and reporting a choice on its own.
/// </remarks>
public sealed record PlanNodeBlocked : PlanEvent
{
    /// <summary>The step that is waiting.</summary>
    public required string NodeId { get; init; }

    /// <summary>What it needs decided.</summary>
    public required string Asks { get; init; }

    /// <summary>The admissible answers, as it found them.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>A step was told what it was waiting for.</summary>
public sealed record PlanNodeAnswered : PlanEvent
{
    /// <summary>The step that asked.</summary>
    public required string NodeId { get; init; }

    /// <summary>What it asked.</summary>
    public required string Asked { get; init; }

    /// <summary>What it was told.</summary>
    public required string Answer { get; init; }

    /// <summary>Who told it.</summary>
    public required string By { get; init; }
}

/// <summary>Somebody gave a step up: it stays in the plan and nothing will attempt it again.</summary>
/// <remarks>
/// <b>Reported because it is a decision, not an absence of one.</b> No version is minted — the plan
/// was not re-authored — so without this the only trace would be a field on a node nobody thought to
/// look at. It is also where a consumer releases whatever the step was holding: the tier says which
/// step was given up, and what that frees is the domain's to know.
/// </remarks>
/// <summary>Something was settled about the plan that will outlive the halt it was settled at.</summary>
/// <remarks>
/// <b>On the stream because it changes what every later version may say, and mints no version to
/// show for it.</b> A term that only appeared as a constraint on some contract two changes of plan
/// later would be a rule nobody could trace to a decision.
/// </remarks>
public sealed record PlanTermCommitted : PlanEvent
{
    /// <summary>Identifies it, so a later revocation names the same thing.</summary>
    public required string TermId { get; init; }

    /// <summary>What it binds, as the supervisor read it.</summary>
    public required string Reading { get; init; }

    /// <summary>The words it was read from, verbatim.</summary>
    public required string Said { get; init; }

    /// <summary>Who said them.</summary>
    public required string By { get; init; }
}

/// <summary>A term stopped holding, and why.</summary>
public sealed record PlanTermContradicted : PlanEvent
{
    /// <summary>The term revoked.</summary>
    public required string TermId { get; init; }

    /// <summary>What it bound, in words — so a reader is not left holding an id.</summary>
    public required string Reading { get; init; }

    /// <summary>Why it no longer holds.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Whether somebody took it back or this plan merely could not hold it.
    /// </summary>
    /// <remarks>
    /// <b>The field anything remembering terms across runs has to read.</b> A retraction is about what
    /// somebody wants and should follow them; a waiver is about one plan and must not.
    /// </remarks>
    public required PlanTermEnd End { get; init; }

    /// <summary>Who ended it.</summary>
    public required string By { get; init; }
}

public sealed record PlanStepAbandoned : PlanEvent
{
    /// <summary>The step that was given up.</summary>
    public required string NodeId { get; init; }

    /// <summary>Why, in the words of whoever decided.</summary>
    public required string Reason { get; init; }

    /// <summary>Who decided.</summary>
    public required string By { get; init; }
}

/// <summary>The supervisor was asked what a halt means, and this is what it said.</summary>
/// <remarks>
/// <para>
/// <b>The one judgement in the tier that had no line of its own.</b> An <c>Ask</c> announces itself
/// here and nowhere else; a <c>Replan</c> also mints a <see cref="PlanVersionMinted"/>. This is the
/// tier's central act; it says so.
/// </para>
/// <para>
/// <b>Reported before the decision is applied</b>, so the account reads in the order it happened:
/// what halted, what was decided, then whatever the decision did.
/// </para>
/// </remarks>
public sealed record PlanDecisionTaken : PlanEvent
{
    /// <summary>The node the supervisor was asked about.</summary>
    public required string NodeId { get; init; }

    /// <summary>What was decided. The same object the tier then acts on, not a copy of it.</summary>
    public required PlanDecision Decision { get; init; }

    /// <summary>Changes of plan already spent when this was decided.</summary>
    public int Changes { get; init; }

    /// <summary>The ceiling a consumer asked for, or <see langword="null"/> if they asked for none.</summary>
    public int? MaxChanges { get; init; }

    /// <summary>
    /// Whether the run had paused before this decision, so the answer came from outside it.
    /// </summary>
    /// <remarks>
    /// An escalated decision is reported and never counted, so a reader printing
    /// <c>"{Changes} of {MaxChanges} changes of plan used"</c> beside one is describing a bound it is
    /// not under. Observed from how the coordinator was entered, never declared.
    /// </remarks>
    public bool Escalated { get; init; }
}

/// <summary>The plan changed, and this is why.</summary>
public sealed record PlanVersionMinted : PlanEvent
{
    /// <summary>The node whose contract was re-ruled.</summary>
    public required string ReRuledNodeId { get; init; }

    /// <summary>Why the previous version stopped being right, as observed at the halt.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Whoever decided the change, and their argument for it. <see langword="null"/> when nobody
    /// offered one.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="Reason"/> rather than folded into it: one was observed and the other
    /// inferred, and a reader who cannot tell them apart cannot tell a record from a guess.
    /// </remarks>
    public PlanRationale? Rationale { get; init; }

    /// <summary>
    /// Work the re-ruling decided was no longer needed. Gone from this version, still readable in
    /// every version before it.
    /// </summary>
    public IReadOnlyList<string> DroppedNodeIds { get; init; } = [];

    /// <summary>
    /// Acceptance criteria this re-ruling authored that nothing configured can decide.
    /// <see langword="null"/> when nobody could say — no verifier, or one that cannot answer
    /// without running the work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap this closes is a silent one.</b> Verdicts are matched by exact text, so a criterion
    /// nobody wrote a check for is <em>abstained</em> rather than failed — correctly — and a plan
    /// re-authored in new words can come out of a change of plan gated by nothing at all while every
    /// node still reads as fine. Reported here because this is the moment it happens; the criteria
    /// do not <b>block</b> the version, because abstention stays legitimate.
    /// </para>
    /// <para>
    /// <b>Gates only.</b> A quality criterion nothing can decide narrows the score and cannot turn an
    /// unverified run into a finished-looking one, and counting both would dilute the number that
    /// matters.
    /// </para>
    /// <para>
    /// <b>An event, never tree state</b>, for the same reason an abstention is not written to the
    /// tree: it is a fact about what was configured when the version was minted, not about the plan.
    /// The same criteria under a differently configured run are a different answer.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string>? CriteriaNothingCanDecide { get; init; }
}

/// <summary>The supervisor was asked what a halt admits, and this is what it offered.</summary>
public sealed record PlanProposalOffered : PlanEvent
{
    /// <summary>The node that halted.</summary>
    public required string NodeId { get; init; }

    /// <summary>Why it halted, as the supervisor was shown it.</summary>
    public string? HaltReason { get; init; }

    /// <summary>What was offered, in the order it was shown.</summary>
    public IReadOnlyList<PlanOption> Options { get; init; } = [];

    /// <summary>What was offered and not kept, and why.</summary>
    public IReadOnlyList<string> Discarded { get; init; } = [];

    /// <summary>Whether the supervisor ran out of tool rounds before it answered.</summary>
    public bool Exhausted { get; init; }
}
