using Ananke.Learning.EmpiricalMemory;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Ananke.Organics.Memory;
using Shouldly;

namespace Ananke.Organics.Tests.Memory;

/// <summary>
/// Terms crossing a run boundary: proposed at the far end, never binding on arrival.
/// </summary>
/// <remarks>
/// <para>
/// <b>Within one run a term binds directly, and that is safe because the person who said it is the
/// person whose run it is.</b> Across runs it is not: a preference stated last week, about a plan
/// that may no longer resemble this one, binding work nobody has looked at today is exactly how a
/// plan gets quietly narrowed by something nobody here agreed to. So this half only ever
/// <em>proposes</em>, and the tests that matter are the ones asserting what it does <b>not</b> do.
/// </para>
/// <para>
/// <b>Absent the wiring, nothing changes.</b> That is the third test, and it is the one that makes
/// the feature honestly optional rather than merely off by default.
/// </para>
/// </remarks>
[TestFixture]
public class RememberedPlanTermsTests
{
    private const string Reading = "Naoshima stays on the trip";
    private const string Said = "keep Naoshima, and find the slack somewhere else";

    // ── It proposes ──

    [Test]
    public async Task ATermSettledInOneRun_IsOfferedAtALaterRunsHalt()
    {
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
        var remembering = new RememberedPlanTerms(memory);

        await Settle(remembering, "trip-january");

        var recalled = await remembering.RecallAsync(Halted("trip-march"));

        var term = recalled.ShouldHaveSingleItem();
        term.Reading.ShouldBe(Reading);

        // The words it was read from crossed the boundary intact, and so did who said them. An entry
        // that arrived without them would be an assertion with no source.
        term.Said.ShouldBe(Said);
        term.By.ShouldBe("the traveller");
    }

    [Test]
    public async Task WhatIsRecalled_ReachesTheSupervisorMarkedAsRememberedRatherThanStated()
    {
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
        var remembering = new RememberedPlanTerms(memory);

        await Settle(remembering, "trip-january");

        PlanCoordination? asked = null;

        var advisor = remembering.Proposing((coordination, _) =>
        {
            asked = coordination;
            return Task.FromResult(new PlanProposal { Options = [] });
        });

        await advisor(Halted("trip-march"), CancellationToken.None);

        // It arrives in its own field. Nothing here has been said in this run, so nothing here may
        // appear anywhere a reader would take for something that was.
        asked!.Recalled.ShouldHaveSingleItem().Reading.ShouldBe(Reading);
        asked.Said.ShouldBeNull();
        asked.Result.Tree.Terms.ShouldBeEmpty();
    }

    // ── And it binds nothing ──

    [Test]
    public async Task ARecalledTerm_BindsNothing_UntilThisRunCommitsIt()
    {
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
        var remembering = new RememberedPlanTerms(memory);

        await Settle(remembering, "trip-january");

        var coordination = Halted("trip-march");
        var recalled = await remembering.RecallAsync(coordination);

        recalled.ShouldNotBeEmpty();

        // Re-ruling with the recall merely in hand carries nothing into the contract: what binds is
        // the tree's own Terms, and nothing has been committed to this run's tree.
        var reruled = coordination.Result.Tree.Rerule(
            "day-1", Contract("Plan day 1, shorter"), "it did not fit");

        reruled.Node("day-1").Contract.Constraints.ShouldNotContain(Reading);
        reruled.Terms.ShouldBeEmpty();
    }

    [Test]
    public async Task AdoptingARecalledTerm_BindsIt_AndKeepsWhoOriginallySaidIt()
    {
        // The whole point of proposing: adopted, it goes through exactly the call a person's own
        // answer goes through, and from then on it binds like any other term of this run.
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
        var remembering = new RememberedPlanTerms(memory);

        await Settle(remembering, "trip-january");

        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree("trip-march"));

        var executor = new PlanExecutor(store);
        var recalled = (await remembering.RecallAsync(Halted("trip-march"))).ShouldHaveSingleItem();

        await executor.CommitAsync("trip-march", recalled.Reading, recalled.Said, recalled.By);
        await executor.ReruleAsync("trip-march", "day-1", Contract("Plan day 1, shorter"), "it did not fit");

        var tree = (await store.LoadAsync("trip-march")).ShouldNotBeNull();

        tree.Node("day-1").Contract.Constraints.ShouldContain(Reading);

        // Adopted, not re-said. A reader has to be able to see it was first said elsewhere.
        var term = tree.Terms.ShouldHaveSingleItem();
        term.Said.ShouldBe(Said);
        term.By.ShouldBe("the traveller");
    }

    // ── And nothing recalls into a run that did not ask for it ──

    [Test]
    public async Task WithNothingWired_ARunBehavesExactlyAsItDidBefore()
    {
        // The claim that makes this optional rather than merely default-off: a plan tier with no
        // adapter anywhere near it recalls nothing, because it knows of nothing to recall from.
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(Tree("trip-march"));

        var coordination = Halted("trip-march");

        coordination.Recalled.ShouldBeEmpty();

        var reruled = coordination.Result.Tree.Rerule(
            "day-1", Contract("Plan day 1, shorter"), "it did not fit");

        reruled.Node("day-1").Contract.Constraints.ShouldNotContain(Reading);
    }

    [Test]
    public async Task ATermAlreadyInForceHere_IsNotOfferedBackAsSomethingToAdopt()
    {
        // It is bound already. Offering it again invites a supervisor to re-adopt what it could not
        // change anyway, and puts a line on the page that reads as a live question.
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
        var remembering = new RememberedPlanTerms(memory);

        await Settle(remembering, "trip-january");

        var already = Halted("trip-march");
        var bound = already.Result.Tree.WithTerm(new PlanTerm
        {
            Id = "local",
            Reading = Reading,
            Said = "said again, here",
            By = "the traveller",
            At = DateTimeOffset.UtcNow
        });

        var recalled = await remembering.RecallAsync(
            already with { Result = already.Result with { Tree = bound } });

        recalled.ShouldBeEmpty();
    }

    [Test]
    public async Task AHaltTheTermHasNothingToDoWith_IsNotOfferedIt()
    {
        // Found by probing rather than by reasoning, and it was the feature's real defect:
        // RecallOptions defaults every threshold to zero, so one remembered term came back for
        // *every* halt in every later run — including "xylophone quarterly tax reconciliation".
        // A supervisor shown a preference that has nothing to do with the halt is a supervisor being
        // invited to narrow a plan on nobody's authority, which is the whole thing this must not do.
        //
        // Scores measured with InMemoryEmbedder against "Naoshima stays on the trip": 0.41 for the
        // step it was about, 0.32 for another day of the same trip, 0.16 and 0.15 for the two below.
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
        var remembering = new RememberedPlanTerms(memory);

        await Settle(remembering, "trip-january");

        foreach (var unrelated in new[]
        {
            "Upgrade the logging library to version 4",
            "xylophone quarterly tax reconciliation"
        })
        {
            (await remembering.RecallAsync(HaltedOn(unrelated)))
                .ShouldBeEmpty($"a term about Naoshima was offered for \"{unrelated}\"");
        }

        // And the control: the step it genuinely is about still gets it, so the floor did not simply
        // turn the feature off.
        (await remembering.RecallAsync(Halted("trip-march"))).ShouldNotBeEmpty();
    }

    // ── Taking it back, versus this plan not managing it ──

    [Test]
    public async Task APersonRetractingATerm_IsNotOfferedItAgainInALaterRun()
    {
        // "Actually, drop Naoshima." That is about what they want, and it is true from here on — so a
        // later run offering the old preference back would be the run arguing with them.
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
        var remembering = new RememberedPlanTerms(memory);

        await Settle(remembering, "trip-january");
        await End(remembering, PlanTermEnd.Retracted, "the traveller would rather see Kanazawa");

        (await remembering.RecallAsync(Halted("trip-march"))).ShouldBeEmpty();
    }

    [Test]
    public async Task ATermThisPlanCouldNotHold_IsStillOfferedNextTime()
    {
        // The half that makes the distinction worth having. The January trip could not fit Naoshima
        // and let the term go to finish; nobody stopped wanting it. Forgetting it here is how a run
        // makes somebody ask for the same thing again — which is the failure terms exist to prevent.
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
        var remembering = new RememberedPlanTerms(memory);

        await Settle(remembering, "trip-january");
        await End(remembering, PlanTermEnd.Waived, "the January dates could not fit the ferry");

        var recalled = await remembering.RecallAsync(Halted("trip-march"));

        recalled.ShouldHaveSingleItem().Reading.ShouldBe(Reading);
    }

    [Test]
    public async Task ARetraction_ForgetsOnlyWhatWasTakenBack()
    {
        // A retraction is narrow by nature: somebody took back one thing they said. A neighbour that
        // merely scores well is a preference nobody withdrew.
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
        var remembering = new RememberedPlanTerms(memory);

        await Settle(remembering, "trip-january");
        await remembering.Sink().ReportAsync(Committed("trip-january", "t2", "the ferry is booked early"));

        await End(remembering, PlanTermEnd.Retracted, "they would rather see Kanazawa");

        var left = await memory.RecallAsync(
            "the ferry is booked early", new RecallOptions { TopK = 5, Kind = EmpiricalKind.Heuristic });

        left.ShouldContain(m => m.Entry.Description.Summary == "the ferry is booked early");
    }

    [Test]
    public async Task ChangingTheirMindBack_IsHeard()
    {
        // The half that makes a retraction survivable rather than permanent. The store merges a
        // re-stated term into the entry it already has and deliberately leaves confidence alone, so
        // without restoring it a term retracted once could never be settled again: every later run
        // would hear "keep Naoshima after all", bind it for that run, and forget it. That is the exact
        // repetition this type exists to stop, made permanent by the fix for the opposite problem.
        var memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder());
        var remembering = new RememberedPlanTerms(memory);

        await Settle(remembering, "trip-january");
        await End(remembering, PlanTermEnd.Retracted, "they would rather see Kanazawa");
        await Settle(remembering, "trip-may");

        (await remembering.RecallAsync(Halted("Plan day 1 — Naoshima, and the ferry")))
            .ShouldHaveSingleItem().Reading.ShouldBe(Reading);
    }

    // ── Fixtures ──

    /// <summary>Ends a term the way a run would, through the event the plan tier raises.</summary>
    private static async Task End(RememberedPlanTerms remembering, PlanTermEnd end, string reason) =>
        await remembering.Sink().ReportAsync(new PlanTermContradicted
        {
            WorkflowName = "trip",
            ExecutionId = "trip-january",
            PlanId = "trip-january",
            PlanVersion = 1,
            TermId = "t1",
            Reading = Reading,
            Reason = reason,
            End = end,
            By = "the traveller"
        });

    private static PlanTermCommitted Committed(string planId, string termId, string reading) => new()
    {
        WorkflowName = "trip",
        ExecutionId = planId,
        PlanId = planId,
        PlanVersion = 1,
        TermId = termId,
        Reading = reading,
        Said = reading,
        By = "the traveller"
    };

    /// <summary>Settles a term in one run, through the event the plan tier already raises.</summary>
    private static async Task Settle(RememberedPlanTerms remembering, string planId)
    {
        await remembering.Sink().ReportAsync(new PlanTermCommitted
        {
            WorkflowName = "trip",
            ExecutionId = planId,
            PlanId = planId,
            PlanVersion = 1,
            TermId = "t1",
            Reading = Reading,
            Said = Said,
            By = "the traveller"
        });
    }

    private static AgentContract Contract(string goal) =>
        new() { Goal = goal, AcceptanceCriteria = ["fits(day-1)"] };

    private static PlanTree Tree(string planId) => PlanTree.Create(
        planId,
        new AgentContract { Goal = "Plan a trip", AcceptanceCriteria = ["fits(trip)"] },
        [("day-1", Contract("Plan day 1 — Naoshima, and the ferry"))]);

    /// <summary>A halt on a step whose goal is whatever is asked for.</summary>
    private static PlanCoordination HaltedOn(string goal)
    {
        var tree = PlanTree.Create(
                "elsewhere",
                new AgentContract { Goal = "Do the work", AcceptanceCriteria = ["done()"] },
                [("step", new AgentContract { Goal = goal, AcceptanceCriteria = ["done()"] })])
            .WithViolation("step", new PlanViolation
            {
                Criterion = "done()",
                Reason = "it did not",
                At = DateTimeOffset.UtcNow
            });

        return new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["step"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "step"
            }
        };
    }

    private static PlanCoordination Halted(string planId) => new()
    {
        Result = new PlanRunResult
        {
            Tree = Tree(planId).WithViolation("day-1", new PlanViolation
            {
                Criterion = "fits(day-1)",
                Reason = "there is not enough room",
                At = DateTimeOffset.UtcNow
            }),
            Executed = ["day-1"],
            Skipped = [],
            Rulings = new Dictionary<string, Verification>(),
            HaltedAt = "day-1"
        }
    };
}
