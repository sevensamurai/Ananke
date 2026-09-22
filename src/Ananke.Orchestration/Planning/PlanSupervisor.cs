using System.Text.Json.Serialization;
using Ananke.Orchestration.Agents.Context;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// Where a supervised plan stands between passes, and what was decided about it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is workflow state, not a message.</b> A plan that can change is a loop — run, decide,
/// run again — and a loop in this framework is jobs and <c>Loop</c>, so what travels between them
/// travels the way everything else does. Holding it in state is what makes a change of plan
/// checkpointable, interruptible and visible in the run's history rather than an invisible round
/// trip inside one job.
/// </para>
/// <para>
/// <b>Nothing here is stored that <see cref="Result"/> can answer.</b> The halted node, its dispute
/// and its verdicts are read back out of the tree rather than copied beside it, for the same reason
/// a node carries no status field: a second copy is a second thing that can be stale.
/// </para>
/// </remarks>
public sealed record PlanCoordination
{
    /// <summary>How the last pass ended.</summary>
    public required PlanRunResult Result { get; init; }

    /// <summary>
    /// What the coordinator decided about that pass, or <see langword="null"/> before it was asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cleared when a new pass records its result, so a decision never outlives the halt it answered.
    /// </para>
    /// <para>
    /// <b>It is also where a person's answer arrives.</b> A run paused before the coordinator is
    /// resumed with <c>ResumeAsync(id, transform)</c> writing the decision here, and on that round it
    /// is taken as final: the coordinator is not asked, so nothing can overwrite it. That is why the
    /// slot is on the coordination rather than somewhere a pause would have to invent — a decision
    /// travels in state because everything else does.
    /// </para>
    /// </remarks>
    public PlanDecision? Decision { get; init; }

    /// <summary>
    /// What is outstanding for somebody to answer, or <see langword="null"/> when nothing is.
    /// </summary>
    /// <remarks>
    /// Set by whatever asked, cleared by whatever applied the answer. It lives here rather than in a
    /// slot of its own because a question is part of where a plan stands between passes, and because
    /// a run is usually paused while one is outstanding — so it has to be somewhere already written
    /// down and read back.
    /// </remarks>
    public PlanQuestion? Question { get; init; }

    /// <summary>How many times a coordinator has intervened in this run.</summary>
    /// <remarks>
    /// Counts interventions the run made by itself — a re-ruling or a retry. Stopping ends the run
    /// rather than buying a pass, and an answer the run paused for came from outside it, so neither
    /// is counted.
    /// </remarks>
    public int Changes { get; init; }

    /// <summary>The ceiling a consumer asked for, or <see langword="null"/> if they asked for none.</summary>
    /// <remarks>
    /// <b>Nothing invents one.</b> What a run costs is bounded by <c>BudgetConfig</c>, and what a node
    /// may attempt by its contract's iteration bound; a ceiling on re-planning is a judgement about
    /// how much re-planning is worth doing, which belongs to whoever is paying for it. When it is set,
    /// a coordinator that can see it is about to spend the last one can behave differently: decide
    /// more conservatively, or stop rather than mint a version nothing will get to act on.
    /// </remarks>
    public int? MaxChanges { get; init; }

    /// <summary>The node that stopped the run, or <see langword="null"/> if the plan settled.</summary>
    public string? NodeId => Result.HaltedAt;

    /// <summary>The halted node.</summary>
    public PlanNode? Node => NodeId is { } id ? Result.Tree.Node(id) : null;

    /// <summary>
    /// Why the plan stopped being right, <b>taken from the halt</b> rather than composed by anyone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what a minted version records, and nobody authors it.</b> A required reason field
    /// that a model has to fill is how a live planner came to blame a tool limitation that does not
    /// exist — and a version's reason is the one part of a lineage no diff can recover, so a wrong
    /// one is permanent.
    /// </para>
    /// <para>
    /// <b>Read from the tree's own durable facts, in the order that answers "what actually
    /// happened."</b> A failing gate's own <see cref="CriterionVerdict.Basis"/> first — the evidence a
    /// check produced (R28) — then a standing dispute's words, then a dead attempt's own message.
    /// None of these needs a halt <em>kind</em> to pick between them: at most one is ever present for
    /// a node that just halted.
    /// </para>
    /// <para>
    /// A coordinator's own argument for its replacement is a different thing and travels separately,
    /// as a <see cref="PlanRationale"/>. A reader must always be able to tell what was observed from
    /// what was inferred.
    /// </para>
    /// </remarks>
    public string? HaltReason
    {
        get
        {
            if (Node?.LatestVerdicts.FirstOrDefault(v => !v.Passed) is { Basis: { } basis } && !Blank(basis))
                return basis;

            if (Dispute is { } dispute)
                return Blank(dispute.Reason)
                    ? $"'{NodeId}' reported that its contract cannot be met, and stated no reason."
                    : dispute.Reason;

            if (Node?.Failure is { } failure)
                return Blank(failure.Message)
                    ? $"'{NodeId}' failed, and reported nothing about why."
                    : $"'{NodeId}' failed: {failure.Message}";

            // A step stopped on its own options, with no check against it.
            if (Node?.Question is { } asking && !Blank(asking.Asks))
                return asking.Asks;

            // A step blocked with no options: its own account of what it found is all there is.
            if (Node is { State: StepState.Blocked, LastRead.Summary: { } said } && !Blank(said))
                return said;

            return null;
        }
    }

    private static bool Blank(string? text) => string.IsNullOrWhiteSpace(text);

    /// <summary>What the node said was wrong, when it is disputed.</summary>
    /// <remarks>
    /// The node is the only thing that observed the contradiction, so this is the witness statement —
    /// kept verbatim, and the only honest source for why a plan stopped being right.
    /// </remarks>
    public PlanViolation? Dispute => Node?.Violation;

    /// <summary>What the node is waiting to be told, when it reached a choice it may not make.</summary>
    public NodeQuestion? Asking => Node?.Question;

    /// <summary>
    /// Whether asking anybody about this halt would be pointless — the attempt died of something no
    /// seat in this run can answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The case this exists for is an account out of allowance.</b> Every seat past the halt is
    /// another model call on the same credentials: the supervisor is asked what the halt admits, and
    /// an advisor may be asked as well. All of them fail the same way, and the run ends up reporting
    /// <em>nobody could propose anything</em> — a statement about the plan — when what happened was
    /// that nothing could be asked at all.
    /// </para>
    /// <para>
    /// <b>Read off the tree, never carried beside it.</b> The classification was taken once, where
    /// the exception still existed, and lives on the node's failure; this only asks the question.
    /// </para>
    /// </remarks>
    public bool Unanswerable => Node?.Failure is { Terminal: true };

    /// <summary>
    /// Terms somebody is <b>proposing</b> for this run from outside it, which bind nothing until a
    /// seat in this run adopts one. Empty unless something is wired to remember across runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Recall proposes; the contract binds.</b> A term stated in <em>this</em> run binds directly —
    /// the next re-ruling carries it into the contract it writes. One remembered from another run is a
    /// far stronger claim: nobody here has said it, and it may be about a plan that no longer
    /// resembles this one. So it arrives as a candidate for the supervisor to read and, if it applies,
    /// to adopt — at which point it is committed <em>here</em> and binds like any other.
    /// </para>
    /// <para>
    /// <b>The plan tier does not know where these came from and must not.</b> Whatever fills this is
    /// somebody's adapter; nothing in this assembly references an experience store, and a run with
    /// nothing wired behaves exactly as it did before this existed.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PlanTerm> Recalled { get; init; } = [];

    /// <summary>
    /// What the supervisor offered and could not be used, most recent last. Empty when it offered
    /// something, or was never asked.
    /// </summary>
    /// <remarks>
    /// <b>Carried because it is the most specific evidence anybody has.</b> Every guard states the
    /// shape it would have accepted, and a seat asked to re-author the plan without seeing what the
    /// last one tried is being asked to guess differently.
    /// </remarks>
    public IReadOnlyList<string> Refused { get; init; } = [];

    /// <summary>
    /// What somebody told the supervisor outside the options it offered, waiting for it to read.
    /// <see langword="null"/> when nothing is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Set when a question is answered off-list, and cleared by whatever asks the next one.</b> An
    /// answer nobody listed is not a choice to apply — nothing in the loop can read prose — so it goes
    /// back to the seat that can: the supervisor authors a fresh set of options with it in hand
    /// (R32), and the run reaches it through the same job that asked the first time.
    /// </para>
    /// <para>
    /// <b>Verbatim, and separate from anything read out of it.</b> Whatever the supervisor concludes
    /// travels as its own options and its own rationale; this stays as the sentence somebody actually
    /// wrote, because everything downstream acts on the reading and a reader has to be able to check
    /// it against the source (R33).
    /// </para>
    /// </remarks>
    public string? Said { get; init; }

    /// <summary>What the halted node said it did, in its own words, on its last run.</summary>
    /// <remarks>
    /// Narration rather than evidence, and worth showing anyway: a coordinator asked to replace a
    /// contract from verdicts and a dispute alone is being asked to explain a failure it did not
    /// witness, which is exactly how a model comes to invent one.
    /// </remarks>
    public string? Summary => Node?.LastRead?.Summary;

    /// <summary>
    /// Whether there is nothing left to decide right now: the plan settled, or the coordinator asked.
    /// </summary>
    /// <remarks>
    /// Only a <see cref="PlanDecision.ReplanPlan"/> loops back to attempt the plan again; an
    /// <see cref="PlanDecision.AskPlan"/>, whatever it carries, pauses the same way <c>Stop</c> and
    /// <c>Refer</c> used to.
    /// </remarks>
    public bool Settled =>
        Result.HaltedAt is null || Decision is PlanDecision.AskPlan;

    /// <summary>Who is recorded as having given a step up, when nobody more specific is known.</summary>
    public const string Answerer = "the answerer";
}

/// <summary>
/// Someone's argument for why a change of plan is the right one. An inference, and attributed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never the same field as the reason.</b> A version's reason is what was <em>observed</em> —
/// taken from the halt, in the witness's words. This is what somebody <em>concluded</em> from it, and
/// a reader who cannot tell the two apart has a lineage that reads like a record and is partly a
/// guess.
/// </para>
/// <para>
/// <b><see cref="By"/> is a role, not a model.</b> "The planner said this" is the attribution that
/// matters and the one that stays true when the model behind the role changes.
/// </para>
/// </remarks>
public sealed record PlanRationale
{
    /// <summary>Who concluded it — a role name, a person, a tool.</summary>
    public required string By { get; init; }

    /// <summary>What they concluded, and why the replacement answers it.</summary>
    public required string Text { get; init; }
}

/// <summary>
/// What a coordinator decided to do about a halt.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two, and the hierarchy is closed so that a consumer's <c>switch</c> stays exhaustive (R30).</b>
/// <see cref="ReplanPlan"/> says the plan was wrong; <see cref="Ask"/> says a person must act, whatever
/// the reason — a step waiting on a choice, a constraint nobody in the run may relax, a step somebody
/// wants given up. The question text carries the difference; the vocabulary does not need to.
/// </para>
/// <para>
/// <b>Retiring three verbs, and where each one's case went.</b> <c>Retry</c> was cost dressed as
/// design — a provider outage or an unparseable reply is a loop-level retry policy (E9), never a
/// model steering, and a dispute the coordinator judged spurious was never resolved by re-running it
/// anyway (a standing violation is only cleared by a re-ruling or a refuting check). <c>Refer</c> and
/// <c>GiveUp</c> collapse into <see cref="Ask"/>: <em>only the owner can move the dates</em> and
/// <em>bullet train or overnight bus</em> are both <em>a person must act</em>, and dropping a step is
/// an authored option like any other rather than a decision shape of its own. <c>Stop</c> is not a
/// supervisor verb either — ending a run is a person's cancel, or the planner reporting that nothing
/// can be planned (044's R25).
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(ReplanPlan), "replan")]
[JsonDerivedType(typeof(AskPlan), "ask")]
public abstract record PlanDecision
{
    private PlanDecision()
    {
    }

    /// <summary>Whether taking <paramref name="decision"/> spends one of the run's changes of plan.</summary>
    /// <remarks>
    /// Only a <see cref="ReplanPlan"/> spends — it is the only one of the two that re-authors a contract.
    /// Asking changes nothing about the plan yet, so there is nothing for the budget to be counting.
    /// </remarks>
    internal static bool Spends(PlanDecision decision, bool escalated) =>
        !escalated && decision is ReplanPlan;

    /// <summary>
    /// The plan was wrong: replace a node's contract, and optionally its children, minting a version.
    /// </summary>
    /// <param name="contract">What replaces the current contract.</param>
    /// <param name="rationale">
    /// Optionally, why the replacement answers the halt. Recorded beside the version's reason and
    /// attributed to whoever concluded it — never merged into it.
    /// </param>
    /// <param name="children">The replacement decomposition. <see langword="null"/> keeps the node's current children.</param>
    /// <param name="nodeId">
    /// The node to re-rule. <see langword="null"/> means the one that halted; naming an ancestor
    /// re-plans the remainder, because a node's input is the previous node's output.
    /// </param>
    /// <remarks>
    /// <b>There is no reason to supply.</b> Why the old contract stopped being right is taken from
    /// the halt — <see cref="PlanCoordination.HaltReason"/> — because the coordinator did not witness
    /// it. A field it had to fill instead is how a live planner came to record a cause that never
    /// happened, into the one part of a lineage no diff can recover.
    /// </remarks>
    public static PlanDecision Replan(
        AgentContract contract,
        PlanRationale? rationale = null,
        IEnumerable<AuthoredStep>? children = null,
        string? nodeId = null) =>
        new ReplanPlan
        {
            Contract = contract,
            Rationale = rationale,
            Children = children is null ? null : [.. children],
            NodeId = nodeId
        };

    /// <summary>The plan was wrong, and the Planner is the one to say what replaces it.</summary>
    /// <param name="rationale">What the supervisor concluded, carried to the Planner unchanged.</param>
    public static PlanDecision Replan(PlanRationale rationale) =>
        new ReplanPlan { Rationale = rationale ?? throw new ArgumentNullException(nameof(rationale)) };

    /// <summary>A person must act — whatever kind of choice put the halt beyond this seat.</summary>
    /// <param name="options">
    /// The courses of action, authored and narrowed by the supervisor, one marked recommended. Empty
    /// is legal: it says the supervisor has nothing to propose, and the question stands on the
    /// refusals alone (R32 — free text and cancel are never among these; whatever answers the
    /// question appends them).
    /// </param>
    /// <remarks>
    /// <b>Deliberately thin for now.</b> The full shape a person answers — authored options with a
    /// required recommendation, free text, cancel, distinguishably typed for whatever reads the
    /// answer back — is E7's. This only needs to exist so the vocabulary closes at two; what a
    /// supervisor actually puts in <paramref name="options"/> does not change here.
    /// </remarks>
    public static PlanDecision Ask(IEnumerable<PlanOption> options) =>
        new AskPlan { Options = [.. options] };

    /// <summary>The plan was wrong, and here is what replaces it.</summary>
    public sealed record ReplanPlan : PlanDecision
    {
        /// <summary>
        /// What replaces the current contract, or <see langword="null"/> when the Planner is the one
        /// to write it.
        /// </summary>
        public AgentContract? Contract { get; init; }

        /// <inheritdoc cref="Replan(AgentContract, PlanRationale?, IEnumerable{AuthoredStep}?, string?)"/>
        public PlanRationale? Rationale { get; init; }

        /// <inheritdoc cref="Replan(AgentContract, PlanRationale?, IEnumerable{AuthoredStep}?, string?)"/>
        public IReadOnlyList<AuthoredStep>? Children { get; init; }

        /// <inheritdoc cref="Replan(AgentContract, PlanRationale?, IEnumerable{AuthoredStep}?, string?)"/>
        public string? NodeId { get; init; }
    }

    /// <summary>Somebody outside this seat must decide. The run pauses, checkpointed, on this.</summary>
    public sealed record AskPlan : PlanDecision
    {
        /// <inheritdoc cref="Ask"/>
        public required IReadOnlyList<PlanOption> Options { get; init; }
    }
}

/// <summary>
/// Decides what a supervised plan does about a halt.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the role the tier was built for.</b> Everything else in it — the decomposition, the
/// pinned contract, the versioned tree, verification, the dispute path — exists so that something can
/// notice a plan has stopped being right and change it. Without one, a plan detects a contradiction
/// perfectly and then has nothing to say.
/// </para>
/// <para>
/// <b>It is not the node, and the node never reaches it.</b> A node reports; it never asks. The
/// authority to change a criterion belongs to the level that wrote it, which is why this runs beside
/// the supervised plan rather than inside it.
/// </para>
/// <para>
/// A coordinator that is not a model — a person, a queue, a rules table — is an
/// <c>IJob&lt;TState&gt;</c> instead, which the same pattern accepts in the same slot.
/// </para>
/// <para>
/// <b>It is not called on a round the run was paused for and answered.</b> Where an escalation is
/// wired, a decision already in <see cref="PlanCoordination.Decision"/> when the run resumes is the
/// decision, and this is never asked — so a coordinator needs no awareness of pauses, and cannot
/// overwrite an answer somebody was stopped and asked for. Resuming with nothing decided still asks:
/// somebody looked and did not answer, which is not the same as nobody having been asked.
/// </para>
/// </remarks>
public delegate Task<PlanDecision> PlanSupervisor(PlanCoordination coordination, CancellationToken ct);
