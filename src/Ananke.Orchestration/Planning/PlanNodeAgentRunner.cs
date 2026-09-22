using System.Text;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;

namespace Ananke.Orchestration.Planning;

/// <summary>How a plan node is run as an agent job.</summary>
/// <remarks>
/// Deliberately small. Everything a job builder can already configure — tools, a context strategy, a
/// tool-output policy, retries — is reachable through <see cref="Configure"/> rather than restated
/// here, so this type does not become a second copy of the builder that drifts from it.
/// </remarks>
public sealed record PlanNodeAgentOptions
{
    /// <summary>The model, or a router, the node's job runs against.</summary>
    public required IAgentModel Model { get; init; }

    /// <summary>
    /// Who the agent is. Persona only — the goal comes from the node's contract, and stating it here
    /// as well is how the two authors' text starts contradicting each other.
    /// </summary>
    public string? Persona { get; init; }

    /// <summary>
    /// Applied to the job builder before this runner pins the contract and the prompt.
    /// </summary>
    /// <remarks>
    /// Order matters and is not configurable: whatever this sets, the contract and the prompt are
    /// applied afterwards and win. A caller that could overwrite them could unpin the contract, and
    /// then R2 would hold only for callers who remembered.
    /// </remarks>
    public Action<AgentJob<PlanNodeContext, PlanNodeReport>.Builder>? Configure { get; init; }

    /// <summary>
    /// The same, told which node it is configuring.
    /// </summary>
    /// <remarks>
    /// <b>For tools that belong to one step rather than to the plan.</b> Where the steps are units
    /// of work sharing a resource — one highlight of a trip, one file of a refactor — a step should
    /// be able to move its own and nothing else's. Without the node, every step gets every
    /// affordance and "do not touch somebody else's work" is a sentence in a persona rather than a
    /// fact about what it can reach.
    /// </remarks>
    /// <summary>
    /// The operations a step may propose, shown to it so it chooses rather than invents.
    /// </summary>
    /// <remarks>
    /// Optional, and a plan with none behaves as before: the step is asked for an operation and
    /// nothing tells it which exist, which is the arrangement that produced <c>onsen_booked(hakone)</c>
    /// and a place id nobody had declared.
    /// </remarks>
    public OperationCatalog? Operations { get; init; }

    public Action<AgentJob<PlanNodeContext, PlanNodeReport>.Builder, PlanNodeContext>? ConfigureFor
    {
        get;
        init;
    }


    /// <summary>
    /// What is named as the oracle on verdicts derived from a node's own report.
    /// </summary>
    /// <remarks>
    /// Defaulted to something that says plainly what it is. A verdict whose oracle is a node's own
    /// say-so must never be indistinguishable in the tree from one a build produced — the whole
    /// value of recording an oracle is that the two read differently a month later.
    /// </remarks>
    public string Oracle { get; init; } = "self-reported";

    /// <summary>Clock used to stamp verdicts and disputes.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>
/// Runs a plan node as an agent job: the contract is pinned into every request the node makes, the
/// tree it is allowed to read is put in front of it, and its answer comes back as a
/// <see cref="NodeOutcome"/> the tree can record.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the join, and it exists so that consumers do not each write their own.</b> The two
/// tiers either side of it were built and tested separately: a contract is pinned by construction
/// inside a job, and nothing crosses a node boundary except through the tree. Both properties held
/// within their own tier and neither was enforced across the seam, because the seam had no code —
/// so every consumer wrote the join by hand, and wrote it differently.
/// </para>
/// <para>
/// <b>The node does not grade itself, and does not act either.</b> What it returns is narration —
/// <see cref="PlanNodeReport.Summary"/> carried into <see cref="NodeOutcome.Summary"/> — and a
/// candidate operation, <see cref="PlanNodeReport.Operation"/> carried into
/// <see cref="NodeOutcome.Candidate"/>. Never a verdict or a dispute, and never a
/// change to the world made by this type itself: whether a criterion holds is decided by
/// something that did not do the work — a verifier, when one is configured; nothing, when one is
/// not — and a candidate is applied by whatever the supervision was given to apply it.
/// </para>
/// <para>
/// <b>A fresh job per node, deliberately.</b> Contracts differ per node, and a contract is pinned at
/// construction; reusing one job across nodes would carry the first node's contract into the second.
/// It is also what keeps the executor stateless between nodes in the plainest possible way — there
/// is no object alive across two nodes for a result to hide in.
/// </para>
/// </remarks>
public sealed class PlanNodeAgentRunner(PlanNodeAgentOptions options)
{
    private readonly PlanNodeAgentOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>This runner as the delegate <see cref="PlanExecutor"/> takes.</summary>
    public PlanNodeRunner AsRunner() => RunAsync;

    /// <summary>Runs one node and turns what it reported into an outcome.</summary>
    public async Task<NodeOutcome> RunAsync(PlanNodeContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        PlanNodeReport? report = null;

        var builder = AgentJobFactory.Create<PlanNodeContext, PlanNodeReport>(
            $"plan-node:{context.Node.Id}", _options.Model);

        _options.Configure?.Invoke(builder);
        _options.ConfigureFor?.Invoke(builder, context);

        if (!string.IsNullOrWhiteSpace(_options.Persona))
            builder.WithSystemPrompt(_options.Persona);

        // Applied last: see PlanNodeAgentOptions.Configure. The contract reaches every assembly this
        // job makes because the engine composes it once at construction, not because anything here
        // remembers to restate it.
        var job = builder
            .WithContract(context.Contract)
            .WithPrompt(BuildPrompt)
            .MapResult((state, produced) =>
            {
                report = produced;
                return state;
            })
            .Build();

        await job.ExecuteAsync(context, ct).ConfigureAwait(false);

        // What a node said it did goes on the event stream and no further — reported by the executor
        // from the outcome, for every runner shape rather than only for this one. The candidate goes
        // to the executor's shape gate and, once past it, to whatever the supervision applies it
        // with; this type never touches the world itself.
        return report is null
            ? NodeOutcome.Nothing
            : new NodeOutcome
            {
                Summary = report.Summary,
                Candidate = report.Operation,
                Done = report.Done,
                Options = report.Options
            };
    }

    /// <summary>
    /// The node's turn-one prompt: what the plan looks like from here, then what is being asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The projected tree goes on the user turn, not into the pinned system section.</b> It is
    /// neither persona nor contract, and putting it where the contract lives would pin something
    /// that is a <em>reading</em> — already budgeted, already trimmed, and true only of the plan
    /// version in force when the node started. What must survive compaction is what the node was
    /// asked for, and that is pinned separately and by construction; the surroundings being
    /// droppable is what makes it affordable to show them at all.
    /// </para>
    /// <para>
    /// The projection's own omission notices travel with it, so a node that was shown a partial view
    /// is told as much rather than being left to infer completeness from silence.
    /// </para>
    /// </remarks>
    internal string BuildPrompt(PlanNodeContext context)
    {
        var text = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(context.TreeView))
        {
            text.Append("# The plan, as it stands\n\n")
                .Append(context.TreeView.Trim())
                .Append("\n\n");
        }

        // What somebody decided on this node's behalf, and the only channel it has: nothing is handed
        // from one attempt to the next, so an answer that is not read off the node is not read at all.
        if (context.Node.Answers is { Count: > 0 } answers)
        {
            text.Append("# What you asked, and what you were told\n\n");

            foreach (var answered in answers)
            {
                text.Append("- ").Append(answered.Asked)
                    .Append("\n  → **").Append(answered.Answer).Append("** (")
                    .Append(answered.By).Append(")\n");
            }

            text.Append("\nThose are decided. Work to them; do not ask again and do not choose ")
                .Append("differently.\n\n");
        }

        // The only memory a retry gets (PlanNodeContext.Rejection): verbatim, because a fresh job per
        // attempt means this node has no other way to know what it said last time.
        //
        // Its own words and the gate's own words, under a heading, and nothing else. No restatement
        // of the finding and no instruction to try harder or differently: a loop that tells an
        // executor what to do about evidence has started supervising it, which is the seat this tier
        // spent an ADR taking judgement out of. What to do next is already in the contract below.
        if (context.Rejection is { } rejection)
        {
            text.Append("# Your last attempt could not be used\n\n").Append("You proposed:\n\n> ")
                .Append(rejection.Candidate.ToString()).Append("\n\nIt could not be used: ")
                .Append(rejection.Finding).Append("\n\n");
        }

        text.Append("# Your turn\n\n");

        // Without operations there is nothing to apply, so the step only reports what it found.
        text.Append(_options.Operations is null
            ? "Look up what your contract asks for with your tools, and say what you found in a sentence or two.\n\n"
            : "Propose the change that would satisfy your contract. You do not perform it yourself: "
              + "something outside you applies it, then checks whether it worked. Leave it out only if "
              + "this step has nothing of its own to change. Then say what you propose, or why nothing "
              + "needs to change, in a sentence or two.\n\n");

        text.Append("Set \"Done\" to true when what you found meets your contract, and false when it does ")
            .Append("not. List in \"Options\" the choices you found, one short line each that names the ")
            .Append("choice so a person can pick it: when \"Done\" is true, every choice that meets the ")
            .Append("contract; when it is false, every choice that comes close — the same thing on a smaller ")
            .Append("scale, on other dates or in another version, where the results show one. Take options ")
            .Append("only from what your tools returned — never advice, never a reason, and never something ")
            .Append("you did not look up — and keep the order the results gave them. Do not choose between ")
            .Append("them; whoever reads them decides. Why goes in your summary, not in \"Options\". If ")
            .Append("nothing comes close, leave \"Options\" empty.");

        // What may be proposed, rather than an invitation to invent one. A step asked for an
        // operation "in its own words" writes something plausible and unrecognised, and the run
        // spends a shape rejection discovering it.
        if (_options.Operations is { } catalog)
        {
            text.Append(" If you could not do it, propose nothing.\n\n")
                // Proposing is the whole of a step's part here, and its contract cannot be met while
                // it proposes: nothing has applied the change or checked it yet. Read the other way,
                // a step that proposed correctly reports it is not done, and the executor drops the
                // candidate it just made. Said only where operations exist — a step that reports
                // findings answers the question the advisor asks, which is a different one.
                .Append("Proposing a change is doing your part, so set \"Done\" to true when you ")
                .Append("propose one: something outside you applies it and checks it afterwards, and ")
                .Append("until that has happened no contract about the change can be met. Set ")
                .Append("\"Done\" to false only when you could not do your part at all — a tool or a ")
                .Append("service failed, what was asked was not clear enough to act on, or there is ")
                .Append("nothing you can propose.\n\n")
                // The proposal is the operation, so a step that proposed has nothing left to offer
                // anyone a choice between. Said here because a run put the whole apply_diff call
                // into an option, which a person would have been asked to pick between.
                .Append("When you propose a change, leave \"Options\" empty: the proposal is the ")
                .Append("operation, and there is nothing there for anyone to choose between. Options ")
                .Append("belong to the case where you could not do your part — one short line each, ")
                .Append("in words somebody can act on, never a diff and never an operation.\n\n")
                .Append("The operations that exist. Choose one and give its name and ")
                .Append("arguments exactly:\n\n").Append(catalog.Legend());
        }

        return text.ToString();
    }
}
