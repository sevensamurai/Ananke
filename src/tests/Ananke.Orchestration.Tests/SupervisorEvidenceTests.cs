using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What reaches the supervisor's prompt at a halt: evidence, never a paraphrase of it.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is downstream of one finding, measured against a live model: an outcome's
/// reliability tracks how much outside judgement it gets, and the two outcomes that were left to a
/// node's own say-so were the two that failed. This is the seam that carries the fix — a failing
/// check's own words reach the supervisor, a node's narration of its own work does not, and the view
/// of the plan it reasons from is never the trimmed one the node itself was shown.
/// </para>
/// <para>
/// <b>The projection side of "truncation must stay visible" already has its own coverage</b> — see
/// <c>PlanExecutionTests</c>'s <c>Project_...</c> cases. What is new here, and tested here, is the
/// evidence section: a supervisor asked about many failing criteria must still see at least one in
/// full, and must be told when the rest did not fit rather than reading a list that looks complete.
/// </para>
/// </remarks>
[TestFixture]
public class SupervisorEvidenceTests
{
    private const string Criterion = "the parser round-trips";

    [Test]
    public async Task DecideAsync_AFailingCriterion_ShowsItsBasisVerbatim()
    {
        const string basis = "CS0103: the name 'jsonOpt' does not exist";

        var prompt = await Prompt(Halt(Verdict(Criterion, passed: false, basis: basis)));

        prompt.ShouldContain(basis);
    }

    [Test]
    public async Task DecideAsync_APassingCriterion_ShowsNoBasisEvenIfOneWasRecorded()
    {
        // A verdict that held needs nothing explained — the budget exists for the failures a
        // decision is actually being asked about, not for a judge's reasoning about a success.
        const string basis = "every round trip byte-matched the input";

        var prompt = await Prompt(Halt(Verdict(Criterion, passed: true, basis: basis)));

        prompt.ShouldNotContain(basis);
    }

    [Test]
    public async Task DecideAsync_ManyFailingVerdicts_ShowsAtLeastOneInFullAndSaysWhatItDropped()
    {
        // Each basis alone is under the per-verdict cap a ProcessCheck would produce, but enough of
        // them together exceed the evidence section's own budget — the case the budget exists for.
        var big = new string('x', 3_000);
        var verdicts = Enumerable.Range(0, 6)
            .Select(i => Verdict($"criterion {i}", passed: false, basis: $"{i}:{big}"))
            .ToArray();

        var prompt = await Prompt(Halt(verdicts));

        prompt.ShouldContain("0:" + big);                 // the first survives in full
        prompt.ShouldContain("further verdict(s) not shown");
        prompt.ShouldNotContain("5:" + big);               // and the budget really was spent
    }

    [Test]
    public async Task DecideAsync_TheProjection_IsNeverTrimmedToTheNodesOwnBudget()
    {
        // Built deep enough that the node's own configured budget would need to trim it — proven
        // directly against PlanTreeProjection, the same mechanism DescribeHalt now calls unbounded.
        var tree = DeepTree();
        var full = PlanTreeProjection.Project(tree, "leaf");
        var underNodesBudget = PlanTreeProjection.Project(tree, "leaf", full.EstimatedTokens / 4);

        underNodesBudget.OmittedVerdicts.ShouldBeGreaterThan(0, "the fixture must actually be tight");

        var prompt = await Prompt(new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["leaf"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "leaf"
            },
            MaxChanges = 3
        });

        // What a tight node budget would have dropped is nonetheless present in the supervisor's
        // prompt — the whole record, the same rule the verifier already reads by.
        for (var i = 0; i < 12; i++)
            prompt.ShouldContain($"an earlier finding number {i}");
    }

    // ── Fixtures ──

    private static async Task<string> Prompt(PlanCoordination coordination)
    {
        string? prompt = null;
        var model = new SimulatedAgentModel(request =>
        {
            prompt = string.Join('\n', request.Messages.Select(m => m.Content));
            return """{"goal":"fix it","criteria":["it round-trips"],"rationale":"because"}""";
        });

        await new AgentPlanSupervisor(new SupervisionOptions { Supervisor = model })
            .DecideAsync(coordination).ConfigureAwait(false);

        return prompt.ShouldNotBeNull();
    }

    private static CriterionVerdict Verdict(string criterion, bool passed, string? basis = null) =>
        new()
        {
            Criterion = criterion,
            Passed = passed,
            Oracle = "dotnet test",
            Basis = basis,
            At = DateTimeOffset.UnixEpoch
        };

    private static PlanCoordination Halt(params CriterionVerdict[] verdicts)
    {
        var tree = PlanTree.Create(
            "plan",
            new AgentContract { Goal = "Ship it.", AcceptanceCriteria = ["it ships"] },
            [
                ("work", new AgentContract
                {
                    Goal = "Do the work.",
                    AcceptanceCriteria = [.. verdicts.Select(v => v.Criterion)]
                })
            ]);

        foreach (var verdict in verdicts)
            tree = tree.WithVerdict("work", verdict);

        return new PlanCoordination
        {
            Result = new PlanRunResult
            {
                Tree = tree,
                Executed = ["work"],
                Skipped = [],
                Rulings = new Dictionary<string, Verification>(),
                HaltedAt = "work"
            },
            MaxChanges = 3
        };
    }

    /// <summary>A root, one ancestor, and a leaf carrying twelve established findings elsewhere.</summary>
    private static PlanTree DeepTree()
    {
        var tree = PlanTree.Create(
            "plan",
            new AgentContract { Goal = "Ship it.", AcceptanceCriteria = ["it ships"] },
            [
                ("parse", new AgentContract
                {
                    Goal = "Parse the input.",
                    AcceptanceCriteria = ["the parser round-trips"]
                }),
                ("leaf", new AgentContract
                {
                    Goal = "Render the output.",
                    AcceptanceCriteria = ["output matches the golden file"]
                })
            ]);

        for (var i = 0; i < 12; i++)
        {
            tree = tree.WithVerdict("parse", new CriterionVerdict
            {
                Criterion = $"an earlier finding number {i}",
                Passed = true,
                Oracle = "dotnet test",
                At = DateTimeOffset.UnixEpoch
            });
        }

        return tree;
    }
}
