using System.Text.Json;
using Ananke.Design;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
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
        narration.Line($"Started {DateTime.UtcNow:u}, with up to {TripWorkflow.ChangesAllowed} changes of plan.");

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

        var workflow = TripWorkflow.Build(
            service, store, models, plan, validate, options.Autopilot, narration);

        WorkflowResult<Trip> result;

        // A demo that ends in a stack trace has taught the reader about C#, not about plans.
        try
        {
            result = await DriveAsync(workflow, options.Attended, narration, events);
        }
        catch (InvalidOperationException stopped)
        {
            Console.Error.WriteLine($"  ✗ {stopped.Message}");
            narration.Line($"the run stopped: {stopped.Message}");
            return 1;
        }

        Report(result, folder, narration);
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
    private static async Task<WorkflowResult<Trip>> DriveAsync(
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

        return execution.Result
            ?? throw new InvalidOperationException("the run produced no completion.");
    }

    /// <summary>What came of it: one sentence, the recommended stays, and where to read the rest.</summary>
    private static void Report(WorkflowResult<Trip> result, string folder, Narration narration)
    {
        var coordination = result.FinalState.Coordination;
        var final = coordination?.Result.Tree;

        narration.Line("");

        if (coordination is not null && final is not null)
        {
            var haltedAt = coordination.Result.HaltedAt;

            // The words the tier uses — Blocked, Unmet, halted — are precise and mean nothing to
            // somebody who has not read the design. Only the trip's own three endings are said here.
            narration.Line(PlanRunEnding.Describe(coordination, Endings));

            if (haltedAt is not null && coordination.HaltReason is { } why)
                narration.Line($"Why it stopped: {why}");

            if (final.Lineage.Count > 1)
                narration.Line($"The plan changed {final.Lineage.Count - 1} time(s) along the way.");

            if (coordination.Result.Outcome is not PlanRunOutcome.Completed)
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

        narration.Line($"Itinerary: {itinerary}");
        narration.Line($"Run log: {Narration.PathIn(folder)}");
        narration.Line($"Events: {Path.Combine(folder, "events.jsonl")}");
    }

}
