using System.Text;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Streaming;

namespace Ananke.Orchestration.Planning;

/// <summary>What a node is given when it runs. Everything here came out of the tree.</summary>
/// <remarks>
/// There is deliberately no property here holding a sibling's result, a parent's return value or a
/// message history. If one were added, the design would quietly become a handoff again and the tree
/// would become a log of it.
/// </remarks>
public sealed record PlanNodeContext
{
    /// <summary>The plan it belongs to.</summary>
    public required string PlanId { get; init; }

    /// <summary>The plan version in force when it started.</summary>
    /// <remarks>
    /// Not a handoff: it is a fact about the tree the node is reading, and the same fact the tree
    /// records against the node afterwards so that "was this checked against the plan as it stands?"
    /// stays answerable.
    /// </remarks>
    public required int PlanVersion { get; init; }

    /// <summary>The node about to run.</summary>
    public required PlanNode Node { get; init; }

    /// <summary>Its contract — pinned, and never trimmed by the projection budget.</summary>
    public required AgentContract Contract { get; init; }

    /// <summary>The tree as this node should read it, rendered to fit its allocation.</summary>
    public required string TreeView { get; init; }

    /// <summary>How many ancestors and records the projection left out, if any.</summary>
    public required PlanTreeProjection.Projection Projection { get; init; }

    /// <summary>
    /// What the last attempt proposed and why it could not be used, when this call is a mechanical
    /// retry after a shape-gate rejection. <see langword="null"/> on a node's first attempt.
    /// </summary>
    /// <remarks>
    /// The only memory a retry gets, because the executor is stateless between attempts by design
    /// — a fresh job per attempt, nothing carried in an object alive across two. Carried verbatim,
    /// never paraphrased, so a runner can see exactly what it said last time and exactly what was
    /// wrong with it.
    /// </remarks>
    public NodeRejection? Rejection { get; init; }
}

/// <summary>What a runner said about a rejected candidate, for the retry that follows it.</summary>
/// <param name="Candidate">The rejected candidate, verbatim — never summarised.</param>
/// <param name="Finding">Why it could not be used, verbatim — the world's or the gate's own words.</param>
public sealed record NodeRejection(Operation Candidate, string Finding);

/// <summary>What a node produced: verdicts against its criteria, and what it says it did.</summary>
/// <remarks>
/// A step reports narration, <see cref="Done"/> and <see cref="Options"/> — never a verdict, a
/// question, or a dispute of its own. A dispute still reaches a node's record, but only through
/// <see cref="PlanExecutor.DisputeAsync"/>, an external call rather than something this type can
/// carry.
/// </remarks>
public sealed record NodeOutcome
{
    /// <summary>Verdicts recorded against this node's acceptance criteria.</summary>
    public IReadOnlyList<CriterionVerdict> Verdicts { get; init; } = [];

    /// <summary>
    /// What the node says it did, in its own words. <see langword="null"/> when it offers none.
    /// </summary>
    /// <remarks>
    /// Narration, never evidence — nothing derives from it. It is kept because it is the one thing
    /// about a run that no verdict can reconstruct, and because whoever decides what a halted plan
    /// should become is otherwise reasoning about work it did not witness.
    /// </remarks>
    public string? Summary { get; init; }

    /// <summary>
    /// The change this node proposes making to the world.
    /// <see langword="null"/> when it proposes nothing, which is the ordinary case for a node that
    /// only decomposes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never applied by this type, and never by the node.</b> A non-empty candidate is handed to
    /// whatever <see cref="PlanExecutor"/> was given to apply it, after the shape gate has looked at
    /// it — form only, never whether it will actually work: a shape gate that judged content would
    /// absorb exactly the deviation the loop may not decide.
    /// </para>
    /// <para>
    /// <b>Empty is a candidate too, and a different fact from <see langword="null"/>.</b> Whitespace
    /// or an empty string means the node tried to propose something and produced nothing usable — the
    /// shape gate rejects it and the pass retries mechanically. <see langword="null"/> means it was
    /// never asked, or had nothing of its own to change, and no gate is consulted at all.
    /// </para>
    /// </remarks>
    public Operation? Candidate { get; init; }

    /// <summary>Whether the node could do its task, as it reported it.</summary>
    public bool Done { get; init; } = true;

    /// <summary>Every option the node found, as it reported it.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>Nothing to record. A node that decomposed and did no checking of its own.</summary>
    public static NodeOutcome Nothing { get; } = new();
}

/// <summary>Runs one node. Anything can implement it — an agent job, a shell check, a person.</summary>
public delegate Task<NodeOutcome> PlanNodeRunner(PlanNodeContext context, CancellationToken ct);

/// <summary>
/// Applies a shape-valid candidate to the world the plan is about, and says whether it was even
/// understood.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never sees a null or empty candidate</b> — the shape gate rejects those before this is ever
/// called, and nothing is applied on their account.
/// </para>
/// <para>
/// <b>May still find the candidate unusable, and that is also a shape fact, never a verdict on the
/// work.</b> "This does not read as an operation I recognise" is a fair rejection; "that place does
/// not exist" is not (S8) — the candidate reaches the world regardless of whether its target is
/// real, and what the world says about it becomes ordinary evidence for the acceptance gate to find
/// on its own next read, never something this delegate decides by refusing to apply it.
/// </para>
/// </remarks>
public delegate Task<PlanApplication> PlanCandidateApplier(
    Operation candidate, PlanNodeContext context, CancellationToken ct);

/// <summary>Whether a candidate was understood well enough to act on, and what to say if not.</summary>
public sealed record PlanApplication
{
    /// <summary>Whether the candidate parsed into a real operation and was applied.</summary>
    public required bool Applied { get; init; }

    /// <summary>
    /// Why not, when it was not — mechanical, about the message's shape, never about whether the work
    /// will succeed. Carried verbatim into the next attempt's <see cref="NodeRejection"/>.
    /// </summary>
    public string? Finding { get; init; }

    /// <summary>The candidate was understood and applied.</summary>
    public static PlanApplication Ok() => new() { Applied = true };

    /// <summary>The candidate could not be understood as an operation at all — form, never referents.</summary>
    public static PlanApplication Rejected(string finding) => new() { Applied = false, Finding = finding };
}

/// <summary>What one pass over a plan did.</summary>
/// <remarks>
/// <para>
/// <b>Only what the tree cannot answer.</b> Anything derivable from <see cref="Tree"/> is asked of
/// the tree — a second copy is a second source of truth, and it is wrong from the moment anything
/// re-rules. That is the same refusal a node makes about its own status, and it applies here for the
/// same reason.
/// </para>
/// <para>
/// So: a node's lifecycle and outcome come from <see cref="PlanTree.LifecycleOf"/> and
/// <see cref="PlanTree.OutcomeOf"/>, and drift from
/// <see cref="PlanTree.StaleNodes"/>. What is left are the three things that are genuinely facts
/// about <em>this invocation</em> and are recorded nowhere else, because the tree keeps one reading
/// per node rather than a log of them.
/// </para>
/// </remarks>
public sealed record PlanRunResult
{
    /// <summary>The tree as it stands after the pass, verdicts written back.</summary>
    public required PlanTree Tree { get; init; }

    /// <summary>Nodes that ran during this pass, in order.</summary>
    public required IReadOnlyList<string> Executed { get; init; }

    /// <summary>Nodes skipped because the tree already recorded them satisfied.</summary>
    public required IReadOnlyList<string> Skipped { get; init; }

    /// <summary>
    /// What the verifier ruled for each node that ran, when one was configured. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// A pass fact rather than tree state on purpose: the tree records the <em>verdicts</em>, which
    /// are what the ruling was prepared to stand behind. What it <b>abstained</b> on is not in the
    /// tree, because nothing was decided — and that absence is exactly what would be invisible if it
    /// were not reported here.
    /// </remarks>
    public required IReadOnlyDictionary<string, Verification> Rulings { get; init; }

    /// <summary>The node that reported a contradiction and stopped this pass, if one did.</summary>
    /// <remarks>
    /// Not the same question as "which nodes have an outstanding dispute" — the tree answers that,
    /// and it keeps answering it on later passes that do not halt. This says where <em>this</em> pass
    /// stopped.
    /// </remarks>
    public string? HaltedAt { get; init; }

    /// <summary>What the verdicts say about the root after the pass. Derived, never stored.</summary>
    public ContractOutcome RootOutcome => Tree.OutcomeOf(Tree.Current.RootId);

    /// <summary>How the run ended, in four words a reader needs no vocabulary for.</summary>
    /// <remarks>
    /// <para>
    /// <b>Generic on purpose, with the specifics left where they already are.</b> A run stopped
    /// because a constraint's owner must answer is <see cref="PlanRunOutcome.Blocked"/> carrying a
    /// referral; one stopped because it spent its changes of plan is <see cref="PlanRunOutcome.Blocked"/>
    /// carrying that reason. Neither needed a value of its own, and the rule is worth stating: <b>if
    /// telling two endings apart needs another enum value, it needs a better record instead.</b>
    /// </para>
    /// <para>
    /// <b><see cref="PlanRunOutcome.Faulted"/> is read off the tree, not off a pass field.</b> A node
    /// that threw carries its own <see cref="PlanNode.Failure"/> — a durable, mechanical fact, unlike
    /// the retired <c>PlanHaltCause</c> it replaces here — so whether the halted node has one is the
    /// whole test.
    /// </para>
    /// </remarks>
    public PlanRunOutcome Outcome =>
        Tree.LifecycleOf(Tree.Current.RootId) is NodeLifecycle.Abandoned ? PlanRunOutcome.Abandoned
        : RootOutcome is ContractOutcome.Met ? PlanRunOutcome.Completed
        : HaltedAt is { } haltedAt && Tree.Node(haltedAt).Failure is not null ? PlanRunOutcome.Faulted
        : PlanRunOutcome.Blocked;
}

/// <summary>How a run of a plan ended.</summary>
/// <remarks>
/// <b>The thing a run never reported.</b> Until now the end of a run was read from the root node's
/// derived status — the tree's arithmetic — so a plan that knowingly gave something up and a plan
/// that ran out of room printed the same word, and neither of them said whether anybody was waiting
/// on an answer.
/// </remarks>
public enum PlanRunOutcome
{
    /// <summary>The run stopped and something outside it has to move before it can go on.</summary>
    Blocked = 0,

    /// <summary>Every contract is met.</summary>
    Completed,

    /// <summary>The work was given up.</summary>
    Abandoned,

    /// <summary>An attempt died and nothing recovered it.</summary>
    Faulted
}

/// <summary>
/// Walks a plan: children in order, then the node that decomposed them, writing every verdict back
/// into the durable tree as it goes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Post-order, and that is the point.</b> A node's own criteria are checked after its children
/// have run, because verification cannot happen below the altitude that decided: no leaf-level check
/// proves that a commitment made higher up still holds. The evidence has to climb back to the level
/// that authored the criterion.
/// </para>
/// <para>
/// <b>Sequential, one node at a time.</b> The failure this avoids is concrete: two nodes working the
/// same area interleave into a result neither of them verified, and no scheme for sharing capacity
/// helps, because what is contended is the artifact and not the window.
/// </para>
/// <para>
/// <b>The tree is reloaded before every node and saved after every node.</b> That is what makes the
/// read a real read: a node's view is built from what the store holds, not from anything the
/// executor is carrying in a local. It is also what makes a half-finished run resumable — rerunning
/// skips whatever the tree already records as satisfied.
/// </para>
/// <para>
/// <b>A reported contradiction stops the pass and is returned, not resolved.</b> Deciding whether the
/// plan should change belongs to whoever authored the criterion; this type has no opinion and does
/// not re-rule on anyone's behalf.
/// </para>
/// <para>
/// <b>With an verifier configured, the node stops grading its own work.</b> Its outcome becomes a
/// report — what it observed — and the verifier's verdicts are what reach the tree. Without one,
/// the node's own verdicts are recorded, which is the behaviour every existing caller has.
/// </para>
/// <para>
/// <b>A node that has used up its declared attempts is not run again, and the pass stops there.</b>
/// The bound is enforced from out here rather than by the node, for the same reason the ruling is: a
/// node is not in a position to decide it has had enough goes. It is a halt rather than a skip
/// because the two say opposite things — one is work that is done, the other work that is stuck —
/// and only a halt reaches whoever could do something about it.
/// </para>
/// </remarks>
public sealed class PlanExecutor(
    IPlanTreeStore store,
    TimeProvider? timeProvider = null,
    int? projectionTokenBudget = null,
    IVerifier? verifier = null,
    PlanCandidateApplier? applier = null,
    PlanRetryPolicy? retries = null,
    OperationCatalog? operations = null)
{
    private readonly PlanRetryPolicy _retries = retries ?? PlanRetryPolicy.Default;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Runs every unsatisfied node of <paramref name="planId"/>.</summary>
    public async Task<PlanRunResult> ExecuteAsync(
        string planId, PlanNodeRunner runner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runner);

        var tree = await store.LoadAsync(planId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No plan '{planId}' in the store.");

        // A pass that is starting has nothing running yet, so an in-flight mark still on the tree is
        // a run that died mid-step. Corrected here rather than inherited: a node reading Running
        // forever is worse than one that never reported it.
        if (tree.ClearAttempts() is var reconciled && !ReferenceEquals(reconciled, tree))
        {
            tree = reconciled;
            await store.SaveAsync(tree, ct).ConfigureAwait(false);
        }

        var executed = new List<string>();
        var skipped = new List<string>();
        var rulings = new Dictionary<string, Verification>(StringComparer.Ordinal);
        string? haltedAt = null;

        // The executor does not know whether it is inside a workflow, and does not need to:
        // reporting with nothing scoped is a no-op, so the same executor works under RunAsync,
        // under StreamAsync, and called directly by a consumer with no workflow at all.
        var (workflowName, executionId) = PlanEvent.Identity();
        ValueTask Report(PlanEvent evt) => WorkflowEventReporting.ReportAsync(evt, ct);

        foreach (var nodeId in PostOrder(tree, tree.Current.RootId))
        {
            ct.ThrowIfCancellationRequested();

            // Reloaded each time: the node's view has to come from the store, or the executor has
            // quietly become the channel the tree is supposed to be.
            tree = await store.LoadAsync(planId, ct).ConfigureAwait(false)!;

            if (tree!.Settled(nodeId))
            {
                skipped.Add(nodeId);
                continue;
            }

            var projection = PlanTreeProjection.Project(tree, nodeId, projectionTokenBudget);
            var node = tree.Node(nodeId);

            await Report(new PlanNodeStarted
            {
                WorkflowName = workflowName,
                ExecutionId = executionId,
                PlanId = planId,
                PlanVersion = tree.Current.Number,
                NodeId = nodeId,
                Goal = node.Contract.Goal,
                ProjectedTokens = projection.EstimatedTokens,
                OmittedAncestors = projection.OmittedAncestors,
                OmittedRecords = projection.OmittedVerdicts
            }).ConfigureAwait(false);

            // Written before the attempt and saved, so anything reading the store while this runs —
            // somebody looking at a plan that has been paused for days — can see which step is in
            // flight. Cleared on both exits below, and by the next pass if neither is reached.
            tree = tree.WithAttempt(nodeId, _time.GetUtcNow());
            await store.SaveAsync(tree, ct).ConfigureAwait(false);

            var nodeContext = new PlanNodeContext
            {
                PlanId = planId,
                PlanVersion = tree.Current.Number,
                Node = node,
                Contract = node.Contract,
                TreeView = projection.Text,
                Projection = projection
            };

            NodeOutcome outcome;

            try
            {
                outcome = await ProposeAsync(runner, nodeContext, ct).ConfigureAwait(false);
            }
            // A node that throws is a node that stopped, not a run that ended. Left unguarded, a
            // provider outage or a tool fault propagates out of the plan and faults the whole
            // workflow — so a plan could record a step that disputed, and could only ever *die* of
            // one that failed. Cancellation is not a failure and travels on untouched.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                tree = await store.LoadAsync(planId, ct).ConfigureAwait(false) ?? tree;

                // No verdict is written. A verdict claims something was checked, and nothing was.
                // Classified here because this is the last place holding the exception itself. A
                // depleted account and a busy one arrive as the same status code, and only the
                // provider's own words separate them — so the reading is taken once, recorded as a
                // durable fact about the failure, and never re-derived from prose downstream.
                var terminal = TerminalProviderError.Is(ex);

                tree = tree!.ClearAttempt(nodeId).WithFailure(nodeId, new NodeFailure
                {
                    Message = terminal
                        ? $"could not be run, and waiting will not fix it: {ex.Message}"
                        : ex.Message,
                    Terminal = terminal,
                    At = _time.GetUtcNow()
                });

                await store.SaveAsync(tree, ct).ConfigureAwait(false);

                executed.Add(nodeId);
                haltedAt = nodeId;

                await Report(new PlanNodeFailed
                {
                    WorkflowName = workflowName,
                    ExecutionId = executionId,
                    PlanId = planId,
                    PlanVersion = tree.Current.Number,
                    NodeId = nodeId,
                    Message = ex.Message,
                    Terminal = terminal
                }).ConfigureAwait(false);

                break;
            }

            // What a node said it did, reported for every runner shape rather than only for the one
            // that happens to be an agent. A step that is a program, a command or a person has an
            // account of itself too, and a narration missing it reads as a plan that did nothing.
            if (outcome.Summary is { Length: > 0 } summary)
            {
                await Report(new PlanNodeReported
                {
                    WorkflowName = workflowName,
                    ExecutionId = executionId,
                    PlanId = planId,
                    PlanVersion = tree!.Current.Number,
                    NodeId = nodeId,
                    Summary = summary
                }).ConfigureAwait(false);
            }

            executed.Add(nodeId);

            // Re-read before applying, not only before projecting. A runner may itself write to the
            // tree while it works — an external verifier recording a verdict is the case this
            // design expects — and applying the outcome to the snapshot taken *before* the node ran
            // would overwrite whatever it wrote. Half a measure here is worse than none: it looks
            // like the store is the channel while quietly making the executor one.
            tree = await store.LoadAsync(planId, ct).ConfigureAwait(false) ?? tree;

            // Written whether or not the node produced anything. A node that ran, read its
            // surroundings and had nothing to add is otherwise indistinguishable from one that
            // never ran — and the difference is the whole of "has this been checked against the
            // plan as it now stands".
            tree = tree!.ClearAttempt(nodeId).WithReading(nodeId, new NodeReading
            {
                At = _time.GetUtcNow(),
                PlanVersion = tree.Current.Number,
                ProjectedTokens = projection.EstimatedTokens,
                OmittedAncestors = projection.OmittedAncestors,
                OmittedRecords = projection.OmittedVerdicts,
                Summary = string.IsNullOrWhiteSpace(outcome.Summary) ? null : outcome.Summary
            });

            // An attempt that finished is the direct answer to one that did not. A dispute is not
            // cleared this way, and the asymmetry is the point: that is a claim about the contract,
            // and this is a claim about a run.
            tree = tree.ClearFailure(nodeId);

            // A question and a state left by an earlier attempt are replaced by what this attempt reports.
            tree = tree.ClearQuestion(nodeId).WithState(nodeId, StepState.Pending);

            var ruling = verifier is null
                ? null
                : await verifier.VerifyAsync(
                    new VerificationRequest
                    {
                        Node = tree!.Node(nodeId),

                        // The whole record, not the node's view of it. A projection is budgeted for
                        // the thing doing the work — it omits what will not fit — and an verifier
                        // reading that is ruling on what the node happened to be shown. Found live:
                        // a reviewer asked whether every step had been ruled on could not tell,
                        // because seven records were missing from the view it inherited.
                        TreeView = PlanTreeProjection.Project(tree, nodeId).Text
                    },
                    ct).ConfigureAwait(false);

            // With an verifier, the node's own verdicts are a report and go no further; the
            // ruling is what the tree records. Without one, nothing has changed for existing callers.
            foreach (var verdict in ruling?.Verdicts ?? outcome.Verdicts)
                tree = tree!.WithVerdict(nodeId, verdict);

            // A refuted dispute is one the node was mistaken about, so it does not travel and does
            // not stop the pass. A dispute reaches a node only through DisputeAsync now — an external
            // call, never a node's own report — so what is left to do here is read it back.
            if (ruling?.Outcome is VerificationOutcome.ViolationRefuted)
                tree = tree!.ClearViolation(nodeId);
            else if (tree!.Node(nodeId).Violation is not null)
                haltedAt = nodeId;

            // A gate a check ruled against reaches the supervisor even when the node reported
            // nothing at all — the loop may pass evidence through or escalate it, never decide
            // for itself that a failing gate is fine to run past.
            if (haltedAt is null && ruling?.Outcome is VerificationOutcome.GateFailed)
                haltedAt = nodeId;

            if (ruling is not null)
            {
                rulings[nodeId] = ruling;

                await Report(new PlanNodeVerified
                {
                    WorkflowName = workflowName,
                    ExecutionId = executionId,
                    PlanId = planId,
                    PlanVersion = tree!.Current.Number,
                    NodeId = nodeId,
                    Outcome = ruling.Outcome,
                    Abstained = ruling.Abstained,
                    Score = ruling.Score
                }).ConfigureAwait(false);
            }

            // What the step reported marks its state. Done with one option, and nothing halted here:
            // that option is the result. Done with several, or not done: the pass stops at this step,
            // and the options are left where whoever chooses reads them. A step that reported neither
            // is left Pending, with its verdicts as its only record.
            NodeQuestion? asking = null;
            var blocked = false;

            if (!outcome.Done || outcome.Options.Count > 0)
            {
                if (haltedAt is null && outcome.Done && outcome.Options.Count == 1)
                {
                    tree = tree!.WithState(nodeId, StepState.Done, outcome.Options[0]);
                }
                else if (haltedAt is null || haltedAt == nodeId)
                {
                    var met = haltedAt is null && outcome.Done;
                    haltedAt = nodeId;
                    blocked = true;
                    tree = tree!.WithState(nodeId, StepState.Blocked);

                    if (outcome.Options.Count > 0)
                    {
                        asking = new NodeQuestion
                        {
                            Asks = !string.IsNullOrWhiteSpace(outcome.Summary) ? outcome.Summary
                                : met ? $"'{nodeId}' found more than one way to do it."
                                : $"'{nodeId}' could not be done as asked.",
                            Options = outcome.Options,
                            Done = met,
                            At = _time.GetUtcNow()
                        };

                        tree = tree.WithQuestion(nodeId, asking);
                    }
                }
            }

            await store.SaveAsync(tree!, ct).ConfigureAwait(false);

            if (blocked)
            {
                await Report(new PlanNodeBlocked
                {
                    WorkflowName = workflowName,
                    ExecutionId = executionId,
                    PlanId = planId,
                    PlanVersion = tree!.Current.Number,
                    NodeId = nodeId,
                    Asks = asking?.Asks
                        ?? (!string.IsNullOrWhiteSpace(outcome.Summary)
                            ? outcome.Summary
                            : $"'{nodeId}' could not be done as asked."),
                    Options = asking?.Options ?? []
                }).ConfigureAwait(false);
            }

            if (haltedAt is not null)
                break;
        }

        var result = new PlanRunResult
        {
            Tree = tree!,
            Executed = executed,
            Skipped = skipped,
            Rulings = rulings,
            HaltedAt = haltedAt
        };

        await Report(new PlanPassCompleted
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = planId,
            PlanVersion = result.Tree.Current.Number,
            Executed = executed,
            Skipped = skipped,
            HaltedAt = haltedAt,
            RootOutcome = result.RootOutcome
        }).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// A candidate never became usable — the shape gate rejected every attempt this pass allows.
    /// </summary>
    /// <remarks>
    /// Deliberately just an <see cref="Exception"/>, caught by the same clause that already catches a
    /// runner throwing outright: the executor produced nothing to evaluate either way, so nothing is
    /// applied, nothing is verified, and the node's record reads the same kind of fact — an attempt
    /// that did not finish — as any other failure would.
    /// </remarks>
    private sealed class PlanShapeRejectedException(string finding) : Exception(finding);

    /// <summary>
    /// Every attempt this pass allowed died, and none of them died of something waiting cannot fix.
    /// </summary>
    /// <remarks>
    /// The last failure travels as the inner exception rather than only in the message, so anything
    /// classifying it afterwards — <see cref="TerminalProviderError"/> walks the chain — reads what
    /// actually happened rather than this type's summary of it.
    /// </remarks>
    private sealed class PlanAttemptsExhaustedException(string finding, Exception last)
        : Exception(finding, last);

    /// <summary>
    /// Runs <paramref name="runner"/>, retrying mechanically while its candidate fails the shape
    /// gate, and throwing once it gives up — caught by <see cref="ExecuteAsync"/> exactly like any
    /// other attempt that produced nothing to evaluate (S0, S9's same family of thing).
    /// </summary>
    /// <remarks>
    /// <b>Every retry carries exactly the three things §2.3 allows, and nothing this loop wrote.</b>
    /// The contract is already pinned into <paramref name="context"/>; the rejected candidate and the
    /// finding travel in <see cref="PlanNodeContext.Rejection"/>, verbatim, because the runner is
    /// stateless between attempts and the tree does not remember shape failures — only settled facts.
    /// </remarks>
    private async Task<NodeOutcome> ProposeAsync(
        PlanNodeRunner runner, PlanNodeContext context, CancellationToken ct)
    {
        NodeRejection? rejection = null;

        for (var attempt = 1; ; attempt++)
        {
            NodeOutcome outcome;

            try
            {
                outcome = await runner(context with { Rejection = rejection }, ct).ConfigureAwait(false);
            }
            // A provider that fell over produced no response, which is the same fact as a reply that
            // would not parse — so it is retried by the same budget, mechanically, re-issuing the
            // contract and nothing this loop wrote. Cancellation is not a failure and travels on.
            //
            // A terminal error is never retried and is not what the budget is for: waiting cannot fix
            // an account that is out of allowance, and re-issuing into one spends calls that were
            // never going to succeed against a limit somebody set on purpose.
            catch (Exception ex) when (ex is not OperationCanceledException
                                       && attempt < _retries.Attempts
                                       && !TerminalProviderError.Is(ex))
            {
                // Nothing to hand back: an attempt that died produced no candidate, and showing it
                // something it said two attempts ago as though it were the last thing it said would
                // be the loop composing a history rather than carrying one.
                rejection = null;
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       && attempt > 1
                                       && !TerminalProviderError.Is(ex))
            {
                // Out of attempts. It becomes an ordinary finding naming what was tried and what the
                // last one said — the count lives in this sentence and nowhere a plan can read it.
                throw new PlanAttemptsExhaustedException(
                    $"could not be run: {attempt} attempts, {ex.Message}", ex);
            }

            // No candidate at all is not a shape failure. Plenty of nodes — anything that only
            // decomposes — have nothing to propose, and an applier is never consulted about work
            // nobody offered. Nor is there a gate to fail if nothing was ever configured to apply one.
            // A step that could not do its task proposes nothing, whatever it left in the operation
            // its reply had to carry, so there is nothing to admit or apply.
            if (!outcome.Done)
                return outcome with { Candidate = null };

            if (applier is null || outcome.Candidate is null)
                return outcome;

            // Empty never reaches the applier: nothing was proposed for it to look at, so there is
            // nothing to apply and no call to make.
            // Admitted before applied. The catalog rules on whether this is an operation at all —
            // the name, the arity, and whatever the domain can say about the arguments without
            // consulting the world. An applier reached without it is one whose first line indexes
            // arguments nothing has counted.
            string? finding = operations?.Admits(outcome.Candidate);

            if (finding is null && !outcome.Candidate.IsNamed)
            {
                finding = "the response named no operation.";
            }
            else if (finding is null)
            {
                var application = await applier(outcome.Candidate, context, ct).ConfigureAwait(false);

                if (application.Applied)
                    return outcome;

                finding = application.Finding ?? "the candidate could not be understood as an operation.";
            }

            if (attempt >= _retries.Attempts)
                throw new PlanShapeRejectedException(finding);

            rejection = new NodeRejection(outcome.Candidate, finding);
        }
    }

    /// <summary>
    /// Repeats passes until the plan stops changing, and returns the last one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The stop condition is derived from the tree, not counted.</b> A pass that leaves every
    /// node's status and every standing verdict exactly as it found them has made no progress, and a
    /// further pass has nothing new to work from — that <em>is</em> non-convergence, and it needs no
    /// retry ceiling to notice. A ceiling would be a second source of truth about whether the work is
    /// still moving, and it would be wrong for every plan except the one it was tuned against.
    /// </para>
    /// <para>
    /// <b>A state already seen ends the run, not only the state immediately before.</b> Work that
    /// oscillates — a fix that breaks what the previous fix repaired, then repairs it again — never
    /// leaves two consecutive passes identical, and comparing only against the last one would loop on
    /// it forever. Returning to any earlier state is the same finding: the plan is going round rather
    /// than forward.
    /// </para>
    /// <para>
    /// <b>Nothing here overrides the executor.</b> Iteration bounds declared on the contracts are
    /// enforced where they already were, and a node that has used up its attempts halts the pass, so
    /// this returns rather than passing again over work that cannot change. A reported contradiction
    /// still halts the pass and is returned unresolved; deciding whether the plan should change
    /// belongs to whoever authored the criterion, and repeating the pass would only re-report it.
    /// </para>
    /// <para>
    /// Read the outcome from the result the way you would read a single pass:
    /// <see cref="PlanRunResult.Outcome"/> says how it ended in one word, and
    /// <see cref="PlanRunResult.HaltedAt"/> names the node that stopped it. The word is deliberately
    /// generic: what makes one <c>Blocked</c> run different from another is in the halt and the tree,
    /// not in a value of its own.
    /// </para>
    /// <para>
    /// <see cref="ExecuteAsync"/> remains the single-pass entry point. A caller that wants to do its
    /// own work between passes — re-rule a node, ask a person, run a build — needs the passes
    /// separated, and that is a real case rather than a lower-level detail.
    /// </para>
    /// </remarks>
    public async Task<PlanRunResult> ExecuteToCompletionAsync(
        string planId, PlanNodeRunner runner, CancellationToken ct = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var result = await ExecuteAsync(planId, runner, ct).ConfigureAwait(false);
        seen.Add(Decided(result.Tree));

        while (result.HaltedAt is null && result.RootOutcome is not ContractOutcome.Met)
        {
            result = await ExecuteAsync(planId, runner, ct).ConfigureAwait(false);

            if (!seen.Add(Decided(result.Tree)))
                break;
        }

        return result;
    }

    /// <summary>
    /// Everything the tree currently holds as decided, rendered so two passes can be compared.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes <see cref="PlanNode.ReadCount"/>, <see cref="PlanNode.LastRead"/> and the
    /// lifecycle. All change on every pass by design — a node that ran and had nothing to add still
    /// records that it ran — so including them would make every pass look like progress and the run
    /// would never stop. What counts as progress is what was <em>decided</em>: the plan version in
    /// force, each node's contract outcome, and the verdicts that currently stand.
    /// </remarks>
    private static string Decided(PlanTree tree)
    {
        var state = new StringBuilder().Append(tree.Current.Number);

        foreach (var nodeId in tree.Current.Nodes.Keys.Order(StringComparer.Ordinal))
        {
            state.Append('|').Append(nodeId).Append(':').Append(tree.OutcomeOf(nodeId));

            foreach (var verdict in tree.Node(nodeId).LatestVerdicts)
                state.Append(';').Append(verdict.Criterion).Append('=').Append(verdict.Passed ? '1' : '0');
        }

        return state.ToString();
    }

    /// <summary>Records what a node was told, saves it, and says so. Mints no version.</summary>
    /// <remarks>
    /// <b>No version, because nothing about the plan changed.</b> The contract it was given is the
    /// contract it still has; what changed is that a choice inside the work has been made, by somebody
    /// entitled to make it. The next attempt reads it off the node.
    /// </remarks>
    public async Task<PlanTree> AnswerAsync(
        string planId, string nodeId, string answer, string by, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var tree = await store.LoadAsync(planId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No plan '{planId}' in the store.");

        var question = tree.Node(nodeId).Question
            ?? throw new InvalidOperationException(
                $"'{nodeId}' was answered \"{answer}\" and is not waiting to be told anything.");

        var asked = question.Asks;

        var answered = tree.WithAnswer(nodeId, new PlanAnswer
        {
            Asked = asked,
            Answer = answer,
            By = by,
            At = _time.GetUtcNow()
        });

        // Choosing one of the options a step left after doing its task settles the step on it. Any
        // other answer — to a question the step raised itself, or to one it left after it could not
        // do its task — leaves the step to be attempted again.
        answered = question.Done
            && question.Options.FirstOrDefault(o => string.Equals(o.Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase))
                is { } chosen
            ? answered.WithState(nodeId, StepState.Done, chosen)
            : answered.WithState(nodeId, StepState.Pending);

        await store.SaveAsync(answered, ct).ConfigureAwait(false);

        var (workflowName, executionId) = PlanEvent.Identity();

        await WorkflowEventReporting.ReportAsync(new PlanNodeAnswered
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = answered.PlanId,
            PlanVersion = answered.Current.Number,
            NodeId = nodeId,
            Asked = asked,
            Answer = answer,
            By = by
        }, ct).ConfigureAwait(false);

        return answered;
    }

    /// <summary>
    /// Records a term somebody settled, saves it, and says so. Mints no version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named for what it does rather than for where it is kept.</b> The plan tier keeps terms on
    /// the tree — the run's own durable state — because a term is scoped to this run and the tree is
    /// what survives a pause, a process death and every version after it. Nothing here reaches an
    /// experience store: cross-run recall is a different question with a different lifetime, and it
    /// is deliberately not what this answers.
    /// </para>
    /// <para>
    /// <b>Both sentences travel.</b> <paramref name="said"/> is what somebody actually wrote and
    /// <paramref name="reading"/> is what the supervisor made of it — kept apart, because everything
    /// afterwards binds the reading and a reader has to be able to check it against the words.
    /// </para>
    /// </remarks>
    public async Task<PlanTree> CommitAsync(
        string planId,
        string reading,
        string said,
        string by,
        string? termId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reading);
        ArgumentException.ThrowIfNullOrWhiteSpace(said);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var tree = await store.LoadAsync(planId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No plan '{planId}' in the store.");

        var term = new PlanTerm
        {
            Id = string.IsNullOrWhiteSpace(termId) ? Guid.NewGuid().ToString("n")[..8] : termId!,
            Reading = reading,
            Said = said,
            By = by,
            At = _time.GetUtcNow()
        };

        var committed = tree.WithTerm(term);
        await store.SaveAsync(committed, ct).ConfigureAwait(false);

        var (workflowName, executionId) = PlanEvent.Identity();

        await WorkflowEventReporting.ReportAsync(new PlanTermCommitted
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = committed.PlanId,
            PlanVersion = committed.Current.Number,
            TermId = term.Id,
            Reading = term.Reading,
            Said = term.Said,
            By = term.By
        }, ct).ConfigureAwait(false);

        return committed;
    }

    /// <summary>Revokes a term, saves it, and says so. Mints no version, and removes nothing.</summary>
    /// <remarks>
    /// <b><paramref name="end"/> is required because the two meanings are opposite.</b>
    /// <see cref="PlanTermEnd.Retracted"/> says somebody wants something else now, and is true from
    /// here on; <see cref="PlanTermEnd.Waived"/> says this plan could not hold it, which is a fact
    /// about one run and no evidence at all about the next. Anything carrying terms beyond this run
    /// has to know which happened — and a default would silently pick one.
    /// </remarks>
    public async Task<PlanTree> ContradictAsync(
        string planId,
        string termId,
        string reason,
        PlanTermEnd end,
        string by,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var tree = await store.LoadAsync(planId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No plan '{planId}' in the store.");

        var revoked = tree.Contradict(termId, reason, end, by, _time.GetUtcNow());
        await store.SaveAsync(revoked, ct).ConfigureAwait(false);

        var (workflowName, executionId) = PlanEvent.Identity();

        await WorkflowEventReporting.ReportAsync(new PlanTermContradicted
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = revoked.PlanId,
            PlanVersion = revoked.Current.Number,
            TermId = termId,
            Reading = revoked.Terms.First(t => string.Equals(t.Id, termId, StringComparison.Ordinal)).Reading,
            Reason = reason,
            End = end,
            By = by
        }, ct).ConfigureAwait(false);

        return revoked;
    }

    /// <summary>Records that a step is given up, saves it, and says so. Mints no version.</summary>
    /// <remarks>
    /// <b>No version, because the plan was not re-authored.</b> A version marks the plan <em>changing</em>;
    /// this marks somebody deciding not to have a part of it, which the node itself now carries. The
    /// event is what makes it visible, and what a consumer releases whatever the step was holding from.
    /// </remarks>
    public async Task<PlanTree> AbandonAsync(
        string planId, string nodeId, string reason, string by, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);

        var tree = await store.LoadAsync(planId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No plan '{planId}' in the store.");

        var abandoned = tree.Abandon(
            nodeId, new PlanAbandonment { Reason = reason, By = by, At = _time.GetUtcNow() });

        await store.SaveAsync(abandoned, ct).ConfigureAwait(false);

        var (workflowName, executionId) = PlanEvent.Identity();

        await WorkflowEventReporting.ReportAsync(new PlanStepAbandoned
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = abandoned.PlanId,
            PlanVersion = abandoned.Current.Number,
            NodeId = nodeId,
            Reason = reason,
            By = by
        }, ct).ConfigureAwait(false);

        return abandoned;
    }

    /// <summary>Records that a node is waiting to be told something, saves it, and says so.</summary>
    /// <remarks>
    /// <b>No version, because nothing about the plan changed.</b> A halt raised this way is a decision
    /// that is available and is not the process's to take — the same shape as a node reaching a choice
    /// it may not make alone — so it travels exactly the way that halt does: through the store, not
    /// only through workflow state.
    /// </remarks>
    public async Task<PlanTree> AskAsync(
        string planId, string nodeId, string asks,
        IReadOnlyList<string>? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asks);

        var tree = await store.LoadAsync(planId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No plan '{planId}' in the store.");

        var asking = tree.WithQuestion(
                nodeId, new NodeQuestion { Asks = asks, Options = options ?? [], At = _time.GetUtcNow() })
            .WithState(nodeId, StepState.Blocked);

        await store.SaveAsync(asking, ct).ConfigureAwait(false);

        var (workflowName, executionId) = PlanEvent.Identity();

        await WorkflowEventReporting.ReportAsync(new PlanNodeBlocked
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = asking.PlanId,
            PlanVersion = asking.Current.Number,
            NodeId = nodeId,
            Asks = asks,
            Options = options ?? []
        }, ct).ConfigureAwait(false);

        return asking;
    }

    /// <summary>Records a dispute against a node's contract, saves it, and says so.</summary>
    /// <remarks>No version: a dispute is a claim that the contract is wrong, not a replacement for it.
    /// The contract changes only when something re-rules the node.</remarks>
    public async Task<PlanTree> DisputeAsync(
        string planId, string nodeId, string criterion, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(criterion);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var tree = await store.LoadAsync(planId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No plan '{planId}' in the store.");

        var disputed = tree.WithViolation(
            nodeId, new PlanViolation { Criterion = criterion, Reason = reason, At = _time.GetUtcNow() });

        await store.SaveAsync(disputed, ct).ConfigureAwait(false);

        var (workflowName, executionId) = PlanEvent.Identity();

        await WorkflowEventReporting.ReportAsync(new PlanNodeDisputed
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = disputed.PlanId,
            PlanVersion = disputed.Current.Number,
            NodeId = nodeId,
            Criterion = criterion,
            Reason = reason
        }, ct).ConfigureAwait(false);

        return disputed;
    }

    /// <summary>
    /// Re-rules a node's contract, mints a plan version recording why, and saves it.
    /// </summary>
    /// <remarks>
    /// The only thing in this type that mints a version. Recording verdicts does not, and neither
    /// does reporting a contradiction: a version marks the plan <em>changing</em>, and if every fact
    /// about the work minted one the lineage would be churn rather than a history of decisions.
    /// </remarks>
    public async Task<PlanTree> ReruleAsync(
        string planId,
        string nodeId,
        AgentContract contract,
        string reason,
        IEnumerable<AuthoredStep>? children = null,
        PlanRationale? rationale = null,
        CancellationToken ct = default)
    {
        var tree = await store.LoadAsync(planId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No plan '{planId}' in the store.");

        // Read once, here. Everything below asks the replacement children two questions — what the
        // new version holds, and what nothing can decide about it — and a caller is entitled to hand
        // this in as a query that only enumerates once.
        var replacement = children?.Select(c => (c.Id, c.Contract)).ToList();

        var reruled = tree.Rerule(nodeId, contract, reason, replacement, _time, rationale);
        await store.SaveAsync(reruled, ct).ConfigureAwait(false);

        var (workflowName, executionId) = PlanEvent.Identity();

        await WorkflowEventReporting.ReportAsync(new PlanVersionMinted
        {
            WorkflowName = workflowName,
            ExecutionId = executionId,
            PlanId = reruled.PlanId,
            PlanVersion = reruled.Current.Number,
            ReRuledNodeId = nodeId,
            Reason = reason,
            Rationale = rationale,
            DroppedNodeIds = reruled.Current.DroppedNodeIds,
            CriteriaNothingCanDecide = CannotDecide(contract, replacement)
        }, ct).ConfigureAwait(false);

        return reruled;
    }

    /// <summary>
    /// The gates this re-ruling authored that nothing configured can decide, or <see langword="null"/>
    /// when nobody could say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asked of the configured verifier</b> — the one that will have to rule on these criteria
    /// — because decidability is a fact about what is wired up, not about the words. Nothing is run:
    /// the answer is free or it is <see langword="null"/>.
    /// </para>
    /// <para>
    /// <b>Scoped to what the re-ruling wrote</b>, not to the whole new version. Whether the rest of
    /// the plan was gated is not what just changed, and reporting it here would bury the criteria
    /// somebody has just authored in the ones they left alone.
    /// </para>
    /// </remarks>
    private IReadOnlyList<string>? CannotDecide(
        AgentContract contract, IReadOnlyList<(string Id, AgentContract Contract)>? children)
    {
        if (verifier is null)
            return null;

        var authored = new List<string>(contract.AcceptanceCriteria);

        foreach (var (_, childContract) in children ?? [])
            authored.AddRange(childContract.AcceptanceCriteria);

        // Deduped: the same criterion on two nodes is one thing nothing can decide, and counting it
        // twice would make the report read worse than the plan is.
        return verifier.CannotDecide([.. authored.Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>Children in order, then the node itself.</summary>
    private static IEnumerable<string> PostOrder(PlanTree tree, string nodeId)
    {
        foreach (var childId in tree.Current.Nodes[nodeId].ChildIds)
        {
            foreach (var deeper in PostOrder(tree, childId))
                yield return deeper;
        }

        yield return nodeId;
    }
}
