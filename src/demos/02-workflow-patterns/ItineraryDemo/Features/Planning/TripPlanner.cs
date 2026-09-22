using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Tools;
using ItineraryDemo.Features.Stays;
using ItineraryDemo.Model;
using ItineraryDemo.Shared;

namespace ItineraryDemo.Features.Planning;

/// <summary>One step a re-authored trip would have.</summary>
/// <remarks>A place and its set dates, written yyyy-MM-dd; the criterion and goal are built from them.</remarks>
internal sealed record PlannedStep
{
    public string Id { get; init; } = string.Empty;
    public string Place { get; init; } = string.Empty;
    public string CheckIn { get; init; } = string.Empty;
    public string CheckOut { get; init; } = string.Empty;
}

/// <summary>The trip as the planner would now write it.</summary>
internal sealed record PlannedTrip
{
    public IReadOnlyList<PlannedStep> Steps { get; init; } = [];
    public string? Rationale { get; init; }
}

/// <summary>
/// Rewrites the plan when a step reports it cannot find what it was asked for, or the week does not add up.
/// </summary>
/// <remarks>
/// <b>Settles scarce or constrained places first, then plans the rest around them.</b> The root's own
/// criterion — <c>week_planned</c> — is what forces this: nothing about one place's stay says anything
/// about the whole week adding up, so it is the plan's shape, not any one step's contract, that has to
/// change once a place will not fit where it was asked to.
/// </remarks>
internal sealed class TripPlanner(
    IAgentModel model, TripService service, InvocationCheck validate, Narration narration)
{
    private const int ToolRounds = 24;

    private const string Persona =
        "You write travel plans. Before you write a step, look up what the service actually has "
        + "with your tools, and run check_stay on every stay you write. You name places and "
        + "dates; the steps find the stays.";

    public PlanAuthor AsAuthor() => ReauthorAsync;

    /// <summary>Asks the Planner for a new plan, and writes it only when it holds.</summary>
    /// <remarks>
    /// <b>The Planner's answer is checked, not trusted.</b> It is run through the same checks as
    /// <c>check_plan</c>. A plan that does not hold goes back to the Planner once with the findings;
    /// if the second answer does not hold either, nothing is written.
    /// </remarks>
    public async Task<AuthoredPlan?> ReauthorAsync(
        PlanCoordination coordination, IReadOnlyList<string> refused, CancellationToken ct)
    {
        var tree = coordination.Result.Tree;
        IReadOnlyList<string> findings = [];

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            if (await AskAsync(coordination, refused, findings, ct).ConfigureAwait(false) is not { } planned)
                return null;

            findings = await FindingsAsync(tree, [.. planned.Steps.Select(StayOn)], ct)
                .ConfigureAwait(false);

            if (findings.Count == 0)
                return Authored(tree, planned);

            narration.Line(
                $"   planner's plan does not hold{(attempt == 1 ? ", asking again" : ", so nothing is written")}: "
                + string.Join(" | ", findings));
        }

        return null;
    }

    private async Task<PlannedTrip?> AskAsync(
        PlanCoordination coordination, IReadOnlyList<string> refused, IReadOnlyList<string> findings, CancellationToken ct)
    {
        PlannedTrip? planned = null;

        var job = AgentJobFactory.Create<PlanCoordination, PlannedTrip>("trip-planner", model)
            .WithSystemPrompt(Persona)
            .WithPrompt(halt => Prompt(halt, refused) + Rejected(findings))
            .WithTools(new StayTools(service).Planning()
                .AddTool(
                    "check_plan",
                    "Whether a whole draft plan holds: every stay can be had, the nights add up to what the "
                        + "plan must hold, none twice, and the places follow the route.",
                    tool => tool
                        .ParamList<PlannedStep>("stays", "Every stay in the plan, in date order.")
                        .OnExecute(args => CheckPlanAsync(
                            coordination.Result.Tree, args.Get<IReadOnlyList<PlannedStep>>("stays"), ct))))
            .WithMaxToolRounds(ToolRounds)
            .MapResult((halt, produced) =>
            {
                planned = produced;
                return halt;
            })
            .Build();

        try
        {
            await job.ExecuteAsync(coordination, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException exhausted)
            when (exhausted.Message.Contains("Tool-calling loop exceeded", StringComparison.Ordinal))
        {
            narration.Line($"   planner ran out of tool rounds ({ToolRounds}) before writing a plan");
            return null;
        }

        if (planned is null)
        {
            narration.Line("   planner returned no answer");
            return null;
        }

        if (planned.Steps.Count == 0)
        {
            narration.Line(string.IsNullOrWhiteSpace(planned.Rationale)
                ? "   planner answered with no steps and no rationale"
                : $"   planner answered with no steps: {planned.Rationale}");
            return null;
        }

        return planned;
    }

    /// <summary>What to tell the Planner about its last plan, when it did not hold.</summary>
    private static string Rejected(IReadOnlyList<string> findings) =>
        findings.Count == 0
            ? string.Empty
            : "\n\nYour last plan did not hold, and was not written:\n"
              + string.Join("\n", findings.Select(finding => $"- {finding}"))
              + "\nWrite a plan that holds, and run check_plan on it before you answer.";

    private static AuthoredPlan Authored(PlanTree tree, PlannedTrip planned) => new()
    {
        Contract = tree.Root.Contract,
        Steps =
        [
            .. planned.Steps.Select(step => new AuthoredStep
            {
                Id = step.Id,
                Contract = new AgentContract
                {
                    Goal = $"Find a stay in {step.Place} from {step.CheckIn} to {step.CheckOut}",
                    AcceptanceCriteria = [StayOn(step)]
                }
            })
        ],
        Rationale = planned.Rationale
    };

    /// <summary>The criterion a planned step is held to: its place on its set dates.</summary>
    private static string StayOn(PlannedStep step) => $"stay_on({step.Place}, {step.CheckIn}, {step.CheckOut})";

    /// <summary>What the Planner's <c>check_plan</c> tool answers.</summary>
    private async Task<ToolResult> CheckPlanAsync(
        PlanTree tree, IReadOnlyList<PlannedStep> stays, CancellationToken ct)
    {
        if (stays.Count == 0)
            return ToolResult.Error("'stays' must list every stay in the plan.");

        // A stay is a place and two dates, so nothing else can be written here and a night count that
        // disagrees with the dates cannot exist.
        var criteria = stays.Select(StayOn).ToList();
        var findings = await FindingsAsync(tree, criteria, ct).ConfigureAwait(false);

        return ToolResult.Ok(findings.Count == 0
            ? "The plan holds: every stay can be had, the nights add up, and it follows the route."
            : "The plan does not hold:\n" + string.Join("\n", findings.Select(f => $"- {f}")));
    }

    /// <summary>
    /// Runs a draft plan through the same checks the run would, and returns what does not hold.
    /// </summary>
    /// <remarks>
    /// The draft is built as the plan it would become, so the week and the route are ruled on over
    /// every step together rather than one criterion at a time.
    /// </remarks>
    private async Task<IReadOnlyList<string>> FindingsAsync(
        PlanTree tree, IReadOnlyList<string> criteria, CancellationToken ct)
    {
        if (criteria.Count == 0)
            return ["no step has a criterion"];

        var draft = Draft(tree, criteria);
        var checks = Validate.Checks(service, _ => Task.FromResult<PlanTree?>(draft));

        return await PlanFindings.UnmetAsync(draft, [checks], ct).ConfigureAwait(false);
    }

    /// <summary>The plan a draft would become, so a check over the week reads every stay together.</summary>
    /// <remarks>
    /// The ids are the demo's: a step is a place and the stays it is drafted from are anonymous until
    /// the plan is written.
    /// </remarks>
    private static PlanTree Draft(PlanTree tree, IReadOnlyList<string> criteria)
    {
        var steps = criteria
            .Select((criterion, index) => (
                Id: $"{Criterion.Parse(criterion)?.Argument(0) ?? "step"}-{index + 1}",
                Contract: new AgentContract { Goal = "draft", AcceptanceCriteria = [criterion] }))
            .ToList();

        return tree.Rerule(tree.Root.Id, tree.Root.Contract, "draft", steps);
    }

    private string Prompt(PlanCoordination coordination, IReadOnlyList<string> refused) =>
        PlanHaltRecord.ForPlanner(coordination, refused) + Facts();

    /// <summary>What the trip adds to the record: what the service has, and how to write a plan.</summary>
    private string Facts() =>
        $"""

        Places the service has stays in: {string.Join(", ", service.Places)}

        The checks a criterion may name:
        {validate.Legend()}

        You recommend a plan; nothing is reserved. Look up the route with route. First settle
        what is scarce and required. For each place
        the plan may not do without, look up its free nights with free_nights over the whole window the plan
        allows, and fix its dates to nights that are actually free — fewer nights than first asked for, if
        that is all there is. Then plan the remaining nights around those dates. Keep a step that is Done
        as it is: the same id, place and dates. Run check_stay on every stay you write, and change any it says is not free.
        Then run check_plan on the whole plan, every stay in date order, and write only a plan it
        says holds; change the plan and check it again until it does.
        Together the steps must satisfy what the plan must hold and break nothing it may not break. Do not
        write again a plan the versions above already tried. Say in the rationale, in one sentence, why
        this plan.
        """;

}
