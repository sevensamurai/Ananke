using System.Text.Json;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A supervisor that offers alternatives and says which one it would take.
/// </summary>
/// <remarks>
/// <para>
/// The difference between <em>what would you do</em> and <em>what are my options</em>. A coordinator
/// answers the first, which is what an unattended run needs; an advisor answers the second, which is
/// what anything asking somebody else needs — and offering one proposal beside "or stop" is the first
/// question wearing the second's clothes.
/// </para>
/// <para>
/// What is pinned here is mostly what the advisor refuses to pass on: an option that neither gives
/// the step up nor asks for a replan, and a recommendation that is not exactly one.
/// </para>
/// </remarks>
[TestFixture]
public class PlanAdvisorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AHalt_YieldsAlternativesWithExactlyOneRecommended()
    {
        var proposal = await Advisor(Options(
            new { summary = "Split it in two", replan = true, rationale = "handles both dialects", recommended = true },
            new { summary = "Drop the legacy one", abandon = "the legacy dialect is rare", recommended = false }))
            .ProposeAsync(Halted());

        proposal.Options.Count.ShouldBe(2);
        proposal.Options.Count(o => o.Recommended).ShouldBe(1);
        proposal.Recommended.ShouldNotBeNull().Summary.ShouldBe("Split it in two");
        proposal.Options.Count(o => o.Replan).ShouldBe(1);
    }

    [Test]
    public async Task AnOptionThatNeitherReplansNorGivesUp_IsNotAnOption()
    {
        // An option with neither a replan nor a reason to give the step up says nothing usable.
        // Passing it on would put a choice in front of somebody that cannot settle the plan
        // whichever way they answer.
        var proposal = await Advisor(Options(
            new { summary = "Try harder", recommended = true },
            new { summary = "Split it in two", replan = true, recommended = false }))
            .ProposeAsync(Halted());

        proposal.Options.ShouldHaveSingleItem().Summary.ShouldBe("Split it in two");

        // And the recommendation moves rather than vanishing: the one that survived is the only one
        // left to take, so a caller reading Recommended still finds something.
        proposal.Recommended.ShouldNotBeNull().Summary.ShouldBe("Split it in two");
    }

    [Test]
    public async Task ASupervisorThatRecommendsNothing_StillLeavesExactlyOneRecommendation()
    {
        // A caller answering unattended takes the recommendation. If a model declines to name one,
        // the honest fallback is its own first answer — not no answer, which would strand the run.
        var proposal = await Advisor(Options(
            new { summary = "Split it in two", replan = true, recommended = false },
            new { summary = "Drop the legacy one", abandon = "the legacy dialect is rare", recommended = false }))
            .ProposeAsync(Halted());

        proposal.Options.Count(o => o.Recommended).ShouldBe(1);
        proposal.Recommended.ShouldNotBeNull().Summary.ShouldBe("Split it in two");
    }

    [Test]
    public async Task ASupervisorWithNothingToOffer_SaysSo()
    {
        // Not an exception and not a decision: an empty proposal is a real answer, and whoever asked
        // still has the way out they were always going to have.
        var proposal = await Advisor(Options()).ProposeAsync(Halted());

        proposal.Empty.ShouldBeTrue();
        proposal.Recommended.ShouldBeNull();
    }

    [Test]
    public void APlanThatDidNotHalt_HasNothingToOfferAlternativesTo()
    {
        var settled = new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = Tree(),
                Executed = [],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = null
            }
        };

        Should.Throw<InvalidOperationException>(() => Advisor(Options()).ProposeAsync(settled))
            .Message.ShouldContain("did not halt");
    }

    [Test]
    public async Task AnAlternative_SurvivesBeingWrittenDownAndReadBack()
    {
        // An alternative exists to be shown to somebody, so the run is usually paused while it is —
        // and a paused run is a checkpoint. An earlier design carried a PlanDecision here and died on
        // resume: System.Text.Json will not deserialise an abstract type, a long way from where the
        // mistake was made.
        var proposal = await Advisor(Options(
            new { summary = "Change the plan", replan = true, rationale = "the step is the wrong shape", recommended = true }))
            .ProposeAsync(Halted());

        var json = JsonSerializer.Serialize(proposal);
        var back = JsonSerializer.Deserialize<PlanProposal>(json).ShouldNotBeNull();

        back.Options.ShouldHaveSingleItem();
        back.Recommended.ShouldNotBeNull().Replan.ShouldBeTrue();
        back.Options[0].Rationale!.Text.ShouldBe("the step is the wrong shape");
    }

    [Test]
    public async Task WhenTheRecommendedOptionIsDropped_TheRecommendationMovesToOneThatSurvived()
    {
        // A caller reading Recommended must never find none — the thing that answers unattended
        // takes the first it sees, and a proposal whose only recommendation was discarded would
        // hand it nothing while still looking like a choice.
        var proposal = await Advisor(Options(
            new { summary = "Both at once", replan = true, abandon = "not worth keeping", recommended = true },
            new { summary = "Split it in two", replan = true, recommended = false }))
            .ProposeAsync(Halted());

        proposal.Options.ShouldHaveSingleItem().Summary.ShouldBe("Split it in two");
        proposal.Recommended.ShouldNotBeNull().Summary.ShouldBe("Split it in two");
        proposal.Discarded.ShouldHaveSingleItem().ShouldContain("both gives the step up");
    }

    [Test]
    public async Task TwoReplanOptions_KeepTheRecommendedOne_AndSayWhyTheOtherWent()
    {
        var proposal = await Advisor(Options(
            new { summary = "Shift the dates", replan = true, rationale = "the dates are taken", recommended = false },
            new { summary = "Hakone has one free night", replan = true, rationale = "only 2027-04-07 is free", recommended = true }))
            .ProposeAsync(Halted());

        proposal.Options.ShouldHaveSingleItem().Summary.ShouldBe("Hakone has one free night");
        proposal.Discarded.ShouldHaveSingleItem().ShouldContain("only one replan is offered");
    }

    [Test]
    public async Task TwoReplanOptions_NeitherRecommended_KeepTheFirst()
    {
        var proposal = await Advisor(Options(
            new { summary = "Shift the dates", replan = true, recommended = false },
            new { summary = "Hakone has one free night", replan = true, recommended = false }))
            .ProposeAsync(Halted());

        var kept = proposal.Options.ShouldHaveSingleItem();
        kept.Summary.ShouldBe("Shift the dates");
        kept.Recommended.ShouldBeTrue();
    }

    [Test]
    public async Task TheAsk_AsksForTheFindingAndNotForTheChange()
    {
        string? prompt = null;

        await new AgentPlanAdvisor(new SupervisionOptions
        {
            Supervisor = new SimulatedAgentModel(request =>
            {
                prompt = string.Join('\n', request.Messages.Select(m => m.Content));
                return Options();
            }),
            Store = new InMemoryPlanTreeStore()
        }).ProposeAsync(Halted());

        prompt.ShouldNotBeNull().ShouldContain("names the finding");
        prompt.ShouldNotContain("what should change");
    }

    [Test]
    public async Task TheAsk_ShowsWhatThePlanAlreadyTried()
    {
        string? prompt = null;

        var tree = Tree()
            .Rerule(
                "plan",
                new AgentContract { Goal = "Ship it", AcceptanceCriteria = ["it ships"] },
                "the input has two dialects",
                [("parse-strict", new AgentContract { Goal = "Parse the documented dialect", AcceptanceCriteria = ["the strict parser round-trips"] })])
            .WithViolation("parse-strict", new PlanViolation
            {
                Criterion = "the strict parser round-trips",
                Reason = "half the files use the other dialect",
                At = T0
            });

        var halted = new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["parse-strict"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "parse-strict"
            }
        };

        await new AgentPlanAdvisor(new SupervisionOptions
        {
            Supervisor = new SimulatedAgentModel(request =>
            {
                prompt = string.Join('\n', request.Messages.Select(m => m.Content));
                return Options();
            }),
            Store = new InMemoryPlanTreeStore()
        }).ProposeAsync(halted);

        prompt.ShouldNotBeNull().ShouldContain("What this plan already tried");
        prompt.ShouldContain("parse [the parser round-trips]");
        prompt.ShouldContain("the input has two dialects");
    }

    [Test]
    public async Task ProposeAsync_AStepsOptions_OffersEveryOneAndRecommendsTheSupervisorsChoice()
    {
        var proposal = await Advisor(Chose(2, "both dialects are in the input"))
            .ProposeAsync(HaltedWithOptions());

        proposal.Options.Select(o => o.Summary)
            .ShouldBe(["parse the documented dialect only", "add a second parser"]);
        proposal.Options.ShouldAllBe(o => o.Replan);
        proposal.Options[0].Recommended.ShouldBeFalse();
        proposal.Options[1].Recommended.ShouldBeTrue();
        proposal.Options[1].Rationale.ShouldNotBeNull().Text.ShouldContain("both dialects are in the input");
        proposal.Discarded.ShouldBeEmpty();
    }

    [Test]
    public async Task ProposeAsync_APositionNoOptionHas_RecommendsTheFirstAndSaysSo()
    {
        // The step's list is what there is to choose from, so an answer outside it decides nothing.
        // Recommending the first is the same fallback a text answer nobody listed used to get.
        var proposal = await Advisor(Chose(7, "simpler")).ProposeAsync(HaltedWithOptions());

        proposal.Options.Count.ShouldBe(2);
        proposal.Options[0].Recommended.ShouldBeTrue();
        proposal.Options[1].Recommended.ShouldBeFalse();
        proposal.Discarded.ShouldContain(d => d.Contains("7"));
    }

    [Test]
    public async Task ProposeAsync_WhatAPersonSaidAtAStepsOptions_IsInThePrompt()
    {
        string? prompt = null;

        await new AgentPlanAdvisor(new SupervisionOptions
        {
            Supervisor = new SimulatedAgentModel(request =>
            {
                prompt = string.Join('\n', request.Messages.Select(m => m.Content));
                return Chose(2, "they asked for both dialects");
            }),
            Store = new InMemoryPlanTreeStore()
        }).ProposeAsync(HaltedWithOptions() with { Said = "we need both dialects" });

        prompt.ShouldNotBeNull().ShouldContain("we need both dialects");
        prompt.ShouldContain("add a second parser");
    }

    [Test]
    public async Task ProposeAsync_OptionsFromAStepThatDidItsTask_AreOfferedAsAnswers()
    {
        var halted = HaltedWithOptions();
        var asked = halted.Result.Tree.Node("parse").Question!;

        var proposal = await Advisor(Chose(2, "both dialects are in the input"))
            .ProposeAsync(halted with
            {
                Result = halted.Result with
                {
                    Tree = halted.Result.Tree.WithQuestion("parse", asked with { Done = true })
                }
            });

        proposal.Options.Count.ShouldBe(2);
        proposal.Options.ShouldAllBe(o => o.Answer != null && !o.Replan);
        proposal.Options.Single(o => o.Recommended).Answer.ShouldBe("add a second parser");
    }

    [Test]
    public void HaltReason_AStepStoppedOnItsOptions_IsWhatItAsks()
    {
        var coordination = new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = Tree().WithQuestion("parse", new NodeQuestion
                {
                    Asks = "two parsers meet the contract",
                    Options = ["the fast parser", "the strict parser"],
                    Done = true,
                    At = T0
                }),
                Executed = ["parse"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "parse"
            }
        };

        coordination.HaltReason.ShouldBe("two parsers meet the contract");
    }

    [Test]
    public void HaltReason_AStepBlockedWithNoOptions_IsWhatItReported()
    {
        var tree = Tree()
            .WithReading("parse", new NodeReading
            {
                At = T0,
                PlanVersion = 1,
                ProjectedTokens = 0,
                Summary = "no parser for the second dialect was found"
            })
            .WithState("parse", StepState.Blocked);

        var coordination = new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["parse"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "parse"
            }
        };

        coordination.HaltReason.ShouldBe("no parser for the second dialect was found");
    }

    // ── Fixtures ──

    private static AgentPlanAdvisor Advisor(string reply) =>
        new(new SupervisionOptions
        {
            Supervisor = SimulatedAgentModel.Fixed(reply),
            Store = new InMemoryPlanTreeStore()
        });

    private static string Options(params object[] options) =>
        JsonSerializer.Serialize(new { options });

    /// <summary>The supervisor's answer: which option it would take, by position, and why.</summary>
    private static string Chose(int option, string why) =>
        JsonSerializer.Serialize(new { option, why });

    /// <summary>The plan stopped at 'parse', and the step left what it found it could do instead.</summary>
    private static PlanCoordination HaltedWithOptions()
    {
        var halted = Halted();

        return halted with
        {
            Result = halted.Result with
            {
                Tree = halted.Result.Tree.WithQuestion("parse", new NodeQuestion
                {
                    Asks = "only the documented dialect parses",
                    Options = ["parse the documented dialect only", "add a second parser"],
                    At = T0
                })
            }
        };
    }

    private static PlanTree Tree() =>
        PlanTree.Create(
            "plan",
            new AgentContract { Goal = "Ship it", AcceptanceCriteria = ["it ships"] },
            [
                ("parse", new AgentContract
                {
                    Goal = "Parse the input",
                    AcceptanceCriteria = ["the parser round-trips"],
                    Constraints = ["no new dependencies"]
                })
            ]);

    /// <summary>A plan stopped at 'parse', with the node's own account of why.</summary>
    private static PlanCoordination Halted()
    {
        var tree = Tree().WithViolation("parse", new PlanViolation
        {
            Criterion = "the parser round-trips",
            Reason = "the input has two dialects and the contract names one",
            At = T0
        });

        return new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["parse"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "parse"
            }
        };
    }
}
