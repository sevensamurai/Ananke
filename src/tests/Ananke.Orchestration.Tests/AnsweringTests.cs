using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a person is actually shown at a halt, and what the three things they can answer do.
/// </summary>
/// <remarks>
/// <para>
/// <b>Narrowing is only safe because it cannot trap anybody (R32).</b> A supervisor that offers three
/// options has done the work of deciding what is genuinely distinct — and that is worth having only
/// while somebody who thinks all three are wrong can say so. So every question carries two answers
/// nobody authored: <em>say something else</em>, which goes back to the supervisor, and <em>cancel</em>,
/// which ends the run. Neither is ever in the list, and no supervisor can forget to offer them.
/// </para>
/// <para>
/// <b>Giving one step up moved the other way, and that is the interesting half.</b> It used to be a
/// refusal — reached past the supervisor, weighed by nobody — which made dropping a step the one
/// course of action that never had to compete with the alternatives to it. It is an authored option
/// now: offered, comparable, recommendable, and takeable by an unattended run.
/// </para>
/// </remarks>
[TestFixture]
public class AnsweringTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private sealed record Trip
    {
        public PlanCoordination? Coordination { get; init; }
    }

    // ── The recommendation is required, because something takes it without asking ──

    [Test]
    public async Task AProposal_RecommendsExactlyOne_WhateverTheModelMarked()
    {
        // An unattended run takes the recommendation, so "none" and "all three" are the same
        // unanswered question — and a caller reading Recommended must never find two, because
        // whatever answers unattended takes the first it sees.
        foreach (var marks in new[] { new[] { false, false }, new[] { true, true } })
        {
            var proposal = await Proposing(Options(
                (Summary: "later", Recommended: marks[0]),
                (Summary: "smaller", Recommended: marks[1])));

            proposal.Options.Count(o => o.Recommended).ShouldBe(1);
            proposal.Recommended.ShouldNotBeNull();
        }
    }

    [Test]
    public async Task APlanAlteringOption_IsAmongThem_AndTheRecommendationIsOneOfTheOptions()
    {
        // S6's pin. The supervisor's own pick has to be on the list it offered — a recommendation
        // that is not one of the options is a fourth answer nobody can take.
        var proposal = await Proposing(Options(
            (Summary: "later", Recommended: false),
            (Summary: "smaller", Recommended: true)));

        proposal.Options.ShouldContain(o => o.Replan);   // something asks for the plan to change
        proposal.Options.ShouldContain(proposal.Recommended!);
    }

    // ── Dropping a step is an authored option, not a refusal ──

    [Test]
    public async Task AnOptionThatDropsTheStep_AbandonsItWhenTakenAndLeavesTheRestOfThePlan()
    {
        var (running, store) = Choosing(Answered(new PlanQuestion
        {
            Options = [new PlanOption { Summary = "go without it", Abandon = "no room for three nights" }],
            Picked = 1
        }));

        var after = await running;

        // The step is given up, not the run: nothing is left halted, and no version was minted for a
        // plan that was not re-authored.
        after.Coordination!.Result.HaltedAt.ShouldBeNull();
        after.Coordination.Result.Tree.Lineage.Count.ShouldBe(1);
        after.Coordination.Question.ShouldBeNull();

        var saved = (await store.LoadAsync("trip")).ShouldNotBeNull();
        saved.Node("day-1").Abandonment.ShouldNotBeNull().Reason.ShouldBe("no room for three nights");
    }

    [Test]
    public async Task AnOptionThatBothDropsTheStepAndAsksForAReplan_IsNotAnOption()
    {
        // The same rule an ordinary option is held to: if the supervisor can act one way, it must
        // not also claim the other. An option carrying both would apply neither half of what it said.
        var proposal = await Proposing($$"""
            { "options": [ {
                "summary": "drop it and also fix it",
                "abandon": "not worth the nights",
                "replan": true,
                "recommended": true } ] }
            """);

        proposal.Options.ShouldBeEmpty();
        proposal.Discarded.ShouldAllBe(d => d.Contains("both"));
        proposal.Discarded.Count.ShouldBe(2); // asked twice, and both times said the same thing
    }

    // ── Saying something else ──

    [Test]
    public async Task AnAnswerOffTheList_GoesBackToTheSupervisor_AndDecidesNothingItself()
    {
        // S7b's first half. Nothing in the loop can read prose, so an off-list answer is not applied,
        // not treated as a refusal, and above all not discarded: it is carried, verbatim, to the seat
        // that authors options — and the halt is deliberately left standing, because this settled
        // nothing about the step.
        var (running, _) = Choosing(Answered(new PlanQuestion
        {
            Options = [new PlanOption { Summary = "drop it" }],
            Said = "keep Naoshima, find the slack elsewhere"
        }));

        var after = await running;

        after.Coordination!.Said.ShouldBe("keep Naoshima, find the slack elsewhere");
        after.Coordination.Question.ShouldBeNull();
        after.Coordination.Result.HaltedAt.ShouldBe("day-1");
        after.Coordination.Decision.ShouldBeNull();
    }

    [Test]
    public void TheSupervisorIsToldWhatWasSaid_Verbatim_AndOnlyWhenSomethingWas()
    {
        PlanHaltRecord.Said(Halted()).ShouldBeEmpty();

        var told = PlanHaltRecord.Said(Halted() with { Said = "keep Naoshima, drop something else" });

        told.ShouldContain("keep Naoshima, drop something else");
    }

    [Test]
    public async Task AskingAgain_ClearsWhatWasSaid_SoItSteersOneRoundAndNotEveryLaterOne()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Halted().Result.Tree).ConfigureAwait(false);

        var asked = new PlanAskingJob<Trip>(
            "propose",
            (_, _) => Task.FromResult(new PlanProposal
            {
                Options = [new PlanOption { Summary = "swap it", Replan = true }]
            }),
            new SupervisionOptions { Store = store },
            state => state.Coordination,
            (state, c) => state with { Coordination = c });

        var after = await asked.ExecuteAsync(
            new Trip { Coordination = Halted() with { Said = "keep Naoshima" } });

        after.Coordination!.Question.ShouldNotBeNull();
        after.Coordination.Said.ShouldBeNull();
    }

    // ── Reporting ──

    [Test]
    public async Task Asking_ReportsWhatWasOfferedAndWhatWasDiscarded()
    {
        var sink = new Collecting();
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Halted().Result.Tree);

        var asked = new PlanAskingJob<Trip>(
            "propose",
            (_, _) => Task.FromResult(new PlanProposal
            {
                Options = [new PlanOption { Summary = "change the plan", Replan = true, Recommended = true }],
                Discarded = ["\"shift it\": only one replan is offered"]
            }),
            new SupervisionOptions { Store = store },
            state => state.Coordination,
            (state, c) => state with { Coordination = c });

        using (WorkflowEventReporting.BeginScope(sink))
            await asked.ExecuteAsync(new Trip { Coordination = Halted() });

        var offered = sink.Events.OfType<PlanProposalOffered>().ShouldHaveSingleItem();
        offered.NodeId.ShouldBe("day-1");
        offered.HaltReason.ShouldBe("there is not enough room");
        offered.Options.ShouldHaveSingleItem().Replan.ShouldBeTrue();
        offered.Discarded.ShouldHaveSingleItem();
    }

    // ── Cancelling ──

    [Test]
    public async Task Cancelling_EndsTheRun_AndIsNotMistakenForHavingChosenNothing()
    {
        var (running, _) = Choosing(Answered(new PlanQuestion
        {
            Options = [new PlanOption { Summary = "drop it" }],
            Refused = PlanRefusal.Cancel
        }));

        var after = await running;

        after.Coordination!.Decision.ShouldBeOfType<PlanDecision.AskPlan>();
        after.Coordination.Question.ShouldBeNull();
        after.Coordination.Said.ShouldBeNull();
    }

    // ── Fixtures ──

    private static (Task<Trip> State, InMemoryPlanTreeStore Store) Choosing(PlanCoordination answered)
    {
        var store = new InMemoryPlanTreeStore();
        store.SaveAsync(answered.Result.Tree).GetAwaiter().GetResult();

        var supervision = new SupervisionOptions { Store = store };

        var job = new PlanChoiceJob<Trip>(
            "choose", supervision,
            state => state.Coordination,
            (state, c) => state with { Coordination = c });

        return (job.ExecuteAsync(new Trip { Coordination = answered }), store);
    }

    private static PlanCoordination Answered(PlanQuestion question) => Halted() with { Question = question };

    private static async Task<PlanProposal> Proposing(string reply) =>
        await new AgentPlanAdvisor(new SupervisionOptions
        {
            Supervisor = new Fixed(reply),
            Store = new InMemoryPlanTreeStore()
        }).ProposeAsync(Halted()).ConfigureAwait(false);

    /// <summary>A replan option and one that gives the step up, marked as the caller says.</summary>
    private static string Options(
        (string Summary, bool Recommended) first, (string Summary, bool Recommended) second) =>
        JsonSerializer.Serialize(new
        {
            options = new object[]
            {
                new { summary = first.Summary, replan = true, rationale = first.Summary, recommended = first.Recommended },
                new { summary = second.Summary, abandon = second.Summary, recommended = second.Recommended }
            }
        });

    private static AgentContract Contract(string goal) =>
        new() { Goal = goal, AcceptanceCriteria = ["fits(day-1)"] };

    private static PlanCoordination Halted()
    {
        var tree = PlanTree.Create(
                "trip",
                new AgentContract { Goal = "Plan a trip", AcceptanceCriteria = ["fits(root)"] },
                [("day-1", Contract("Plan day 1"))])
            .WithViolation("day-1", new PlanViolation
            {
                Criterion = "fits(day-1)",
                Reason = "there is not enough room",
                At = T0
            });

        return new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["day-1"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "day-1"
            }
        };
    }

    private sealed class Fixed(string reply) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = reply });
    }

    private sealed class Collecting : IWorkflowEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];

        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
