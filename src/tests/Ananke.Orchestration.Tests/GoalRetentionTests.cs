using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The goal-retention curve — measured with a scripted model, because dropping is a harness fact.
/// </summary>
/// <remarks>
/// <para>
/// Whether a goal stated at turn one is still in the prompt at turn fifty is decided entirely by
/// what compaction kept. No model opinion enters into it, so no model is needed to measure it, and
/// the measurement is exactly repeatable — which is what makes it usable as a before-and-after.
/// </para>
/// <para>
/// <b>What this does not measure:</b> whether a model that was shown the goal still acts on it.
/// That needs a real model, and treating a retention result as a compliance result is the specific
/// mistake these tests are named to avoid.
/// </para>
/// <para>
/// The prediction under test is a <b>shape</b>. A pinned contract should survive every turn while an
/// unpinned statement of the same thing falls off at some point; two arms that both decay gradually
/// would mean the mechanism is not doing what it claims.
/// </para>
/// </remarks>
[TestFixture]
public class GoalRetentionTests
{
    private const string Constraint = "All file paths must stay relative to the repository root.";

    [Test]
    public async Task Unpinned_AGoalStatedAtTurnOne_IsLostAtAKnowableTurn()
    {
        var probe = Probe();
        await Run("unpinned", probe, contract: null, statedInPrompt: true, rounds: 6);

        var curve = probe.Curve();

        curve.FirstLossTurn[Constraint].ShouldNotBeNull();
        curve.Survived.ShouldBeEmpty();

        // Retention is monotone once lost: nothing puts it back.
        var lostAt = curve.FirstLossTurn[Constraint]!.Value;
        curve.Samples.Where(s => s.Turn >= lostAt)
            .ShouldAllBe(s => s.Retained[Constraint] == false);
    }

    [Test]
    public async Task Pinned_TheSameConstraint_SurvivesEveryTurn()
    {
        var probe = Probe();
        await Run("pinned", probe, Contract(), statedInPrompt: false, rounds: 6);

        var curve = probe.Curve();

        curve.FirstLossTurn[Constraint].ShouldBeNull();
        curve.Survived.ShouldBe([Constraint]);
        curve.Samples.ShouldAllBe(s => s.Retained[Constraint]);
    }

    [Test]
    public async Task TheTwoArms_DifferAsAStepRatherThanAsAGradualDecay()
    {
        // The claim is about shape. Both arms run the same job under the same window; the pinned one
        // holds flat at 1.0 while the unpinned one drops to 0 and stays there. Two arms both sagging
        // slowly would mean the improvement is coming from somewhere else.
        var unpinned = Probe();
        var pinned = Probe();

        await Run("arm-a", unpinned, contract: null, statedInPrompt: true, rounds: 6);
        await Run("arm-b", pinned, Contract(), statedInPrompt: false, rounds: 6);

        var rates = unpinned.Curve().Samples.Select(s => s.RetentionRate).Distinct().ToList();
        rates.ShouldContain(1);      // held at first
        rates.ShouldContain(0);      // then gone
        rates.Count.ShouldBe(2);     // and nothing in between — a step, not a slope

        pinned.Curve().Samples.Select(s => s.RetentionRate).Distinct().ShouldBe([1]);
    }

    [Test]
    public async Task TheCurve_RecordsGrowingHistoryAgainstTurnCount()
    {
        var probe = Probe();
        await Run("growth", probe, Contract(), statedInPrompt: false, rounds: 4);

        var samples = probe.Curve().Samples;

        samples.Select(s => s.Turn).ShouldBe(Enumerable.Range(1, samples.Count));
        samples[^1].InputMessages.ShouldBeGreaterThan(samples[0].InputMessages);
    }

    [Test]
    public void TheProbe_PassesTheInnerStrategysProjectionThrough()
    {
        // It measures; it must not change what the run does, or the numbers describe the probe.
        var inner = new SlidingWindowContextStrategy(maxTokens: 50);
        var probe = new ContextRetentionProbe(inner, [Constraint]);
        var messages = Enumerable.Range(0, 8).Select(i => AgentMessage.User(new string('x', 400) + i)).ToList();

        var direct = inner.ApplyAsync(messages, null, ContextBudget.Unspecified).Result;
        var through = probe.ApplyAsync(messages, null, ContextBudget.Unspecified).Result;

        through.Messages.Count.ShouldBe(direct.Messages.Count);
        through.ShadowedCount.ShouldBe(direct.ShadowedCount);
    }

    [Test]
    public void TheReport_NamesWhereEachPhraseWasLost()
    {
        var probe = Probe();
        probe.ApplyAsync([AgentMessage.User(Constraint)], null, ContextBudget.Unspecified).Wait();
        probe.ApplyAsync([AgentMessage.User("something else")], null, ContextBudget.Unspecified).Wait();

        var report = probe.Curve().ToReport();

        report.ShouldContain("turn");
        report.ShouldContain("lost at turn 2");
    }

    // ── Fixtures ──

    private static ContextRetentionProbe Probe() =>
        new(new SlidingWindowContextStrategy(maxTokens: 260), [Constraint]);

    private static AgentContract Contract() => new()
    {
        Goal = "Tidy the loader",
        Constraints = [Constraint]
    };

    private static Task Run(
        string name, IContextStrategy strategy, AgentContract? contract, bool statedInPrompt, int rounds)
    {
        var builder = AgentJobFactory.Create<S>(name, new Chatty(rounds))
            .WithPrompt(s => s.In)
            .WithTools(NoisyKit())
            .WithContextStrategy(strategy);

        if (contract is not null)
            builder = builder.WithContract(contract);

        var prompt = statedInPrompt ? $"Tidy the loader. {Constraint}" : "Tidy the loader.";

        return builder
            .WithMaxToolRounds(rounds + 1)
            .MapResult((s, text) => s with { Out = text })
            .Build()
            .ExecuteAsync(new S { In = prompt });
    }

    /// <summary>A tool whose output is large enough to push earlier turns out of a small window.</summary>
    private static ToolKit NoisyKit() =>
        new ToolKit("noise").AddTool(
            "work", "Does some work", () => ToolResult.Ok(new string('n', 600)));

    private sealed record S
    {
        public string In { get; init; } = string.Empty;
        public string Out { get; init; } = string.Empty;
    }

    /// <summary>Calls the tool <c>rounds</c> times, then answers. No opinions, entirely repeatable.</summary>
    private sealed class Chatty(int rounds) : IAgentModel
    {
        private int _calls;

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(_calls++ < rounds
                ? new AgentResponse
                {
                    Text = "working",
                    ToolCalls = [new AgentToolCall($"c{_calls}", "work", $$"""{"n":{{_calls}}}""")]
                }
                : new AgentResponse { Text = "done" });
    }
}
