using System.Text.Json;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a seat is told about a halt: what somebody said, what earlier runs settled, what the plan
/// already tried, and what was refused.
/// </summary>
/// <remarks>
/// <para>
/// These sections are composed into the advisor's prompt today. Pinning them before they move is what
/// makes putting them where a second seat can reach them a move rather than a rewrite: the same halt
/// has to produce the same sections afterwards.
/// </para>
/// <para>
/// A phrase per section is matched, not the paragraph, so what is pinned is which sections are
/// composed and in what order rather than the prose inside them.
/// </para>
/// <para>
/// The refusals section is reached only on the advisor's second ask, which happens when every option
/// it offered was discarded — so the first answer here is one the guards reject.
/// </para>
/// </remarks>
[TestFixture]
public class PlanHaltRecordTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TheAsk_CarriesEverySectionOfTheRecord()
    {
        var prompts = await PromptsAsync(Halted(), refusedFirst: true);

        prompts.Count.ShouldBe(2);
        var reask = prompts[1];

        reask.ShouldContain("none of the options you offered");     // what somebody said
        reask.ShouldContain("Remembered from earlier runs");        // what earlier runs settled
        reask.ShouldContain("What this plan already tried");        // the lineage
        reask.ShouldContain("already answered this once");          // what was refused
    }

    [Test]
    public async Task TheAsk_CarriesTheSectionsInOrder()
    {
        var prompts = await PromptsAsync(Halted(), refusedFirst: true);
        var reask = prompts[1];

        var said = reask.IndexOf("none of the options you offered", StringComparison.Ordinal);
        var remembered = reask.IndexOf("Remembered from earlier runs", StringComparison.Ordinal);
        var tried = reask.IndexOf("What this plan already tried", StringComparison.Ordinal);
        var refused = reask.IndexOf("already answered this once", StringComparison.Ordinal);

        said.ShouldBeLessThan(remembered);
        remembered.ShouldBeLessThan(tried);
        tried.ShouldBeLessThan(refused);
    }

    [Test]
    public async Task TheAsk_CarriesWhatWasSaidVerbatim()
    {
        var prompts = await PromptsAsync(Halted());

        prompts[0].ShouldContain("we would rather not add a dependency");
    }

    [Test]
    public async Task TheAsk_CarriesARememberedTermAsRememberedAndNotAsSaidHere()
    {
        var prompts = await PromptsAsync(Halted());

        prompts[0].ShouldContain("the team works in one repository");
        prompts[0].ShouldContain("These bind nothing");
    }

    [Test]
    public async Task TheAsk_IsNotAskedTwiceWhenTheFirstAnswerHolds()
    {
        var prompts = await PromptsAsync(Halted());

        prompts.Count.ShouldBe(1);
        prompts[0].ShouldNotContain("already answered this once");
    }

    [Test]
    public async Task TheAsk_OmitsEverySectionNothingFilled()
    {
        var prompts = await PromptsAsync(Bare());

        prompts[0].ShouldNotContain("none of the options you offered");
        prompts[0].ShouldNotContain("Remembered from earlier runs");
        prompts[0].ShouldNotContain("already answered this once");
    }

    [Test]
    public async Task TheAsk_CarriesTheHaltReasonAndTheStepsContract()
    {
        var prompts = await PromptsAsync(Bare());

        prompts[0].ShouldContain("the input has two dialects");
        prompts[0].ShouldContain("the parser round-trips");
    }

    // ── Fixtures ──

    /// <summary>
    /// Every prompt the advisor built for <paramref name="halted"/>, in order.
    /// </summary>
    /// <param name="halted">The halt to ask about.</param>
    /// <param name="refusedFirst">
    /// Answer the first ask with an option the guards reject, so the advisor asks again with what was
    /// refused.
    /// </param>
    private static async Task<IReadOnlyList<string>> PromptsAsync(
        PlanCoordination halted, bool refusedFirst = false)
    {
        var prompts = new List<string>();
        var answers = new Queue<string>(refusedFirst ? [Unusable, Usable] : [Usable]);

        await new AgentPlanAdvisor(new SupervisionOptions
        {
            Supervisor = new SimulatedAgentModel(request =>
            {
                prompts.Add(string.Join('\n', request.Messages.Select(m => m.Content)));
                return answers.Count > 1 ? answers.Dequeue() : answers.Peek();
            }),
            Store = new InMemoryPlanTreeStore()
        }).ProposeAsync(halted).ConfigureAwait(false);

        return prompts;
    }

    /// <summary>An option that neither changes the plan nor gives the step up, which is discarded.</summary>
    private static readonly string Unusable = JsonSerializer.Serialize(new
    {
        options = new[] { new { summary = "Think about it", recommended = true } }
    });

    private static readonly string Usable = JsonSerializer.Serialize(new
    {
        options = new[]
        {
            new { summary = "Split it in two", replan = true, rationale = "handles both dialects", recommended = true }
        }
    });

    /// <summary>A plan stopped at 'parse', with nothing said, remembered or refused.</summary>
    private static PlanCoordination Bare() => new()
    {
        Result = new PlanRunResult
        {
            Tree = Tree().WithViolation("parse", new PlanViolation
            {
                Criterion = "the parser round-trips",
                Reason = "the input has two dialects and the contract names one",
                At = T0
            }),
            Executed = ["parse"],
            Skipped = [],
            Rulings = new Dictionary<string, Verification>(),
            HaltedAt = "parse"
        }
    };

    /// <summary>The same halt, after somebody answered, with a term recalled and the plan re-ruled once.</summary>
    private static PlanCoordination Halted()
    {
        var bare = Bare();

        var tried = bare.Result.Tree.Rerule(
            "plan",
            new AgentContract { Goal = "Ship it", AcceptanceCriteria = ["it ships"] },
            "the input has two dialects",
            [("parse-strict", new AgentContract
            {
                Goal = "Parse the documented dialect",
                AcceptanceCriteria = ["the strict parser round-trips"]
            })]);

        return bare with
        {
            Result = bare.Result with { Tree = tried, HaltedAt = "parse-strict" },
            Said = "we would rather not add a dependency",
            Recalled =
            [
                new PlanTerm
                {
                    Id = "t1",
                    Reading = "the team works in one repository",
                    Said = "everything lives in the monorepo",
                    By = "the team",
                    At = T0
                }
            ]
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
}
