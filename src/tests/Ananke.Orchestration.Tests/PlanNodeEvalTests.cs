using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The one judgement in the tier that still needs a model: <c>Ask</c> or <c>Replan</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This harness used to measure the opposite end of the loop, and inverting it is E10's job.</b>
/// It asked whether a <em>node</em> could classify its own outcome — done, blocked, disputed — and
/// measured 24 runs a cell on the cheap model, 8 September 2026:
/// </para>
/// <list type="table">
///   <listheader><term>The world says</term><description>plain / steered</description></listheader>
///   <item><term>done</term><description>24/24 · 24/24</description></item>
///   <item><term>already answered</term><description>24/24 · 24/24</description></item>
///   <item><term>cannot be done</term><description>23/24 · 24/24</description></item>
///   <item><term>two ways to take it</term><description><b>14/24 · 4/24</b></description></item>
///   <item><term>needs somebody outside</term><description><b>0/24 · 2/24</b></description></item>
/// </list>
/// <para>
/// Those numbers are why the design changed, and they are <b>history rather than a current reading</b>:
/// nothing asks a node to classify anything any more. What is measured here now is the decision that
/// replaced it — the supervisor reading findings and returning one of two verbs — because that is the
/// only place left where a model's judgement steers the plan.
/// </para>
/// <para>
/// <b>Two things were deleted with the inversion, and both had reasons worth keeping.</b>
/// </para>
/// <para>
/// <b>The <c>Said</c>/<c>Held</c> diagnostic is gone.</b> It read every run twice — once off what the
/// model claimed, once off what the tree was willing to hold — because when those disagreed the
/// failure was the <em>seam's</em> (the model reported correctly and something discarded it) and when
/// they agreed it was the model's, and a single reading could not tell two opposite fixes apart. It is
/// gone because the gap it measured is gone: a report carries no classification, so there is nothing
/// between the model and the tree that can quietly drop one. What a node says is narration, and it
/// steers nothing whether it survives or not.
/// </para>
/// <para>
/// <b>The <c>Plain</c>/<c>Steered</c> voices are gone too</b>, and their finding stands: the steered
/// wording was worse wherever the two differed, by a factor of three on the case the tier exists for.
/// The variable no longer exists on this path — under R27 the world's answer reaches the <em>loop</em>
/// as a value and the executor never reads it, so there is no prose to tune. The same experiment is
/// still available one seat up, where a supervisor does read findings; nobody has run it there.
/// </para>
/// <para>
/// Live and explicit — it spends model calls. Run with
/// <c>dotnet test --filter TestCategory=Live</c>, with <c>OPENAI_API_KEY</c> in the environment or in
/// the repo's <c>.env</c>.
/// </para>
/// </remarks>
[TestFixture]
[Category("Live")]
[Explicit("Calls a real model; needs OPENAI_API_KEY.")]
public class PlanNodeEvalTests
{
    /// <summary>How many times each case runs. All of them must agree.</summary>
    private static int Runs =>
        int.TryParse(Environment.GetEnvironmentVariable("ANANKE_EVAL_RUNS"), out var n) && n > 0
            ? n
            : 3;

    // ── The world says the step simply cannot be done ──

    [Test]
    public async Task WhenNowhereOfThatNameExists_TheSupervisorReplansWithoutAskingAnybody()
    {
        // S8, and the shortest path to the planner there is. Nothing a person could decide would make
        // atlantis reachable, so a run that asks about it has spent a pause on a question with no
        // answer — and left somebody choosing between losses that were never in question.
        var seen = await Observe(
            "atlantis",
            _ => Carrying.Cannot("there is nowhere called 'atlantis' that this trip can reach"));

        seen.ShouldAllDecide<PlanDecision.ReplanPlan>();
    }

    // ── The world offers a choice the step may not make ──

    [Test]
    public async Task WhenTheWorldOffersTwoWays_TheSupervisorAsks()
    {
        // S6. Nothing is wrong with the plan and nothing is blocked in the world — somebody simply has
        // to pick, and picking is not this loop's to do. A Replan here re-words a contract that was
        // never the problem.
        var seen = await Observe(
            "kyoto",
            _ => Carrying.Choice("kyoto can be taken two ways: by bullet train, or by overnight bus"));

        seen.ShouldAllDecide<PlanDecision.AskPlan>();
    }

    // ── The world says somebody outside the run has to move ──

    [Test]
    public async Task WhenOnlySomebodyOutsideCanUnblockIt_TheSupervisorAsks()
    {
        // The row that scored 0/24 when a node had to classify it, and the reason R30 folded Refer
        // into Ask: *somebody must act* and *somebody must choose* are one verb, and the question text
        // carries the difference. Nothing here has to predict which it will turn out to be.
        var seen = await Observe(
            "naoshima",
            _ => Carrying.Needs(
                "the ferry runs only in summer, and only the trip's owner can move the dates"));

        seen.ShouldAllDecide<PlanDecision.AskPlan>();
    }

    // ── And the case where no judgement is wanted at all ──

    [Test]
    public async Task WhenTheWorldTakesIt_TheSupervisorIsNeverConsulted()
    {
        // The happy path costs one executor call and a check. Nothing judges what a check already
        // decided, so a supervisor consulted here is a round nobody needed to pay for.
        var seen = await Observe("hakone", _ => Carrying.Done);

        seen.Halts.ShouldAllBe(h => h == false, "a settled step reached the supervisor");
        seen.Decisions.ShouldBeEmpty();
    }

    // ── Fixtures ──

    /// <summary>What the fake world says when the loop applies a candidate.</summary>
    private sealed record Carrying(string Outcome, string? Because = null)
    {
        public static Carrying Done { get; } = new("done");

        public static Carrying Choice(string why) => new("choice", why);

        public static Carrying Cannot(string why) => new("cannot", why);

        public static Carrying Needs(string why) => new("needs", why);
    }

    /// <summary>What <see cref="Runs"/> runs of one halt produced.</summary>
    private sealed record Seen
    {
        public required IReadOnlyList<PlanDecision?> Runs { get; init; }

        public IEnumerable<bool> Halts => Runs.Select(d => d is not null);

        public IEnumerable<PlanDecision> Decisions => Runs.Where(d => d is not null)!;

        /// <summary>Asserts every run reached the supervisor and got the same verb.</summary>
        public void ShouldAllDecide<T>() where T : PlanDecision
        {
            Runs.ToList().ShouldAllBe(
                d => d is T,
                $"expected all {Runs.Count} runs to decide {typeof(T).Name}; got "
                    + string.Join("; ", Runs.Select((d, i) =>
                        $"run {i + 1}: {d?.GetType().Name ?? "no halt at all"}")));
        }
    }

    private static async Task<Seen> Observe(string place, Func<string?, Carrying> world)
    {
        var runs = new List<PlanDecision?>();

        for (var run = 0; run < Runs; run++)
            runs.Add(await Once(place, world).ConfigureAwait(false));

        return new Seen { Runs = runs };
    }

    /// <summary>
    /// One node proposing a candidate, the loop applying it, and the supervisor reading what the
    /// checks found.
    /// </summary>
    /// <remarks>
    /// Deliberately the whole path rather than a hand-built coordination: what is being measured is a
    /// decision made from <em>findings the run actually produced</em>, and a halt assembled by hand
    /// would be measuring the prompt against evidence the tier never generated.
    /// </remarks>
    private static async Task<PlanDecision?> Once(string place, Func<string?, Carrying> world)
    {
        var carried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var check = new InvocationCheck("world",
        [
            CheckDefinition.Of(
                "carried", 1, "that place is on the trip",
                c => carried.Contains(c.Argument(0)!)
                    ? Finding.Held
                    : Finding.Not(world(null).Because))
        ]);

        var store = new InMemoryPlanTreeStore();

        var contract = new AgentContract
        {
            Goal = $"Put {place} on the trip.",
            AcceptanceCriteria = [$"carried({place})"]
        };

        await store.SaveAsync(PlanTree.Create(
            "trip",
            new AgentContract { Goal = "Plan the trip", AcceptanceCriteria = [$"carried({place})"] },
            [(place, contract)])).ConfigureAwait(false);

        var model = Model();

        var supervision = new SupervisionOptions
        {
            Store = store,
            Supervisor = model,
            Checks = [check],
            Verifier = new DeterministicVerifier([check]),

            // Read-only, as R27 requires: what the world says arrives at the loop as a value, and the
            // executor never sees it. It proposes; the applier below is what tries.
            Runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions
            {
                Model = model,
                Configure = builder => builder
                    .WithTools(new ToolKit("trip").AddTool(
                        "places",
                        "Everywhere this trip could go.",
                        () => ToolResult.Ok($"[\"{place}\"]")))
                    .WithMaxToolRounds(4)
            }).AsRunner(),

            Applier = (candidate, _, _) =>
            {
                var named = candidate.Arguments.Any(
                    a => string.Equals(a, place, StringComparison.OrdinalIgnoreCase));

                if (named && world(null).Outcome == "done")
                    carried.Add(place);

                // Form only: anything naming somewhere is applied, and what the world says about it
                // is the acceptance gate's to discover (S8).
                return Task.FromResult(named
                    ? PlanApplication.Ok()
                    : PlanApplication.Rejected(
                        $"\"{candidate}\" does not read as an operation this step can perform."));
            }
        };

        var result = await new PlanExecutor(
                store, verifier: supervision.ResolvedVerifier, applier: supervision.Applier)
            .ExecuteAsync("trip", supervision.ResolvedRunner)
            .ConfigureAwait(false);

        if (result.HaltedAt is null)
            return null;

        return await new AgentPlanSupervisor(supervision)
            .DecideAsync(new PlanCoordination { Result = result })
            .ConfigureAwait(false);
    }

    private static IAgentModel Model() => OpenAIChatAgentModel.Create(
        Keys.Require("OPENAI_API_KEY"),
        Environment.GetEnvironmentVariable("ANANKE_TEST_MODEL") ?? Models.OpenAI.Gpt56Luna);
}
