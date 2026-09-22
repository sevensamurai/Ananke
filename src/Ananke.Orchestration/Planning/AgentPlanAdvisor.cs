using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;

namespace Ananke.Orchestration.Planning;

/// <summary>One alternative, as the model returns it.</summary>
internal sealed record ProposedOption
{
    /// <summary>One line somebody can weigh this against the others by.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Why this answers the halt.</summary>
    public string? Rationale { get; init; }

    /// <summary>Whether this is the one the supervisor would take.</summary>
    public bool Recommended { get; init; }

    /// <summary>
    /// Whether taking this asks for the plan to be changed, leaving what replaces it to the Planner.
    /// </summary>
    public bool Replan { get; init; }

    /// <summary>
    /// Why this step should be given up, when that is what this option is. Empty for an ordinary
    /// option.
    /// </summary>
    public string? Abandon { get; init; }
}

/// <summary>Which of a step's options the supervisor would take, as the model returns it.</summary>
/// <remarks>
/// <b>A position, not the option's words.</b> Copied text can come back altered, or be an option the
/// step never found, and then something has to decide what a near-match meant. A number is either one
/// of the options or it is not.
/// </remarks>
internal sealed record ChosenOption
{
    /// <summary>Its place in the step's list, counting from one.</summary>
    public int? Option { get; init; }

    /// <summary>Why that one, in a line.</summary>
    public string? Why { get; init; }
}

internal sealed record ProposedOptions
{
    public IReadOnlyList<ProposedOption> Options { get; init; } = [];

    /// <summary>
    /// What an off-list answer settled about the plan as a whole, beyond answering this halt.
    /// Empty is the ordinary case.
    /// </summary>
    public IReadOnlyList<string> Terms { get; init; } = [];
}

/// <summary>
/// Asks the planner for the changes of plan a halt admits, and which one it would take.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shipped answer to a question every consumer was writing.</b> A wall is where understanding
/// is needed, so producing alternatives is a model's work — and a plan tier that made each consumer
/// wire that themselves would be repeating the shape it has already named five times.
/// </para>
/// <para>
/// <b>It offers; it does not decide.</b> Nothing here applies anything, spends anything or picks on
/// anybody's behalf. What comes back is a list and a recommendation, for whoever asked to choose
/// between — which is the whole difference between this and <see cref="AgentPlanSupervisor"/>.
/// </para>
/// <para>
/// <b>Cancel is not in the list</b>, deliberately: a way out is not a judgement, so it is appended by
/// whatever puts the question rather than left to a supervisor to remember.
/// </para>
/// </remarks>
public sealed class AgentPlanAdvisor(SupervisionOptions supervision, string? persona = null)
{
    /// <summary>How many alternatives are worth having: enough to be a choice, few enough to read.</summary>
    private const int Most = 3;

    /// <summary>Enough rounds to try a candidate, be told it does not work, and try another.</summary>
    private const int DefaultToolRounds = 12;

    /// <summary>
    /// What to say first when the role has tools, and nothing when it has none.
    /// </summary>
    /// <remarks>
    /// <b>Offering a tool is not asking for it to be used.</b> A live supervisor with tools wired,
    /// described and offered called none of them: everything around the question said <em>reply</em>,
    /// so it replied. The instruction belongs in the ask rather than in a persona a consumer may
    /// replace — a supervision that supplies tools has said what it wants, and this is the tier
    /// asking for it on their behalf.
    /// </remarks>
    internal static string Check(SupervisionOptions supervision) =>
        supervision.SupervisorTools is null
            ? string.Empty
            : """
              You have tools. Use them before you answer: read the facts, and test each option you
              are considering. An option you have not checked is a guess, and the arithmetic that
              rules on it afterwards will not be guessing. Do not offer one a tool has just told you
              does not work.


              """;

    /// <summary>
    /// What to tell a model about the option that drops the step instead of changing it.
    /// </summary>
    /// <remarks>
    /// <b>Offered as an option because it is one.</b> It used to be a refusal a person reached
    /// past the supervisor for, which made giving up the one course of action nobody had to weigh, or
    /// recommend against. Said here so the seat that can see the whole plan is the seat that decides
    /// whether the trip is better without this leg.
    /// </remarks>
    internal static string Drop(PlanCoordination coordination) =>
        $"""
         One option may give '{coordination.NodeId}' up: say why, in a line, in "abandon". Offer it
         when the plan is genuinely better without this step, and not while a change of plan might
         still work. It gives up the step and not the run; whoever is asked already has a way to stop.


         """;

    private const string DefaultPersona =
        "You supervise a plan. One work item has stopped: a check says its contract is not met, or it "
        + "reported that it cannot be met. Say what kind of problem this is and cite the finding that "
        + "shows it. The plan's steps are not yours to write: if the plan has to change, say so and why, "
        + "and the Planner decides how. You are not asked what went wrong — that is already recorded, in "
        + "the words of whatever stopped. Do not offer stopping as an option; whoever is asked already "
        + "has that.";

    /// <summary>Reads the halt, asks the planner, and returns what it offered.</summary>
    /// <exception cref="InvalidOperationException">The plan did not halt, so there is nothing to change.</exception>
    public async Task<PlanProposal> ProposeAsync(
        PlanCoordination coordination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(coordination);

        var first = await AskAsync(coordination, [], ct).ConfigureAwait(false);

        // Asked again, once, with what was wrong. Every guard here already computes the shape it
        // would have accepted — "put it on the step for 'kyoto' instead" — and until now that was
        // written down and thrown away, because a proposal that keeps nothing ends the run. A
        // rejection that teaches nothing is this iteration's oldest open finding, and the lesson
        // was already in hand.
        //
        // Once, not until it works: a second refusal is a fact about the seat rather than about
        // the wording, and it is the thing worth handing on.
        if (first is not { Empty: true, Exhausted: false, Discarded.Count: > 0 })
            return first;

        var second = await AskAsync(coordination, first.Discarded, ct).ConfigureAwait(false);

        // Both rounds, in order. A reader shown only the second attempt cannot tell a seat that
        // was told and corrected itself from one that was told and repeated itself — and that
        // difference is the whole of what this retry measures.
        return second with { Discarded = [.. first.Discarded, .. second.Discarded] };
    }

    /// <summary>Asks once, telling the model what was refused last time if anything was.</summary>
    private async Task<PlanProposal> AskAsync(
        PlanCoordination coordination, IReadOnlyList<string> refused, CancellationToken ct)
    {
        if (coordination.Node is not { } node)
            throw new InvalidOperationException(
                "An AgentPlanAdvisor was asked about a plan that did not halt. Nothing stopped, so "
                + "there is nothing to offer alternatives to.");

        var model = supervision.ModelFor(PlanRoles.Supervisor)
            ?? throw new InvalidOperationException(
                "An AgentPlanAdvisor needs a model for the 'supervisor' role: set Supervisor on the "
                + "supervision, or add it to Models.");

        // A step that left options has already looked at the world. Every one of them is offered, and
        // the supervisor only chooses which to recommend.
        if (node.Question is { Options.Count: > 0 } asking)
            return await RecommendAsync(coordination, asking, model, ct).ConfigureAwait(false);

        ProposedOptions? proposed = null;

        var builder = AgentJobFactory.Create<PlanCoordination, ProposedOptions>("plan-advisor", model)
            .WithSystemPrompt(persona ?? DefaultPersona)
            .WithPrompt(halt => AgentPlanSupervisor.DescribeHalt(
                halt,
                Check(supervision)
                + PlanHaltRecord.Said(halt)
                + PlanHaltRecord.Remembered(halt)
                + PlanHaltRecord.Tried(halt)
                + Drop(halt)
                + PlanHaltRecord.Refused(refused)
                + """
                If — and only if — somebody has just answered in their own words above, and what
                they said settles something about the plan beyond this halt, report it as a term: one
                line, in the present tense, as a standing constraint. When it is unclear whether what
                they said is an answer to this halt or a term for the run, it is an answer — a term
                binds every version after it. Report no terms otherwise.

                Reply with the options there are, at most one of each kind. One may give the step up
                ("abandon", as above). One may ask for the plan to be changed ("replan": true). A
                replan's rationale names the finding that makes this the plan's problem and not the
                step's — the verdict, the constraint or the fact, quoted — and does not say how to
                change the plan: the Planner decides that. Never both on one option. For each, a
                one-line summary that names the problem, not a fix. Mark exactly one as recommended,
                and say in its rationale why that one: something unattended will take it without
                asking anybody. Do not offer stopping. If nothing would help, reply with no options.
                """))
            .MapResult((halt, produced) =>
            {
                proposed = produced;
                return halt;
            });

        if (supervision.SupervisorTools is { } tools)
            builder = builder
                .WithTools(tools)
                .WithMaxToolRounds(supervision.SupervisorToolRounds ?? DefaultToolRounds);

        var job = builder.Build();

        try
        {
            await job.ExecuteAsync(coordination, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException exhausted) when (OutOfRounds(exhausted))
        {
            // Searching until the rounds run out is a supervisor that did not finish thinking, not a
            // broken run. Offering nothing is already an outcome the tier handles — the halt stands
            // and whoever is watching sees it — whereas letting this escape ends the workflow at the
            // halt with a message about tool budgets, which tells a reader nothing about their plan.
            //
            // Marked, because it is the opposite of having nothing to offer: one is a statement about
            // the plan and the other about a budget, and only the first is worth re-planning on.
            return new PlanProposal { Options = [], Exhausted = true };
        }

        // An option that neither gives the step up nor asks for a replan says nothing, so it is not
        // an option. A proposal that loses all of them comes back empty, which is a real answer:
        // nothing was offered, and whoever asked still has the way out they were always going to have.
        var options = proposed?.Options ?? [];

        // One replan at most: several could only differ in the change they describe, and which change to
        // make is the Planner's.
        var replan = options.FirstOrDefault(o => o.Replan && o.Recommended && Refused(o) is null)
            ?? options.FirstOrDefault(o => o.Replan && Refused(o) is null);

        var usable = new List<ProposedOption>();
        var discarded = new List<string>();

        foreach (var option in options)
        {
            if (Refused(option) is { } why)
            {
                discarded.Add($"\"{Name(option)}\": {why}");
                continue;
            }

            if (option.Replan && !ReferenceEquals(option, replan))
            {
                discarded.Add($"\"{Name(option)}\": only one replan is offered, and how to change the plan is the Planner's to decide");
                continue;
            }

            if (usable.Count < Most)
                usable.Add(option);
        }

        if (usable.Count == 0)
            return new PlanProposal { Options = [], Discarded = discarded };

        // Exactly one recommendation, whatever came back. A model that recommended everything, or
        // nothing, has not answered the question — and a caller reading `Recommended` must never find
        // two, because the thing that answers unattended takes the first it sees.
        var pick = usable.FindIndex(o => o.Recommended);
        if (pick < 0)
            pick = 0;

        return new PlanProposal
        {
            Discarded = discarded,

            // Only from an off-list answer: a term is something somebody settled, and with nobody
            // having said anything there is nothing for the supervisor to have read it out of.
            // From this run's own off-list answer, or from a remembered one the supervisor adopted.
            // With neither, a term would be something this seat invented about a plan nobody
            // commented on, which is the one way a term must never come about.
            Terms = string.IsNullOrWhiteSpace(coordination.Said) && coordination.Recalled.Count == 0
                ? []
                : [.. (proposed?.Terms ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())],
            Options =
            [
                .. usable.Select((option, index) => new PlanOption
                {
                    Summary = string.IsNullOrWhiteSpace(option.Summary)
                        ? (option.Replan ? "Change the plan" : $"Give '{node.Id}' up")
                        : option.Summary,
                    Abandon = Abandonment(option),
                    Replan = option.Replan,
                    // Attributed to the role, not to the model: the attribution that stays true when
                    // the model behind the role changes.
                    Rationale = string.IsNullOrWhiteSpace(option.Rationale)
                        ? null
                        : new PlanRationale { By = PlanRoles.Supervisor, Text = option.Rationale! },
                    Recommended = index == pick
                })
            ]
        };
    }

    /// <summary>Puts forward every option the step found, with the supervisor's pick recommended.</summary>
    /// <remarks>
    /// <b>The supervisor authors and drops nothing here.</b> Every option is one the step found, offered
    /// in the step's order; what is asked is which position it would take and why. An answer outside the
    /// list recommends the step's first option and says so.
    /// </remarks>
    private async Task<PlanProposal> RecommendAsync(
        PlanCoordination coordination, NodeQuestion asking, IAgentModel model, CancellationToken ct)
    {
        ChosenOption? fitted = null;

        var builder = AgentJobFactory.Create<PlanCoordination, ChosenOption>("plan-recommender", model)
            .WithSystemPrompt(persona ?? DefaultPersona)
            .WithPrompt(halt => AgentPlanSupervisor.DescribeHalt(
                halt,
                Check(supervision)
                + PlanHaltRecord.Said(halt)
                + PlanHaltRecord.Tried(halt)
                + $"""
                   {(asking.Done
                       ? "The step did its task and found more than one way to do it:"
                       : "The step could not do its task as asked. What it found it could do instead:")}
                   {Numbered(asking.Options)}

                   Every one of these stays on offer: the step looked them up, and none is yours to drop.
                   Choose the one that best fits what the plan already holds: the steps already settled,
                   the constraints above this step, and anything a person has said. An option may ask for
                   less than this step's contract; choosing it changes the plan, and the Planner writes
                   that change. Reply with its number in "option", and in "why" one line saying why.
                   Something unattended will take it without asking anybody.
                   """))
            .MapResult((halt, produced) =>
            {
                fitted = produced;
                return halt;
            });

        if (supervision.SupervisorTools is { } tools)
            builder = builder
                .WithTools(tools)
                .WithMaxToolRounds(supervision.SupervisorToolRounds ?? DefaultToolRounds);

        try
        {
            await builder.Build().ExecuteAsync(coordination, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException exhausted) when (OutOfRounds(exhausted))
        {
            return new PlanProposal { Options = [], Exhausted = true };
        }

        var offered = asking.Options;
        var discarded = new List<string>();

        // The position it answered, counting from one. Anything else chose nothing: the step's list is
        // what there was to choose from.
        var pick = fitted?.Option is { } position && position >= 1 && position <= offered.Count
            ? position - 1
            : -1;

        var why = pick >= 0 && !string.IsNullOrWhiteSpace(fitted?.Why) ? fitted.Why.Trim() : null;

        if (pick < 0)
        {
            if (fitted?.Option is { } outside)
                discarded.Add($"option {outside}: the step found {offered.Count}");

            pick = 0;
        }

        return new PlanProposal
        {
            Discarded = discarded,
            Options =
            [
                .. offered.Select((option, index) => new PlanOption
                {
                    Summary = option,

                    // An option that meets the step's contract settles the step; one that only comes
                    // close changes what the plan asks for, which is the Planner's to write.
                    Answer = asking.Done ? option : null,
                    Replan = !asking.Done,
                    Rationale = new PlanRationale
                    {
                        By = PlanRoles.Supervisor,
                        Text = index == pick && why is not null ? $"{option} — {why}" : option
                    },
                    Recommended = index == pick
                })
            ]
        };
    }

    /// <summary>One numbered line per option, so a choice can be a position rather than copied words.</summary>
    private static string Numbered(IReadOnlyList<string> options) =>
        string.Join(Environment.NewLine, options.Select((option, index) => $"  {index + 1}. {option}"));

    /// <summary>
    /// Why an option cannot be offered, or <see langword="null"/> when it can.
    /// </summary>
    /// <remarks>
    /// <b>One place, so every refusal has a reason to report.</b> Spread across filters, each guard
    /// knew why it dropped something and none of them could say.
    /// </remarks>
    private static string? Refused(ProposedOption option) => (option.Replan, Abandonment(option) is not null) switch
    {
        (true, true) => "it both gives the step up and asks for a replan",
        (false, false) => "it neither gives the step up nor asks for a replan",
        _ => null
    };

    /// <summary>Something to call an option by in a refusal, whatever it filled in.</summary>
    private static string Name(ProposedOption option) =>
        !string.IsNullOrWhiteSpace(option.Summary) ? option.Summary : "an option with nothing in it";

    /// <summary>
    /// An option's reason for giving the step up, or <see langword="null"/> when it changes the plan
    /// instead.
    /// </summary>
    private static string? Abandonment(ProposedOption option) =>
        string.IsNullOrWhiteSpace(option.Abandon) ? null : option.Abandon!.Trim();

    /// <summary>Whether a failure is the tool loop running out rather than something being wrong.</summary>
    /// <remarks>
    /// Matched on the message because the engine raises no type of its own for it. Narrow
    /// deliberately: anything else this role throws is a real fault and must not be swallowed into
    /// <em>nothing was offered</em>.
    /// </remarks>
    internal static bool OutOfRounds(Exception error) =>
        error.Message.Contains("Tool-calling loop exceeded", StringComparison.Ordinal);

    /// <summary>This advisor as the delegate a supervision takes.</summary>
    public PlanAdvisor AsAdvisor() => ProposeAsync;
}
