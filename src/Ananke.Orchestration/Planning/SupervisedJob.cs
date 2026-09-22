using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Planning;

/// <summary>The roles a supervised plan hires a model for.</summary>
/// <remarks>
/// Names, not types, so a third role costs a string rather than a signature change. Two exist
/// because two are called; a reviewer is a real role and is not invented here until something asks
/// it a question.
/// </remarks>
/// <summary>
/// Why <paramref name="criterion"/> may not be asked of <paramref name="nodeId"/>, or
/// <see langword="null"/> when it may.
/// </summary>
/// <remarks>
/// <b>Say what would be accepted, not only what was refused.</b> The message reaches whoever is
/// choosing and, through the discard list, the model that wrote it — and a refusal that does not
/// name the shape it wanted produces a better-formed version of the same mistake (R18).
/// </remarks>
/// <param name="nodeId">The step the criterion would be asked of.</param>
/// <param name="criterion">The criterion, as written.</param>
public delegate string? CriterionAdmission(string nodeId, string criterion);

public static class PlanRoles
{
    /// <summary>Runs the work items.</summary>
    public const string Executor = "executor";

    /// <summary>
    /// Authors the plan, and re-authors it when a change is accepted that no single step absorbs.
    /// </summary>
    /// <remarks>
    /// It named the Supervisor's model for a dozen runs, which is why "we have a planner" was true in
    /// the grep and false in the design. <see cref="PlanAuthorJob{TState}"/> is the seat; a Planner
    /// model is resolved the same way an Executor's or a Supervisor's is, through
    /// <see cref="SupervisionOptions.ModelFor"/>, and <see cref="Patterns.SupervisedPlanBuilder{TState}.WithPlanner"/>
    /// is how a consumer wires the job into the loop.
    /// </remarks>
    public const string Planner = "planner";

    /// <summary>Decides what every halt means, and carries the round trip to a person.</summary>
    public const string Supervisor = "supervisor";
}

/// <summary>
/// How a plan is supervised: who runs a node, who rules on it, and where the tree lives.
/// </summary>
/// <remarks>
/// <b>Three roles, and deciding what a halted plan becomes is not one of them.</b> Each of these is
/// invoked by the supervised job itself. A coordinator is invoked by the workflow <em>around</em> the
/// job — it is a job of its own, so a change of plan can be interrupted, checkpointed and read back
/// out of the run's history — which is why it is named where the plan is composed rather than here.
/// See <c>AgenticPattern.SupervisedPlan</c>.
/// </remarks>
public sealed record SupervisionOptions
{
    /// <summary>
    /// What runs one node. <see langword="null"/> builds the default agent runner from the
    /// <see cref="PlanRoles.Executor"/> model.
    /// </summary>
    /// <remarks>
    /// Kept, and it <b>wins over <see cref="Executor"/> when both are given</b>: a node is not
    /// necessarily an agent. A shell command, a person and a queue are all things that run one work
    /// item, and a supervision that could only be configured with a model would exclude them.
    /// </remarks>
    public PlanNodeRunner? Runner { get; init; }

    /// <summary>Models by role — see <see cref="PlanRoles"/>.</summary>
    /// <remarks>
    /// <b>The mechanism; the named properties are sugar over it.</b> Roles are how this repo already
    /// talks about models elsewhere — <c>AgentRole</c> carries a model alias and an escalation alias,
    /// and the routing types select on capability rather than on an id — so this connects the plan
    /// tier to a vocabulary that exists rather than inventing a second one.
    /// </remarks>
    public IReadOnlyDictionary<string, IAgentModel> Models { get; init; } =
        new Dictionary<string, IAgentModel>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The model the work items run on. Sugar for <see cref="PlanRoles.Executor"/>.</summary>
    /// <remarks>
    /// Supplying it is enough to run a plan — no runner delegate, no runner options — which is the
    /// whole of the common case. The expensive judgement is <see cref="Supervisor"/>'s, and the two are
    /// separate because a node's work is gated by checks that catch a bad answer while a change of
    /// plan is checked by nothing.
    /// </remarks>
    public IAgentModel? Executor { get; init; }

    /// <summary>The model the Supervisor thinks with. Sugar for <see cref="PlanRoles.Supervisor"/>.</summary>
    public IAgentModel? Supervisor { get; init; }

    /// <summary>
    /// What the <see cref="PlanRoles.Supervisor"/> role can query when it is asked what a halt admits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The role asked the hardest question in the tier was the only one that could not check its
    /// own answer.</b> A work item gets tools and finds out whether what it chose actually works; a
    /// coordinator got a persona and a description of the halt, and proposed from that. It could
    /// only ever offer what sounded right — and everything downstream then treats an untested guess
    /// as a change of plan.
    /// </para>
    /// <para>
    /// <b>Give it what tells whether a halt is the step's or the plan's.</b> The facts a step queries,
    /// read-only — never the tool that records one, and never a way to score a candidate plan. Which
    /// change to make is the Planner's, and a supervisor that can draft the work has stopped supervising it.
    /// </para>
    /// <para>
    /// Applies to both jobs that run on this role: the coordinator that decides, and the advisor
    /// that offers. They put different questions to the same evidence and neither should be the
    /// better informed of the two.
    /// </para>
    /// </remarks>
    public ToolKit? SupervisorTools { get; init; }

    /// <summary>
    /// How many tool rounds the planner role may take. <see langword="null"/> for the default.
    /// </summary>
    /// <remarks>
    /// Enough to try a candidate, be told it does not work, and try another — the loop is the point
    /// of giving it tools at all. Too few is not a budget but a gag, and worse than it sounds: the
    /// job does not report having found nothing, it <b>faults</b>, and the run ends at the halt with
    /// a message about tool rounds. The first equipped supervisor to actually use its tools spent
    /// eight of them searching and killed the run.
    /// </remarks>
    public int? SupervisorToolRounds { get; init; }

    /// <summary>
    /// Why a criterion may not be asked of a particular step, or <see langword="null"/> when it may.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The pairing is what nothing else can check.</b> A criterion on its own is validated by the
    /// check that owns it — the right name, the right arity, arguments that could hold. Whether it is
    /// about the work <em>this step owns</em> is a different question, and the answer is in the domain:
    /// where a step is a place, a file or a service, a criterion naming a different one asks for work
    /// this step can reach no tool to do.
    /// </para>
    /// <para>
    /// <b>Measured.</b> A supervisor re-ruled `hakone` with the criterion <c>nights(osaka, 2)</c>. Every
    /// guard passed — a real step, a real check, the right arity, a real place — and the step, whose
    /// tools are scoped to its own leg, disputed it on every pass until the change-of-plan budget was
    /// gone. Five passes and four versions to discover that nothing could ever do the work.
    /// </para>
    /// <para>
    /// <b>It is the consumer's because ids are the consumer's</b>, which is the same reason plan
    /// admission takes a domain rule: the tier has no way to know what a name is supposed to mean.
    /// </para>
    /// </remarks>
    public CriterionAdmission? AdmitCriterion { get; init; }

    /// <summary>
    /// Where the tree lives between passes. Defaults to an in-memory store created for the run.
    /// </summary>
    /// <remarks>
    /// Worth supplying a durable one for work that outlives a process. The argument for reading a
    /// tree rather than passing records is that a loss becomes recoverable, and that only holds
    /// while the tree still exists.
    /// </remarks>
    public IPlanTreeStore? Store { get; init; }

    /// <summary>
    /// Who rules on whether a node met its contract. <see langword="null"/> records the node's own
    /// verdicts, which is what a plan without one has always done.
    /// </summary>
    public IVerifier? Verifier { get; init; }

    /// <summary>
    /// Applies a node's candidate operation to the domain the plan is about — R27's "the loop
    /// applies it". <see langword="null"/> means no node here proposes one, so the shape gate never
    /// runs and <see cref="NodeOutcome.Candidate"/> is read by nobody.
    /// </summary>
    /// <remarks>
    /// The domain-specific half of the split: a runner proposes in whatever form this reads, and
    /// never touches the world itself. For the itinerary demo that is <c>world.Carry(place)</c> and
    /// recording it on the itinerary when the world agrees; for work that edits files it would be
    /// writing the patch to disk. Nothing here judges whether the result is any good — that is still
    /// the acceptance gate's, on its next ordinary read.
    /// </remarks>
    public PlanCandidateApplier? Applier { get; init; }

    /// <summary>
    /// The operations a step may propose, ruled on before <see cref="Applier"/> is reached.
    /// </summary>
    /// <remarks>
    /// <b>Beside <see cref="Checks"/> and <see cref="AdmitCriterion"/> because it is the same kind of
    /// thing</b> — what the tier will admit, declared once and enforced where the work passes through.
    /// Left null, nothing rules on a candidate's shape and an applier receives whatever the model
    /// produced, arguments uncounted.
    /// </remarks>
    public OperationCatalog? Operations { get; init; }

    /// <summary>
    /// The checks a plan's criteria may name, when a supervisor is going to author some.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Separate from <see cref="Verifier"/> because a different question is being asked.</b>
    /// An verifier rules on finished work; this says what a criterion is <em>allowed to be</em>,
    /// and it is needed before the work — to admit a plan, and to stop a supervisor offering a
    /// change of plan nothing could ever rule on.
    /// </para>
    /// <para>
    /// <b>It exists because a supervisor authors the criteria of its own replacement.</b> Left
    /// unchecked it writes criteria that resolve and cannot hold — the right check asked about a
    /// step, a stop or a file that does not exist — and the run discovers that by executing them.
    /// </para>
    /// </remarks>
    public IReadOnlyList<IDeterministicCheck> Checks { get; init; } = [];

    /// <summary>
    /// Who offers the changes of plan a halt admits, when somebody else is going to choose between
    /// them. <see langword="null"/> for a run that decides rather than asks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <c>PlanSupervisor</c>, which <em>decides</em>. This <em>offers</em>: two or
    /// three alternatives with one recommended, for a person — or an unattended answerer — to pick
    /// from. A run that never escalates does not need one; a run that does needed one badly enough
    /// that every consumer was writing it.
    /// </para>
    /// <para>
    /// Left <see langword="null"/>, nothing changes: a halt goes to the coordinator as it always has.
    /// </para>
    /// </remarks>
    public PlanAdvisor? Advisor { get; init; }

    /// <summary>
    /// Who re-authors the plan when a halt is one no single step can absorb.
    /// <see langword="null"/> for a run with no Planner leg.
    /// </summary>
    /// <remarks>
    /// Left <see langword="null"/>, nothing changes: a halt nobody could propose against reaches the
    /// coordinator exactly as it always has, with nothing left to try.
    /// </remarks>
    public PlanAuthor? Author { get; init; }

    /// <summary>The model the Planner thinks with. Sugar for <see cref="PlanRoles.Planner"/>.</summary>
    public IAgentModel? Planner { get; init; }

    /// <summary>
    /// How many times the loop re-issues an attempt that produced nothing to evaluate.
    /// <see langword="null"/> for the default of three.
    /// </summary>
    /// <remarks>
    /// Bounds mechanical retries only — a reply that would not parse, a provider that fell over.
    /// Nothing a model decides is retried by this, and nothing it counts reaches the plan.
    /// </remarks>
    public PlanRetryPolicy? Retries { get; init; }

    /// <summary>How much of the tree a node is shown. <see langword="null"/> for the default.</summary>
    public int? ProjectionTokenBudget { get; init; }

    /// <summary>Time source for verdicts and versions.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// The store this supervision actually uses — <see cref="Store"/>, or one in memory created for
    /// it. Shared by every job registered with these options, so two of them supervise one plan
    /// rather than two copies of it.
    /// </summary>
    internal IPlanTreeStore ResolvedStore => Store ?? _fallback.Value;

    /// <summary>Who actually rules, or nobody.</summary>
    /// <remarks>
    /// <b>Never built implicitly.</b> There was sugar here that made one out of a <c>Reviewer</c>
    /// model, and R8 retired that role: what no check can decide goes to the Supervisor. A
    /// supervision that wants a model in the loop composes it — <c>AgentVerifier(supervisor,
    /// DeterministicVerifier([checks]))</c> — because which authority runs first is a decision worth
    /// writing down rather than inferring, and the checks are the cheaper and stronger one.
    /// </remarks>
    internal IVerifier? ResolvedVerifier => Verifier;

    private readonly Lazy<IPlanTreeStore> _fallback = new(() => new InMemoryPlanTreeStore());

    /// <summary>
    /// The model hired for <paramref name="role"/>, or <see langword="null"/> if none was.
    /// </summary>
    /// <remarks>
    /// A named property wins over a <see cref="Models"/> entry for the same role: it is the more
    /// visible of the two, so it should not be the one that loses silently.
    /// </remarks>
    public IAgentModel? ModelFor(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (Executor is not null && string.Equals(role, PlanRoles.Executor, StringComparison.OrdinalIgnoreCase))
            return Executor;

        if (Supervisor is not null && string.Equals(role, PlanRoles.Supervisor, StringComparison.OrdinalIgnoreCase))
            return Supervisor;

        if (Planner is not null && string.Equals(role, PlanRoles.Planner, StringComparison.OrdinalIgnoreCase))
            return Planner;

        return Models.TryGetValue(role, out var model) ? model : null;
    }

    /// <summary>What actually runs a node: the supplied runner, or one built for the executor.</summary>
    /// <remarks>
    /// Deliberately not cached on this record. A <c>with</c> copy that changes the executor must get
    /// a runner for the model it now names, and a materialised field would hand it the old one —
    /// which is the opposite of <see cref="ResolvedStore"/>, where sharing is the point.
    /// </remarks>
    internal PlanNodeRunner ResolvedRunner =>
        Runner
        ?? (ModelFor(PlanRoles.Executor) is { } model
            ? new PlanNodeAgentRunner(new PlanNodeAgentOptions
            {
                Model = model,
                TimeProvider = TimeProvider
            }).AsRunner()
            : throw new InvalidOperationException(
                "A supervision needs something to run its nodes: set Executor to a model, or Runner "
                + "to anything else that runs one work item."));

    /// <summary>
    /// Re-rules a node of a supervised plan and mints a new version, under this supervision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the job that decides a plan should change. That decision belongs above the node that
    /// reported the contradiction, so it stays a job of the consumer's — but writing it should not
    /// mean assembling an executor by hand out of the options already in hand, and it must not mean
    /// re-ruling a <em>different</em> store from the one the supervised job reads.
    /// </para>
    /// <para>
    /// Emits <c>PlanVersionMinted</c>, so a change of plan reaches the same stream the run does.
    /// </para>
    /// </remarks>
    /// <summary>Records what a node was told, and reports it.</summary>
    public Task<PlanTree> AnswerAsync(
        string planId, string nodeId, string answer, string by, CancellationToken ct = default) =>
        new PlanExecutor(ResolvedStore, TimeProvider, ProjectionTokenBudget, ResolvedVerifier)
            .AnswerAsync(planId, nodeId, answer, by, ct);

    /// <summary>Records a term somebody settled about the plan, and reports it.</summary>
    public Task<PlanTree> CommitAsync(
        string planId, string reading, string said, string by, string? termId = null,
        CancellationToken ct = default) =>
        new PlanExecutor(ResolvedStore, TimeProvider, ProjectionTokenBudget, ResolvedVerifier)
            .CommitAsync(planId, reading, said, by, termId, ct);

    /// <summary>Revokes a term, and reports it.</summary>
    public Task<PlanTree> ContradictAsync(
        string planId, string termId, string reason, PlanTermEnd end, string by,
        CancellationToken ct = default) =>
        new PlanExecutor(ResolvedStore, TimeProvider, ProjectionTokenBudget, ResolvedVerifier)
            .ContradictAsync(planId, termId, reason, end, by, ct);

    /// <summary>Records that a step is given up, and reports it.</summary>
    public Task<PlanTree> AbandonAsync(
        string planId, string nodeId, string reason, string by, CancellationToken ct = default) =>
        new PlanExecutor(ResolvedStore, TimeProvider, ProjectionTokenBudget, ResolvedVerifier)
            .AbandonAsync(planId, nodeId, reason, by, ct);

    /// <summary>Records that a node is waiting to be told something, and reports it.</summary>
    public Task<PlanTree> AskAsync(
        string planId, string nodeId, string asks,
        IReadOnlyList<string>? options = null, CancellationToken ct = default) =>
        new PlanExecutor(ResolvedStore, TimeProvider, ProjectionTokenBudget, ResolvedVerifier)
            .AskAsync(planId, nodeId, asks, options, ct);

    /// <summary>Records a dispute against a node's contract, and reports it.</summary>
    public Task<PlanTree> DisputeAsync(
        string planId, string nodeId, string criterion, string reason, CancellationToken ct = default) =>
        new PlanExecutor(ResolvedStore, TimeProvider, ProjectionTokenBudget, ResolvedVerifier)
            .DisputeAsync(planId, nodeId, criterion, reason, ct);

    public Task<PlanTree> ReruleAsync(
        string planId,
        string nodeId,
        AgentContract contract,
        string reason,
        IEnumerable<AuthoredStep>? children = null,
        PlanRationale? rationale = null,
        CancellationToken ct = default) =>
        new PlanExecutor(ResolvedStore, TimeProvider, ProjectionTokenBudget, ResolvedVerifier)
            .ReruleAsync(planId, nodeId, contract, reason, children, rationale, ct);
}

/// <summary>
/// Runs a plan to completion as one job in an ordinary workflow.
/// </summary>
/// <remarks>
/// <para>
/// <b>A supervised plan is a job, not a second execution model.</b> The framework already answered
/// "a workflow inside a workflow" with <see cref="SubFlowJob{TParent,TChild}"/> — a job, two mapping
/// functions, registered the ordinary way — and this follows it exactly. Everything the plan tier
/// adds (pinned contracts, the versioned tree, verification) is what this job <em>is</em>, rather
/// than a parallel stack a consumer assembles by hand.
/// </para>
/// <para>
/// <b>The state that flows job-to-job is not a node handoff.</b> What a plan's nodes may not pass
/// between themselves is one thing; what the workflow around the plan passes between its own jobs is
/// another, and predates this design. A job that decomposes and a job that supervises are ordinary
/// workflow jobs, and the plan travelling between them is ordinary workflow state.
/// </para>
/// <para>
/// <b>A node never resolves its own dispute, and neither does this job.</b> A node that reports its
/// contract is wrong stops the run, and the run is returned with
/// <see cref="PlanRunResult.HaltedAt"/> naming it. What happens next belongs to whoever authored the
/// criterion, one level up — a coordinator job, per <c>AgenticPattern.SupervisedPlan</c>, or a
/// caller reading the result. Deciding here would put the judgement inside the pass that discovered
/// it, and hide a change of plan from the history, the checkpoints and the topology.
/// </para>
/// <para>
/// <b>Execution inside the plan stays sequential.</b> That is a property of a plan, not of the
/// workflow around it. A workflow that forks two supervised jobs is doing what workflows do, and
/// each plan still runs one node at a time.
/// </para>
/// </remarks>
public sealed class SupervisedJob<TState>(
    string name,
    Func<TState, PlanTree> plan,
    SupervisionOptions supervision,
    Func<TState, PlanRunResult, TState> mapResult) : IJob<TState>
{
    private readonly IPlanTreeStore _store = supervision.ResolvedStore;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct = default)
    {
        var tree = plan(state)
            ?? throw new InvalidOperationException($"Supervised job '{Name}' was given no plan.");

        // Seeded, not overwritten. A store that already holds this plan holds it further along than
        // whatever the state is carrying — a run that died mid-plan, or a checkpoint restored from
        // before the last pass — and the whole argument for keeping the tree durable is that such a
        // run resumes rather than starts again.
        if (await _store.LoadAsync(tree.PlanId, ct).ConfigureAwait(false) is null)
            await _store.SaveAsync(tree, ct).ConfigureAwait(false);

        var executor = new PlanExecutor(
            _store, supervision.TimeProvider, supervision.ProjectionTokenBudget,
            supervision.ResolvedVerifier, supervision.Applier, supervision.Retries,
            supervision.Operations);

        var result = await executor
            .ExecuteToCompletionAsync(tree.PlanId, supervision.ResolvedRunner, ct)
            .ConfigureAwait(false);

        return mapResult(state, result);
    }
}
