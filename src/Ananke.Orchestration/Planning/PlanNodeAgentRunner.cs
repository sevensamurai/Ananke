using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Nodes;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Planning;

/// <summary>How a plan node is run as an agent job answering <typeparamref name="TAnswer"/>.</summary>
/// <remarks>
/// Deliberately small. Everything a job builder can already configure — tools, a context strategy, a
/// tool-output policy, retries — is reachable through <see cref="Configure"/> rather than restated
/// here, so this type does not become a second copy of the builder that drifts from it.
/// </remarks>
/// <typeparam name="TAnswer">What the node answers: the shape it is asked to fill.</typeparam>
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public record PlanNodeAgentOptions<TAnswer>
    where TAnswer : class, IPlanNodeAnswer
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
    /// the pinned contract would hold only for callers who remembered.
    /// </remarks>
    public Action<AgentJob<PlanNodeContext, TAnswer>.Builder>? Configure { get; init; }

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

    /// <summary>
    /// Offers each of <see cref="Operations"/> as a tool too. A call records the operation, applies
    /// nothing and ends the step's job: the call is its candidate, and no report is asked for. A step
    /// that calls none answers as it would without them.
    /// </summary>
    /// <remarks>
    /// <b>Why a step would need it.</b> A step with tools answers twice: its tool rounds, then a reply
    /// turned into the report. A proposal written in prose has to survive that turning, and can be
    /// lost on the way; a call is structured before it happens. A report asked for after the call
    /// writes the operation out again, and a copy is not the call. Off by default, and with no
    /// <see cref="Operations"/> it does nothing.
    /// </remarks>
    public bool OperationsAsTools { get; init; }

    /// <summary>
    /// Offers the answer as a tool too, for an answer that has one (<see cref="IPlanNodeAnswer.Tool"/>).
    /// A call is the step's answer and ends its job, and no answer is asked for after it. A step that
    /// calls it not answers as it would without it.
    /// </summary>
    /// <remarks>
    /// <b>Why a step would need it.</b> A step with tools that answers in words is asked for the same
    /// answer again as JSON, in one more call sent without the tools: the answer is written twice, and
    /// a request without the tools no longer starts as the tool rounds' did, so a provider's prompt
    /// cache does not hold it. A call gives the answer once, in a request that is the tool rounds' own.
    /// Off by default.
    /// </remarks>
    public bool AnswerAsTool { get; init; }

    public Action<AgentJob<PlanNodeContext, TAnswer>.Builder, PlanNodeContext>? ConfigureFor
    {
        get;
        init;
    }

    /// <summary>
    /// The fields a step's effort is written as, for the model this runner runs on, such as
    /// OpenRouter's <c>reasoning</c>. Every request of a step that names an effort
    /// (<see cref="PlanNode.Effort"/>) carries them.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> when this runner has no way to write one. A step that names an effort is
    /// then refused before any call, rather than run at the model's default.
    /// </remarks>
    public Func<string, IReadOnlyDictionary<string, JsonNode>>? EffortFields { get; init; }


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

/// <summary>How a plan node is run as an agent job that proposes: it answers a <see cref="PlanNodeReport"/>.</summary>
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record PlanNodeAgentOptions : PlanNodeAgentOptions<PlanNodeReport>;

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
/// <typeparam name="TAnswer">
/// What the node answers. It decides what the node is asked for and what that means to the executor
/// (<see cref="IPlanNodeAnswer"/>); the runner decides neither.
/// </typeparam>
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class PlanNodeAgentRunner<TAnswer>(PlanNodeAgentOptions<TAnswer> options)
    where TAnswer : class, IPlanNodeAnswer
{
    private readonly PlanNodeAgentOptions<TAnswer> _options = Checked(options);

    /// <summary>This runner as the delegate <see cref="PlanExecutor"/> takes.</summary>
    public PlanNodeRunner AsRunner() => RunAsync;

    /// <summary>Runs one node and turns what it reported into an outcome.</summary>
    public async Task<NodeOutcome> RunAsync(PlanNodeContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        TAnswer? answer = null;
        string? endedWith = null;

        var builder = AgentJobFactory.Create<PlanNodeContext, TAnswer>(
            $"plan-node:{context.Node.Id}", _options.Model);

        // Before the caller's configuration, so a caller can still read the answer another way.
        if (TAnswer.FromText is { } read)
            builder.WithAnswerFromText(said => (TAnswer)read(said));

        _options.Configure?.Invoke(builder);
        _options.ConfigureFor?.Invoke(builder, context);

        // After the caller's configuration, so the effort the step names is the one its requests carry.
        if (context.Node.Effort is { } effort)
        {
            var fields = _options.EffortFields?.Invoke(effort) ?? throw new InvalidOperationException(
                $"'{context.Node.Id}' names the effort '{effort}', and this runner has no way to write one "
                + "into a request: give its options EffortFields.");

#pragma warning disable ANNKEEXP003 // a request's own fields are experimental
            builder.WithRequestFields(fields);
#pragma warning restore ANNKEEXP003
        }

        Operation? called = null;
        TAnswer? answeredByCall = null;
        ToolKit? calls = null;

        if (_options.OperationsAsTools && _options.Operations is { } catalog)
            calls = OperationTools(catalog, operation => called = operation);

        if (_options.AnswerAsTool && TAnswer.Tool(given => answeredByCall = (TAnswer)given) is { } answering)
            calls = calls is null ? answering : calls.Merge(answering);

        if (calls is not null)
        {
            builder.WithMoreTools(calls)
                .WhenEndedByTool((state, said) =>
                {
                    endedWith = said;
                    return state;
                });
        }

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
                answer = produced;
                return state;
            })
            .Build();

        try
        {
            await job.ExecuteAsync(context, ct).ConfigureAwait(false);
        }
        // A step that has proposed has done its work; what fails after its call loses only the step's
        // account of it, so the proposal goes to the gate on its own. A step that answered by its call
        // has given its whole answer. A guardrail stops the step instead: what it blocked is not
        // something a proposal may outlive.
        catch (Exception ex) when ((called is not null || answeredByCall is not null)
                                   && ex is not (OperationCanceledException or GuardrailException)
                                   && !TerminalProviderError.Is(ex))
        {
            return called is not null ? new NodeOutcome { Candidate = called } : answeredByCall!.ToOutcome();
        }

        // What a node said it did goes on the event stream and no further — reported by the executor
        // from the outcome, for every runner shape rather than only for this one. The candidate goes
        // to the executor's shape gate and, once past it, to whatever the supervision applies it
        // with; this type never touches the world itself.
        //
        // A step that called its operation ended there, whatever its answer's shape: the call is its
        // proposal, as it was made, and what it wrote with the call its account of it.
        if (called is not null)
            return new NodeOutcome { Summary = string.IsNullOrWhiteSpace(endedWith) ? null : endedWith.Trim(), Candidate = called };

        // A step that gave its answer by calling ended there, and the call carried all of it.
        if (answeredByCall is not null)
            return answeredByCall.ToOutcome();

        return answer is null ? NodeOutcome.Nothing : answer.ToOutcome();
    }

    /// <summary>
    /// <paramref name="options"/>, once they can run <typeparamref name="TAnswer"/>: an answer that is
    /// proposed only by calling needs operations offered as tools, or no step could propose.
    /// </summary>
    private static PlanNodeAgentOptions<TAnswer> Checked(PlanNodeAgentOptions<TAnswer> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (TAnswer.RequiresOperationsAsTools && !(options.OperationsAsTools && options.Operations is not null))
        {
            throw new ArgumentException(
                $"{typeof(TAnswer).Name} is proposed by calling an operation: give the options Operations and set "
                + $"{nameof(PlanNodeAgentOptions<TAnswer>.OperationsAsTools)}.",
                nameof(options));
        }

        if (options.AnswerAsTool && TAnswer.Tool(_ => { }) is null)
        {
            throw new ArgumentException(
                $"{typeof(TAnswer).Name} has no tool a step could give it by: leave "
                + $"{nameof(PlanNodeAgentOptions<TAnswer>.AnswerAsTool)} off.",
                nameof(options));
        }

        return options;
    }

    /// <summary>
    /// Each operation in <paramref name="catalog"/> as a tool whose call is handed to
    /// <paramref name="record"/>, applies nothing and ends the job.
    /// </summary>
    /// <remarks>
    /// One tool per name: a catalog may define a name at more than one arity, and a tool name is unique.
    /// Arguments past the fewest any definition of it takes are optional, and the shape gate rules on
    /// the arity as it does for any candidate. An argument a definition names as a list is offered as
    /// one, and reaches the operation as the JSON the call held.
    /// </remarks>
    private static ToolKit OperationTools(OperationCatalog catalog, Action<Operation> record)
    {
        var kit = new ToolKit("operations");

        foreach (var named in catalog.Definitions.GroupBy(d => d.Name, StringComparer.Ordinal))
        {
            var most = named.Max(d => d.Arity);
            var fewest = named.Min(d => d.Arity);
            var description = string.Join(" Or: ", named.Select(d => d.Description));

            kit.AddTool(named.Key, $"Propose this operation. {description} The call is your proposal and ends your turn; it is applied after.", b =>
            {
                b.EndsJob();

                for (var i = 1; i <= most; i++)
                {
                    var at = i - 1;

                    if (named.Select(d => d.Lists.GetValueOrDefault(at)).FirstOrDefault(entry => entry is not null) is { } entry)
                        b.ParamList($"arg{i}", $"Argument {i}, a list, as the operation describes it.", entry, required: i <= fewest);
                    else
                        b.Param<string>($"arg{i}", $"Argument {i}, as the operation describes it.", required: i <= fewest);
                }

                b.OnExecute(args =>
                {
                    var given = Enumerable.Range(1, most).Select(i => args.GetOrDefault($"arg{i}")).ToList();

                    while (given.Count > 0 && given[^1] is null)
                        given.RemoveAt(given.Count - 1);

                    record(new Operation { Name = named.Key, Arguments = [.. given.Select(a => a ?? string.Empty)] });

                    // The step does not read this: its job ends at the call. A conversation kept
                    // across steps does, so it says only what happened.
                    return ToolResult.Ok($"Proposed {named.Key}.");
                });
            });
        }

        return kit;
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
            text.Append("# Your last attempt could not be used\n\n");

            if (rejection.Candidate is { } candidate)
                text.Append("You proposed:\n\n> ").Append(candidate.ToString()).Append("\n\n");

            if (rejection.Findings.Count > 0)
            {
                text.Append("You reported:\n\n");
                foreach (var finding in rejection.Findings)
                    text.Append("> ").Append(finding.Claim).Append('\n');
                text.Append('\n');
            }

            text.Append("It could not be used: ").Append(rejection.Finding).Append("\n\n");
        }

        text.Append("# Your turn\n\n").Append(TAnswer.Instructions(_options.Operations));

        if (_options.AnswerAsTool && TAnswer.Tool(_ => { }) is { } answering)
        {
            text.Append("\n\nGive your answer by calling ")
                .Append(string.Join(" or ", answering.Tools.Keys))
                .Append(": the call is your answer and ends your turn.");
        }

        return text.ToString();
    }
}

/// <summary>
/// Runs a plan node as an agent job that proposes: it answers a <see cref="PlanNodeReport"/>. The
/// shape every node had before a step could be asked for less.
/// </summary>
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class PlanNodeAgentRunner(PlanNodeAgentOptions options)
{
    private readonly PlanNodeAgentRunner<PlanNodeReport> _runner = new(options);

    /// <summary>This runner as the delegate <see cref="PlanExecutor"/> takes.</summary>
    public PlanNodeRunner AsRunner() => _runner.AsRunner();

    /// <summary>Runs one node and turns what it reported into an outcome.</summary>
    public Task<NodeOutcome> RunAsync(PlanNodeContext context, CancellationToken ct = default) =>
        _runner.RunAsync(context, ct);

    /// <summary>The node's turn-one prompt.</summary>
    internal string BuildPrompt(PlanNodeContext context) => _runner.BuildPrompt(context);
}
