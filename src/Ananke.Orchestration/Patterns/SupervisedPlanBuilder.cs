using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Workflows;

namespace Ananke.Orchestration.Patterns;

/// <summary>
/// Fluent builder for the <b>Supervised Plan</b> agentic pattern.
/// <para>
/// The pattern wires two jobs — a <em>supervised plan</em> and a <em>coordinator</em> — in a feedback
/// loop. The plan runs to completion; when a node reports that its contract cannot be met, the
/// coordinator decides what the plan becomes and the work resumes under the new version.
/// </para>
/// <para>
/// <b>Every way out of that loop is a condition, and none of them is a number this pattern picked.</b>
/// It ends when the plan settles, when the coordinator stops, when a round decided nothing — the
/// next round would put the identical question to the identical coordinator, which is what being
/// stuck is — or when a ceiling the consumer asked for is reached. That last one is
/// <see cref="MaxChangesOfPlan"/>, and it is <b>optional and unset by default</b>.
/// </para>
/// </summary>
/// <remarks>
/// <para><b>Generated workflow topology:</b></para>
/// <code>
/// plan → coordinate → [settled?] → __end__
///   ↑         │ no
///   └─────────┘
/// </code>
/// <para>
/// <b>Nothing here is a new execution concept.</b> The builder composes <c>Supervise</c>, <c>Job</c>,
/// <c>Then</c> and <c>Loop</c> — all of which ship — and returns an ordinary
/// <see cref="Workflow{TState}"/>, open for checkpointing, tracing, extra jobs, or embedding as a
/// sub-workflow. The change of plan is a job, so it appears in the run's history and topology,
/// <see cref="EscalateToAPerson(Func{PlanCoordination, bool})"/> stops the run between assessment and
/// resumption on the halts a person must answer, and a coordinator that is a person or a queue rather
/// than a model goes in the same slot.
/// </para>
/// <para>
/// <b>A change of plan is not a new plan.</b> The tree is durable in the supervision's store and
/// carries its versions, so looping back into the same supervised job picks up the next version with
/// its lineage intact. Spawning a fresh plan per change would discard exactly what versions exist to
/// preserve.
/// </para>
/// <para>
/// Create instances via <see cref="AgenticPattern.SupervisedPlan{TState}"/>.
/// </para>
/// </remarks>
/// <typeparam name="TState">The workflow state type. It must carry a <see cref="PlanCoordination"/> slot.</typeparam>
public sealed class SupervisedPlanBuilder<TState>
{
    /// <summary>The name of the job that runs the plan.</summary>
    public const string PlanJob = "plan";

    /// <summary>The name of the job that decides what a halted plan becomes.</summary>
    /// <remarks>
    /// Stable, because it is the handle: <c>InterruptBefore(SupervisedPlanBuilder&lt;T&gt;.CoordinatorJob)</c>
    /// is how a consumer stops a run between assessment and resumption.
    /// </remarks>
    public const string CoordinatorJob = "coordinate";

    /// <summary>The name of the job that offers changes of plan for somebody else to choose between.</summary>
    /// <remarks>Registered only when <see cref="WithAdvisor"/> was called, or <see cref="SupervisionOptions.Advisor"/> was set.</remarks>
    public const string AdvisorJob = "propose";

    /// <summary>The name of the job that re-authors the plan when a halt is one no step can absorb.</summary>
    /// <remarks>Registered only when <see cref="WithPlanner"/> was called, or <see cref="SupervisionOptions.Author"/> was set.</remarks>
    public const string PlannerJob = "author";

    /// <summary>The name of the job that reviews a settled plan against its goal.</summary>
    /// <remarks>Registered only when <see cref="WithReviewer"/> was called.</remarks>
    public const string ReviewerJob = "review";

    private readonly string _name;
    private Func<TState, PlanTree>? _plan;
    private SupervisionOptions? _supervision;
    private PlanSupervisor? _coordinate;
    private IJob<TState>? _coordinatorJob;
    private Func<TState, PlanCoordination?>? _read;
    private Func<TState, PlanCoordination, TState>? _write;
    private int? _maxChangesOfPlan;
    private Func<PlanCoordination, bool>? _escalate;
    private PlanAdvisor? _advisor;
    private bool _unattended;
    private PlanAuthor? _author;
    private PlanReviewer? _review;

    internal SupervisedPlanBuilder(string name) => _name = name;

    /// <summary>Sets the plan to run.</summary>
    public SupervisedPlanBuilder<TState> WithPlan(PlanTree plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plan = _ => plan;
        return this;
    }

    /// <summary>Sets the plan to run, sourced from state.</summary>
    /// <remarks>
    /// <b>Seed only.</b> A store that already holds this plan holds it further along than whatever
    /// the state is carrying, and the supervised job prefers what the store has.
    /// </remarks>
    public SupervisedPlanBuilder<TState> WithPlan(Func<TState, PlanTree> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plan = plan;
        return this;
    }

    /// <summary>Sets who runs a node, who rules on it, and where the tree lives.</summary>
    public SupervisedPlanBuilder<TState> Supervised(SupervisionOptions supervision)
    {
        ArgumentNullException.ThrowIfNull(supervision);
        _supervision = supervision;
        return this;
    }

    /// <summary>Where the plan's progress lives in <typeparamref name="TState"/>.</summary>
    /// <param name="read">Reads the slot. <see langword="null"/> before the first pass.</param>
    /// <param name="write">Writes the slot back.</param>
    /// <remarks>
    /// One slot, read and written the way <c>SubFlow</c> maps in and out. It is state rather than
    /// something the pattern holds privately because a run that is checkpointed and resumed must find
    /// its plan's progress where every other job's progress is.
    /// </remarks>
    public SupervisedPlanBuilder<TState> Tracking(
        Func<TState, PlanCoordination?> read,
        Func<TState, PlanCoordination, TState> write)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        _read = read;
        _write = write;
        return this;
    }

    /// <summary>Sets who decides what a halted plan becomes.</summary>
    public SupervisedPlanBuilder<TState> WithCoordinator(PlanSupervisor coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        _coordinate = coordinate;
        _coordinatorJob = null;
        return this;
    }

    /// <summary>Sets the coordinator as a job of its own.</summary>
    /// <remarks>
    /// For a decider that is not a function of the halt: a person to be asked, a queue to be posted
    /// to, a sub-workflow. It is responsible for applying its own decision — the supervision's
    /// <c>ReruleAsync</c> is what it calls to do so — and for writing the outcome where
    /// <see cref="Tracking"/> reads it.
    /// </remarks>
    public SupervisedPlanBuilder<TState> WithCoordinator(IJob<TState> job)
    {
        ArgumentNullException.ThrowIfNull(job);
        _coordinatorJob = job;
        _coordinate = null;
        return this;
    }

    /// <summary>
    /// Sets who offers the changes of plan a halt admits, for somebody else to choose between.
    /// </summary>
    /// <param name="advisor">Offers two or three alternatives, one of them recommended.</param>
    /// <param name="unattended">
    /// Answer the advisor's own recommendation in-job, rather than pausing for a person. The same
    /// topology serves an attended and an unattended run; only this flag changes. Forwarded to
    /// <see cref="PlanAskingJob{TState}"/>'s own <c>autopilot</c> flag — spelled differently here
    /// because only that job's constructor may be named for it; a second public reader of that name
    /// is how a branch on the mode gets back into the loop.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Implies the chooser.</b> With this called and no explicit <see cref="WithCoordinator(PlanSupervisor)"/>
    /// or <see cref="WithCoordinator(IJob{TState})"/>, the coordinator seat becomes a
    /// <see cref="PlanChoiceJob{TState}"/> — the offer is put to a person or answered unattended, and
    /// the pick is applied. Setting an explicit coordinator too is legal: that is the shape for a
    /// coordinator that has to pause <em>between</em> proposing and deciding.
    /// </para>
    /// <para>
    /// Wires <see cref="AdvisorJob"/> ahead of the coordinator, and composes the halts this pauses for
    /// with whatever <see cref="EscalateToAPerson(Func{PlanCoordination, bool})"/> already names.
    /// </para>
    /// </remarks>
    public SupervisedPlanBuilder<TState> WithAdvisor(PlanAdvisor advisor, bool unattended = false)
    {
        ArgumentNullException.ThrowIfNull(advisor);
        _advisor = advisor;
        _unattended = unattended;
        return this;
    }

    /// <summary>
    /// Sets who re-authors the plan when a halt is one no single step can absorb.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Runs on two signals: a proposal that kept nothing, and a chosen replan.</b> Not a proposal
    /// that was exhausted — that is a tool budget, not a plan — and not a halt somebody has been asked
    /// about and answered with an option that changes nothing here, because a person choosing is the
    /// answer and this is what happens when there was nothing to choose, or when what was chosen was
    /// itself a request to replan.
    /// </para>
    /// <para>
    /// Wires <see cref="PlannerJob"/> between <see cref="AdvisorJob"/> and the coordinator. Without an
    /// advisor also wired, nothing routes a halt to it.
    /// </para>
    /// </remarks>
    public SupervisedPlanBuilder<TState> WithPlanner(PlanAuthor author)
    {
        ArgumentNullException.ThrowIfNull(author);
        _author = author;
        return this;
    }

    /// <summary>Sets who reviews a settled plan against its goal.</summary>
    /// <remarks>
    /// <para>
    /// <b>Runs only when there is nothing left to run</b>, and it accepts silence: a reviewer that
    /// answered nothing has not found a fault. A rejection is recorded as a dispute against the root,
    /// so it travels — to the coordinator, to a person where one is wired — the way every other halt
    /// does.
    /// </para>
    /// <para>
    /// Wires <see cref="ReviewerJob"/> after a settled plan, ahead of ending the run.
    /// </para>
    /// </remarks>
    public SupervisedPlanBuilder<TState> WithReviewer(PlanReviewer review)
    {
        ArgumentNullException.ThrowIfNull(review);
        _review = review;
        return this;
    }

    /// <summary>
    /// Sets a ceiling on how many times the coordinator may change the plan in one run.
    /// <b>Optional, and unset by default.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing invents a ceiling for you.</b> What a run may spend is bounded by <c>BudgetConfig</c>,
    /// in the currency that actually runs out; what a node may attempt is bounded by its contract's
    /// iteration bound, and a node with none left halts the pass without running anything. A ceiling
    /// on re-planning is a third judgement — how much re-planning is worth doing — and it is the
    /// consumer's, so a default here would be the framework ending runs on a number nobody chose.
    /// </para>
    /// <para>
    /// Set it when a coordinator's judgement is not trusted to stop on its own: a planner
    /// re-authoring the same contradiction pass after pass is real observed behaviour, and a
    /// consumer who wants a hard stop rather than a cost ceiling asks for one here.
    /// </para>
    /// <para>
    /// <b>It counts the rounds the run took by itself.</b> A round the run <em>paused</em> for is
    /// bounded by somebody being there to answer, so it is reported and never counted — and that is
    /// read from the pause, not from the declaration that wired it. Any interrupt before the
    /// coordinator has the effect, not only
    /// <see cref="EscalateToAPerson(Func{PlanCoordination, bool})"/>: a plain approval gate over
    /// every change of plan makes every round a paused one, so this ceiling never comes into force
    /// and the person at the gate is the bound. That is the intended composition rather than a
    /// caveat — but a host that answers such a gate automatically has removed both bounds and should
    /// reach for <c>BudgetConfig</c> instead.
    /// </para>
    /// </remarks>
    public SupervisedPlanBuilder<TState> MaxChangesOfPlan(int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        _maxChangesOfPlan = max;
        return this;
    }

    /// <summary>
    /// Stops the run and waits for a person on the halts <paramref name="when"/> names, instead of
    /// deciding them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Escalation is a property of the halt, not of the run.</b> A plan can steer itself through a
    /// dispute it knows how to re-rule and still need somebody for a step that has run out of
    /// attempts — so this is a condition over the halt, and the rounds it does not name are decided
    /// the way they always were.
    /// </para>
    /// <para>
    /// <b>What it wires</b> is a conditional pause before the coordinator job, which is the same pause
    /// every other human-in-the-loop workflow uses: the run checkpoints, reports <c>Interrupted</c>,
    /// and is continued with <c>ResumeAsync(id, transform)</c> carrying whatever the person decided.
    /// It therefore needs <c>UseCheckpointing</c>, and a host already able to render a paused workflow
    /// needs to learn nothing new.
    /// </para>
    /// <para>
    /// <b>An escalated decision is reported and never counted.</b> <see cref="MaxChangesOfPlan"/>, if
    /// a consumer set one, bounds the re-planning this loop does <em>by itself</em>; a round it paused
    /// for is bounded by somebody being there to answer, so the two compose rather than competing. The
    /// framework reads which is which from how the coordinator was entered, never from what came back
    /// — a decision's <c>PlanRationale.By</c> is free text a model can write.
    /// </para>
    /// </remarks>
    /// <param name="when">The halts a person must answer. Given the coordination as the coordinator
    /// would have seen it — the halted node, the cause, the tree and the budget.</param>
    public SupervisedPlanBuilder<TState> EscalateToAPerson(Func<PlanCoordination, bool> when)
    {
        ArgumentNullException.ThrowIfNull(when);
        _escalate = when;
        return this;
    }

    /// <summary>Stops the run and waits for a person on <b>every</b> halt.</summary>
    /// <remarks>
    /// The shape to reach for when no halt is the plan's own to answer — an approval gate over
    /// changes of plan. <see cref="EscalateToAPerson(Func{PlanCoordination, bool})"/> is the one to
    /// reach for when some of them are.
    /// </remarks>
    public SupervisedPlanBuilder<TState> EscalateToAPerson() =>
        EscalateToAPerson(_ => true);

    /// <summary>
    /// Validates the configuration and builds the <see cref="Workflow{TState}"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A required part is missing.</exception>
    public Workflow<TState> Build()
    {
        if (_plan is null)
            throw new InvalidOperationException(
                $"SupervisedPlan '{_name}': a plan is required. Call WithPlan().");

        if (_supervision is null)
            throw new InvalidOperationException(
                $"SupervisedPlan '{_name}': supervision is required. Call Supervised().");

        if (_read is null || _write is null)
            throw new InvalidOperationException(
                $"SupervisedPlan '{_name}': the state slot is required. Call Tracking(read, write).");

        // An advisor implies the chooser (below), so it is the third way to answer "who decides".
        var advisor = _advisor ?? _supervision.Advisor;
        var author = _author ?? _supervision.Author;
        var review = _review;

        if (_coordinate is null && _coordinatorJob is null && advisor is null)
            throw new InvalidOperationException(
                $"SupervisedPlan '{_name}': a coordinator is required. Call WithCoordinator() or "
                + "WithAdvisor(). A plan that decides nothing when it halts is "
                + "Workflow<TState>.Supervise on its own.");

        var read = _read;
        var write = _write;
        var max = _maxChangesOfPlan;
        var escalate = _escalate;

        // A consumer's own job is wrapped so the run's accounting — the change-of-plan budget, and
        // saying that a decision was taken — stays with the framework rather than being four duties
        // a consumer has to know about and can silently skip. The delegate form already does all of
        // them, and does one of them better: it reports before applying, which a wrapper cannot.
        var coordinator = _coordinatorJob is { } supplied
            ? new AccountedSupervisorJob<TState>(supplied, read, write)
            : _coordinate is { } coordinate
                ? (IJob<TState>)new PlanSupervisorJob<TState>(CoordinatorJob, _supervision, coordinate, read, write)
                // Nothing explicit was given, so the advisor's own offer is what gets chosen between —
                // the shape the demo wires, and the only one left once the guard above has passed.
                : new AccountedSupervisorJob<TState>(
                    new PlanChoiceJob<TState>(CoordinatorJob, _supervision, read, write), read, write);

        var workflow = new Workflow<TState>(_name)
            .Supervise(PlanJob, _plan, _supervision, (state, result) =>
            {
                var current = read(state);

                // The decision is cleared, not carried: it answered the previous halt, and letting it
                // survive into a new pass would let a stale "stop" settle a plan that just moved.
                return write(state, current is null
                    ? new PlanCoordination { Result = result, MaxChanges = max }
                    : current with { Result = result, Decision = null, MaxChanges = max });
            })
            .Job(coordinator.Name, coordinator);

        // No advisor and no reviewer: today's topology, bit-identical, so every existing consumer of
        // this builder keeps the shape it has always had.
        if (advisor is null && review is null)
        {
            workflow
                .Then(PlanJob, coordinator.Name)
                .Loop(
                    coordinator.Name,
                    loopTarget: PlanJob,
                    exitTarget: Workflow.End,
                    // The cap is counted where the changes are, not inferred from how many times the
                    // loop went round: a pass that settles the plan and a pass that changes it are
                    // both one iteration, and only one of them spends the budget.
                    until: state => read(state) is not { } coordination
                        || coordination.Settled
                        || coordination.MaxChanges is { } ceiling && coordination.Changes >= ceiling
                        // Asked, and said nothing. The plan is unchanged and unsettled, so the next
                        // round would put the identical question to the identical coordinator — which
                        // is what "stuck" is, detected where it happens rather than counted up to.
                        // Only a job coordinator can reach this: the delegate form returns a decision
                        // by signature.
                        || coordination.Decision is null,
                    // No cap: every way out of this loop is a condition, above.
                    maxIterations: int.MaxValue);

            // A settled plan is nobody's to answer, so the condition never sees one: the pause is for
            // a halt, and the coordinator job is reached after every pass whether or not there was one.
            if (escalate is not null)
                workflow.AwaitInputWhen(
                    coordinator.Name,
                    state => read(state) is { Result.HaltedAt: not null } coordination
                        && escalate(coordination));

            return workflow;
        }

        // plan -> propose -> author -> choose -> plan|End, with review after a settled pass. Every
        // bracketed leg is optional, and its absence is a Then straight to whatever follows it — the
        // ordering is the demo's own (ItineraryDemo.TripRun.Build) and it is load-bearing: a halt
        // reaches the Planner only once the advisor has kept nothing, and a rejected review re-enters
        // at the advisor rather than past it.
        if (advisor is not null)
            workflow.Job(AdvisorJob, new PlanAskingJob<TState>(AdvisorJob, advisor, _supervision, read, write, _unattended));

        if (author is not null)
            workflow.Job(PlannerJob, new PlanAuthorJob<TState>(PlannerJob, author, _supervision, read, write));

        if (review is not null)
            workflow.Job(ReviewerJob, new PlanReviewJob<TState>(ReviewerJob, review, _supervision, read, write));

        workflow.Then(PlanJob, Workflow.Decide<TState>(state =>
            read(state)?.NodeId is null
                ? (review is not null ? ReviewerJob : Workflow.End)
                : (advisor is not null ? AdvisorJob : coordinator.Name)));

        if (advisor is not null)
            workflow.Then(AdvisorJob, author is not null ? PlannerJob : coordinator.Name);

        if (author is not null)
            workflow.Then(PlannerJob, coordinator.Name);

        if (review is not null)
            workflow.Then(ReviewerJob, Workflow.Decide<TState>(state =>
                read(state)?.NodeId is null
                    ? Workflow.End
                    : (advisor is not null ? AdvisorJob : coordinator.Name)));

        workflow.Then(coordinator.Name, Workflow.Decide<TState>(state => read(state) switch
        {
            // A chosen replan carries no contract of its own — the Planner writes it. First, so that
            // a replan chosen on the last allowed change is still written, just as a contract re-rule
            // is applied before the ceiling ends the run.
            { Decision: PlanDecision.ReplanPlan { Contract: null } } when author is not null => PlannerJob,
            { MaxChanges: { } cap, Changes: var spent } when spent >= cap => Workflow.End,
            // Somebody answered off the list. That is not a choice to apply and not a way out — it is
            // an answer for the supervisor to read, so the run goes back to the seat that authors
            // options and asks again with it in hand (R32). Never past it to the plan: the step is
            // unchanged, and re-attempting it would spend an executor on a halt nobody has answered.
            // With no advisor wired there is nothing that can read prose, and the rows below end it.
            { Said: not null } when advisor is not null => AdvisorJob,
            // Nobody has answered. The run goes back to the offer, not past it.
            { Question.Outstanding: true } => advisor is not null ? AdvisorJob : coordinator.Name,
            // Asked, with no question left outstanding: Stop and Refer are both this now (R30) — a
            // person's cancel, or a constraint nobody in this run may relax — told apart only by
            // what (if anything) the question itself said, not by the vocabulary.
            { Decision: PlanDecision.AskPlan, Question: null } => Workflow.End,
            // Answered, and nothing decided, nothing outstanding: the identical question would reach
            // the identical coordinator, which is what "stuck" is.
            { Question: null, Decision: null, NodeId: not null } => Workflow.End,
            _ => PlanJob
        }));

        // Composed with whatever EscalateToAPerson already names: the question having been recorded
        // is what makes there be something to answer, and an advisor's own autopilot flag answering
        // it in-job is what lets one topology serve both an attended and an unattended run.
        if (escalate is not null || advisor is not null)
            workflow.AwaitInputWhen(coordinator.Name, state =>
            {
                var coordination = read(state);

                return (escalate is not null
                        && coordination is { Result.HaltedAt: not null }
                        && escalate(coordination))
                    || (advisor is not null && coordination?.Question is { Outstanding: true });
            });

        return workflow;
    }
}
