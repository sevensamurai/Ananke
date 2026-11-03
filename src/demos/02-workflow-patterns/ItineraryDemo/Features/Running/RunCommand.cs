using System.Text.Json;
using Ananke.Abstractions.Budget;
using Ananke.Design;
using Ananke.OpenTelemetry.Budget;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using ItineraryDemo.Features.Proposing;
using ItineraryDemo.Features.Stays;
using ItineraryDemo.Shared;
using ItineraryDemo.Model;

namespace ItineraryDemo.Features.Running;

/// <summary>
/// Carries the plan out.
/// </summary>
/// <remarks>
/// <para>
/// <b>One topology, whoever is watching.</b> The same jobs over the same edges run attended and
/// unattended. What the flags choose is <em>who</em> answers.
/// </para>
/// </remarks>
internal static class RunCommand
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The three endings that are the trip's to word rather than the loop's.</summary>
    private static readonly PlanEndingWords Endings = new()
    {
        Done = "Done: every night of the trip has a recommended stay.",
        GivenUp = "Given up: the trip was called off rather than left unfinished.",
        NothingStarted = "Stopped before the trip was planned, with no step halted."
    };

    public static async Task<int> RunAsync(TripService service, PlanTree plan, Options options)
    {
        if (TripModels.For(options.Provider) is not { } models)
        {
            Console.Error.WriteLine(TripModels.Setup(options.Provider));
            return 1;
        }

        // Refused before anything runs. For a dozen runs this was a parenthesis at the end of a
        // line, and every one of them reported an escalation it did not have.
        if (models.Collapsed is { } collapsed)
        {
            Console.Error.WriteLine(collapsed);
            return 1;
        }

        if (models.Unpriced is { } unpriced)
        {
            Console.Error.WriteLine(unpriced);
            return 1;
        }

        var folder = Path.Combine(
            AppContext.BaseDirectory, "runs", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}");

        Directory.CreateDirectory(folder);

        // Opened before anything can halt: the runs worth reading afterwards are mostly the ones
        // somebody interrupted.
        using var narration = Narration.Open(folder);
        using var events = JsonLinesEventSink.Open(Path.Combine(folder, "events.jsonl"));

        narration.Line(plan.Root.Contract.Goal);

        foreach (var constraint in plan.Root.Contract.Constraints)
            narration.Line($"  · {constraint}");

        narration.Line($"{models.Describe}.");
        narration.Line(options.Attended
            ? "Attended: a person answers what the plan cannot."
            : "Unattended.");
        narration.Line($"Started {DateTime.UtcNow:u}, with up to {TripWorkflow.ChangesAllowed} changes of plan "
            + $"and a budget of {options.Budget:0.00} for the whole run.");

        // The week is ruled on from the plan as it stands in the store, which the run keeps current.
        var store = new InMemoryPlanTreeStore();
        var validate = Validate.Checks(service, ct => store.LoadAsync(plan.PlanId, ct));

        // The plan is checked before anything runs, against the same checks that will have to rule
        // on it — a criterion nothing can decide is refused now rather than discovered mid-run.
        if (PlanAdmission.Faults(plan, [validate]) is { Count: > 0 } faults)
        {
            foreach (var fault in faults)
                Console.Error.WriteLine($"  ✗ {fault}");

            return 1;
        }

        narration.Line(plan.ToOutline());

        // Listens before the first model call: every call publishes what it consumed on the usage
        // meter, tagged with its workflow, and this reads it back per workflow.
        using var usage = new OpenTelemetryBudgetMeter();

        var workflow = TripWorkflow.Build(
            service, store, models, plan, validate, options.Autopilot, options.Budget, narration);

        WorkflowExecution<Trip> execution;

        // A demo that ends in a stack trace has taught the reader about C#, not about plans.
        try
        {
            execution = await DriveAsync(workflow, options.Attended, narration, events);
        }
        catch (InvalidOperationException stopped)
        {
            Console.Error.WriteLine($"  ✗ {stopped.Message}");
            narration.Line($"the run stopped: {stopped.Message}");
            return 1;
        }

        await ReportAsync(execution, options.Budget, models, folder, narration, usage, plan.PlanId);
        return 0;
    }

    /// <summary>
    /// Runs the workflow to completion, answering each halt it stops on where a person is wired in.
    /// </summary>
    /// <remarks>
    /// <b>The pause is not a failure to complete.</b> An escalating run ends every leg
    /// <c>Interrupted</c> and is continued with <c>ResumeAsync</c> carrying the decision — the same
    /// door every other kind of human input uses.
    /// </remarks>
    private static async Task<WorkflowExecution<Trip>> DriveAsync(
        Workflow<Trip> workflow, bool attended, Narration narration, JsonLinesEventSink events)
    {
        using var sink = WorkflowEventReporting.BeginScope(EventSinks.Several(narration, events));

        var execution = await workflow.RunAsync(new Trip());

        while (execution.Status == ExecutionStatus.Interrupted)
        {
            // Autopilot answers inside the workflow, so an unattended run should never get here —
            // and if it does, the honest failure is loud. Prompting would block on a standard input
            // nobody is watching, which is an unattended run's worst ending: it does not finish, it
            // does not fail, and nothing says why.
            if (!attended)
            {
                throw new InvalidOperationException(
                    $"the run paused at '{execution.CurrentJob}' under autopilot, which answers its "
                    + "own questions. Something asked that the workflow was not wired to answer.");
            }

            var coordination = execution.State.Coordination
                ?? throw new InvalidOperationException("the run paused before the plan had halted.");

            var question = coordination.Question
                ?? throw new InvalidOperationException("the run paused with nothing to decide.");

            var answer = Prompt.Ask(question, coordination, narration);

            execution = await workflow.ResumeAsync(execution.Id, paused => paused with
            {
                Coordination = paused.Coordination! with
                {
                    Question = answer switch
                    {
                        { Picked: { } at } => paused.Coordination.Question! with { Picked = at },

                        // Verbatim, and into its own field: the supervisor reads it and offers again
                        // with it settled, rather than anything here guessing which option the
                        // traveller nearly meant.
                        { Said: { } said } => paused.Coordination.Question! with { Said = said },

                        _ => paused.Coordination.Question! with { Refused = PlanRefusal.Cancel }
                    }
                }
            });
        }

        if (execution.Status is ExecutionStatus.Faulted)
        {
            throw new InvalidOperationException(
                $"the run faulted at '{execution.CurrentJob}': "
                + (execution.Result?.Error ?? "no error was recorded."));
        }

        return execution.Result is not null
            ? execution
            : throw new InvalidOperationException("the run produced no completion.");
    }

    private static string Tokens(BudgetSpend spend) =>
        $"{spend.TokensIn:N0} tokens in, {spend.TokensOut:N0} out";

    /// <summary>
    /// What came of it: one sentence, the recommended stays, the proposal, what it cost, and where to
    /// read the rest.
    /// </summary>
    private static async Task ReportAsync(
        WorkflowExecution<Trip> execution, decimal budget, TripModels models, string folder, Narration narration,
        IBudgetMeter usage, string planWorkflow)
    {
        var result = execution.Result!;
        var coordination = result.FinalState.Coordination;
        var final = coordination?.Result.Tree;
        var overBudget = execution.Status is ExecutionStatus.BudgetExceeded;

        narration.Line("");

        // Worded for the trip like its other endings: a budget is how this run was told to stop, not
        // something that went wrong.
        if (overBudget)
            narration.Line($"Stopped: the trip reached its budget of {budget:0.00} before it was planned.");

        if (coordination is not null && final is not null)
        {
            var haltedAt = coordination.Result.HaltedAt;

            // The words the tier uses — Blocked, Unmet, halted — are precise and mean nothing to
            // somebody who has not read the design. Only the trip's own three endings are said here.
            if (!overBudget)
                narration.Line(PlanRunEnding.Describe(coordination, Endings));

            if (haltedAt is not null && coordination.HaltReason is { } why)
                narration.Line($"Why it stopped: {why}");

            if (final.Lineage.Count > 1)
                narration.Line($"The plan changed {final.Lineage.Count - 1} time(s) along the way.");

            if (overBudget || coordination.Result.Outcome is not PlanRunOutcome.Completed)
            {
                narration.Line("The plan as it ended:");
                narration.Line(final.ToOutline());
            }
        }

        var recommended = final is null
            ? []
            : PlannedStay.In(final).Where(stay => stay.State is StepState.Done).OrderBy(stay => stay.CheckIn).ToList();

        foreach (var stay in recommended)
            narration.Line($"  {stay.CheckIn:ddd d MMM} to {stay.CheckOut:ddd d MMM}  {stay.Place,-8} {stay.Choice}");

        var itinerary = Path.Combine(folder, "itinerary.json");
        File.WriteAllText(itinerary, JsonSerializer.Serialize(recommended, Indented));

        var planned = !overBudget && coordination?.Result.Outcome is PlanRunOutcome.Completed && recommended.Count > 0;
        var reviewSpent = planned
            ? await ProposeAsync(models, recommended, budget - execution.EstimatedCost, folder, narration)
            : 0m;

        var spent = execution.EstimatedCost + reviewSpent;
        narration.Line($"Spent {spent:0.0000} of {budget:0.00}: {execution.EstimatedCost:0.0000} planning"
            + (planned ? $", {reviewSpent:0.0000} reviewing the proposal." : "."));

        // The same spend, read from the usage meter rather than from the runs: it should agree.
        var planning = usage.GetCurrentSpend(planWorkflow);
        var reviewing = usage.GetCurrentSpend(ProposalReview.WorkflowName);
        narration.Line($"The usage meter read {planning.EstimatedUsd + reviewing.EstimatedUsd:0.0000}: "
            + $"{planning.EstimatedUsd:0.0000} planning ({Tokens(planning)})"
            + (planned ? $", {reviewing.EstimatedUsd:0.0000} reviewing the proposal ({Tokens(reviewing)})." : "."));

        narration.Line($"Itinerary: {itinerary}");
        narration.Line($"Run log: {Narration.PathIn(folder)}");
        narration.Line($"Events: {Path.Combine(folder, "events.jsonl")}");
    }

    /// <summary>Has the proposal written and checked, says how that ended, and returns what it cost.</summary>
    private static async Task<decimal> ProposeAsync(
        TripModels models, IReadOnlyList<PlannedStay> recommended, decimal remaining, string folder, Narration narration)
    {
        narration.Line("");

        if (remaining <= 0)
        {
            narration.Line("No budget was left to write the proposal.");
            return 0m;
        }

        var outcome = await ProposalReview.RunAsync(models, recommended, remaining);

        narration.Line(outcome switch
        {
            { OverBudget: true } =>
                "The proposal's review reached the budget before it finished; the draft was not checked clean.",
            { Approved: true } =>
                $"Proposal checked against the itinerary: approved after {outcome.Rounds} round(s).",
            _ => $"Proposal not approved after {ProposalReview.RoundsAllowed} rounds. The check still found:"
        });

        if (!outcome.Approved)
        {
            foreach (var mismatch in outcome.Mismatches)
                narration.Line($"  ✗ {mismatch}");
        }

        var proposal = Path.Combine(folder, "proposal.md");
        File.WriteAllText(proposal, $"# Your trip{Environment.NewLine}{Environment.NewLine}{outcome.Draft}{Environment.NewLine}"
            + (outcome.Approved
                ? ""
                : $"{Environment.NewLine}## Not checked clean{Environment.NewLine}{Environment.NewLine}"
                  + string.Join(Environment.NewLine, outcome.Mismatches.Select(m => $"- {m}")) + Environment.NewLine));

        narration.Line($"Proposal: {proposal}");
        return outcome.Spent;
    }

}
