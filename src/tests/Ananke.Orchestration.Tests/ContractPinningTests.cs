using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Contract pinning: a constraint stated once stays in force after compaction has evicted the turn
/// that stated it.
/// </summary>
/// <remarks>
/// <para>
/// The defect this closes is that <b>nothing could be pinned across compaction at all</b>. Both
/// shipped strategies preserve content <em>positionally</em> — a sliding window keeps the last
/// messages that fit, a summarizing one keeps a trailing count and hands the rest to a model with a
/// request, not a guarantee. A goal stated at turn one is therefore gone by turn fifty, which for a
/// long-running session is a correctness bug rather than a tuning question.
/// </para>
/// <para>
/// The mechanism is <em>re-rendering</em>, not marking: nothing is flagged as pinned, so no context
/// strategy — including one written outside this repo — carries an obligation it could silently
/// forget. Each test below is written so that its control is visible next to it, because "the text
/// is still there" is easy to assert by accident.
/// </para>
/// </remarks>
[TestFixture]
public class ContractPinningTests
{
    private const string Constraint = "All file paths must stay relative to the repository root.";

    // ── Rendering ──

    [Test]
    public void Render_AGoalOnly_IsTheWholeContract()
    {
        var rendered = new AgentContract { Goal = "Ship the parser" }.Render();

        rendered.ShouldContain("Ship the parser");
        rendered.ShouldNotContain("Acceptance criteria");
        rendered.ShouldNotContain("Standing constraints");
    }

    [Test]
    public void Render_Criteria_AreNumberedSoTheyCanBeCheckedSeparately()
    {
        var rendered = new AgentContract
        {
            Goal = "Ship the parser",
            AcceptanceCriteria = ["The build passes", "No public API changes"]
        }.Render();

        rendered.ShouldContain("1. The build passes");
        rendered.ShouldContain("2. No public API changes");
    }

    [Test]
    public void Render_Constraints_AreDescribedAsStandingRatherThanAsAFreshInstruction()
    {
        // A constraint stated once at turn one and re-rendered at turn fifty reads as a repeated
        // demand unless the wording says otherwise.
        var rendered = new AgentContract { Goal = "g", Constraints = [Constraint] }.Render();

        rendered.ShouldContain("- " + Constraint);
        rendered.ShouldContain("not restated");
    }

    [Test]
    public void Compose_KeepsThePersonaFirstAndTheContractSecond()
    {
        var composed = AgentContract.Compose("You are terse.", new AgentContract { Goal = "Ship it" });

        composed.ShouldNotBeNull();
        composed.IndexOf("You are terse.", StringComparison.Ordinal)
            .ShouldBeLessThan(composed.IndexOf("Ship it", StringComparison.Ordinal));
    }

    [Test]
    public void Compose_WithNoContract_LeavesTheSystemPromptExactlyAsItWas()
    {
        AgentContract.Compose("You are terse.", null).ShouldBe("You are terse.");
        AgentContract.Compose(null, null).ShouldBeNull();
    }

    [Test]
    public void WithContract_AGoallessContract_IsRejected()
    {
        Should.Throw<ArgumentException>(() =>
            AgentJobFactory.Create<PinState>("bad", new EchoModel())
                .WithPrompt(s => s.Input)
                .WithContract(new AgentContract { Goal = "   " })
                .MapResult((s, text) => s with { Output = text })
                .Build());
    }

    // ── The pin itself ──

    [Test]
    public async Task Contract_IsCarriedByEveryRequestIncludingTheToolRounds()
    {
        var model = new RecordingModel(rounds: 3);
        var job = ToolJob("every-round", model, Contract(), strategy: null, memory: null);

        await job.ExecuteAsync(new PinState { Input = "go" });

        model.Requests.Count.ShouldBeGreaterThan(3);
        model.Requests.ShouldAllBe(r => r.SystemPrompt!.Contains(Constraint));
    }

    /// <summary>
    /// The whole claim, with its control. A sliding window narrow enough to evict turn one is
    /// applied to both runs; only the contracted one still carries the constraint afterwards.
    /// </summary>
    [Test]
    public async Task Contract_SurvivesCompaction_WhereTheTurnOneMessageStatingItDoesNot()
    {
        var withContract = new RecordingModel(rounds: 3);
        var withoutContract = new RecordingModel(rounds: 3);

        await ToolJob("pinned", withContract, Contract(), NarrowWindow(), memory: null)
            .ExecuteAsync(new PinState { Input = "go" });

        await ToolJob("unpinned", withoutContract, contract: null, NarrowWindow(), memory: null)
            .ExecuteAsync(new PinState { Input = StatedOnceInTurnOne });

        var pinnedFinal = withContract.Requests[^1];
        var unpinnedFinal = withoutContract.Requests[^1];

        // Compaction did its job in both runs: the opening turn is gone from each.
        pinnedFinal.Messages.Any(m => (m.Content ?? string.Empty).Contains("go")).ShouldBeFalse();
        unpinnedFinal.Messages.Any(m => (m.Content ?? string.Empty).Contains(Constraint)).ShouldBeFalse();

        // The control loses the constraint entirely; the contract still carries it.
        (unpinnedFinal.SystemPrompt ?? string.Empty).ShouldNotContain(Constraint);
        pinnedFinal.SystemPrompt!.ShouldContain(Constraint);
    }

    [Test]
    public async Task Contract_SurvivesTheStructuredCoercionRound()
    {
        // The structured path makes one more call after the tool loop, on a different code path.
        var model = new StructuredRecordingModel();
        var job = AgentJobFactory.Create<PinState, Answer>("structured", model)
            .WithPrompt(s => s.Input)
            .WithTools(EchoKit())
            .WithContract(Contract())
            .MapResult((s, a) => s with { Output = a.Text })
            .Build();

        await job.ExecuteAsync(new PinState { Input = "go" });

        model.Requests[^1].ResponseFormat.ShouldNotBeNull();   // this really is the coercion round
        model.Requests.ShouldAllBe(r => r.SystemPrompt!.Contains(Constraint));
    }

    [Test]
    public async Task Contract_ReachesTheStrategy_SoItIsPaidForInTheBudgetArithmetic()
    {
        // A strategy that could not see the contract would compact to a budget it then overspends.
        var strategy = new RecordingStrategy();
        var job = ToolJob("accounted", new RecordingModel(rounds: 2), Contract(), strategy, memory: null);

        await job.ExecuteAsync(new PinState { Input = "go" });

        strategy.SystemPrompts.ShouldNotBeEmpty();
        strategy.SystemPrompts.ShouldAllBe(p => p!.Contains(Constraint));
    }

    [Test]
    public async Task Contract_IsNeverWrittenToConversationMemory()
    {
        // It is not a message, and a contract that leaked into history would be reloaded next turn
        // *and* re-rendered — growing once per turn.
        var memory = new InMemoryConversationMemory();
        var job = ToolJob("remembered", new RecordingModel(rounds: 1), Contract(), strategy: null, memory);

        await job.ExecuteAsync(new PinState { Input = "go" });

        var history = await memory.GetHistoryAsync("s1");
        history.ShouldNotBeEmpty();
        history.ShouldAllBe(m => m.Content == null || !m.Content.Contains(Constraint));
    }

    [Test]
    public async Task Contract_CostIsReportedSoAPinnedFloorCanBeSeen()
    {
        var observer = new RecordingObserver();
        using var scope = ContextObserving.BeginScope(observer);

        var contract = Contract();
        var job = ToolJob("measured", new RecordingModel(rounds: 2), contract, NarrowWindow(), memory: null);

        await job.ExecuteAsync(new PinState { Input = "go" });

        observer.Compacted.ShouldNotBeEmpty();
        var record = observer.Compacted[0];
        record.ContractTokens.ShouldBe(
            ApproximateTokenCounter.Instance.EstimateTokens(contract.Render()));
        record.ContractTokens.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task WithoutAContract_NothingIsPinnedAndNothingIsCharged()
    {
        var observer = new RecordingObserver();
        using var scope = ContextObserving.BeginScope(observer);

        var job = ToolJob("plain", new RecordingModel(rounds: 2), contract: null, NarrowWindow(), memory: null);

        await job.ExecuteAsync(new PinState { Input = "go" });

        observer.Compacted.ShouldAllBe(r => r.ContractTokens == 0);
    }

    // ── Fixtures ──

    private static readonly string StatedOnceInTurnOne =
        Constraint + " " + new string('.', 200);

    private static AgentContract Contract() => new()
    {
        Goal = "Refactor the loader",
        AcceptanceCriteria = ["The build passes", "No public API changes"],
        Constraints = [Constraint]
    };

    /// <summary>Narrow enough that the opening turn is evicted within a few tool rounds.</summary>
    private static SlidingWindowContextStrategy NarrowWindow() => new(maxTokens: 60);

    private static ToolKit EchoKit() =>
        new ToolKit("k").AddTool("echo", "Returns a payload", () => ToolResult.Ok(new string('p', 400)));

    private static TextAgentJob<PinState> ToolJob(
        string name,
        IAgentModel model,
        AgentContract? contract,
        IContextStrategy? strategy,
        IConversationMemory? memory)
    {
        var builder = AgentJobFactory.Create<PinState>(name, model)
            .WithPrompt(s => s.Input)
            .WithTools(EchoKit());

        if (contract is not null)
            builder = builder.WithContract(contract);
        if (strategy is not null)
            builder = builder.WithContextStrategy(strategy);
        if (memory is not null)
            builder = builder.WithMemory(memory, _ => "s1");

        return builder.MapResult((s, text) => s with { Output = text }).Build();
    }

    private sealed record PinState
    {
        public string Input { get; init; } = string.Empty;
        public string Output { get; init; } = string.Empty;
    }

    private sealed record Answer(string Text);

    /// <summary>Calls the tool <c>rounds</c> times, then answers, recording every request.</summary>
    private sealed class RecordingModel(int rounds) : IAgentModel
    {
        private int _calls;

        public List<AgentRequest> Requests { get; } = [];

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(_calls++ < rounds
                ? new AgentResponse { Text = "working", ToolCalls = [new AgentToolCall($"c{_calls}", "echo", "{}")] }
                : new AgentResponse { Text = "done" });
        }
    }

    /// <summary>One tool round, then a JSON answer for the coercion call.</summary>
    private sealed class StructuredRecordingModel : IAgentModel
    {
        private int _calls;

        public List<AgentRequest> Requests { get; } = [];

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(_calls++ == 0
                ? new AgentResponse { Text = "working", ToolCalls = [new AgentToolCall("c1", "echo", "{}")] }
                : new AgentResponse { Text = """{"Text":"done"}""" });
        }
    }

    private sealed class EchoModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "done" });
    }

    /// <summary>Passes messages through, recording the system prompt it was offered each time.</summary>
    private sealed class RecordingStrategy : IContextStrategy
    {
        public List<string?> SystemPrompts { get; } = [];

        public Task<ContextProjection> ApplyAsync(
            IReadOnlyList<AgentMessage> messages,
            string? systemPrompt,
            ContextBudget budget,
            CancellationToken ct = default)
        {
            SystemPrompts.Add(systemPrompt);
            return Task.FromResult(ContextProjection.Unchanged(messages, budget.Resolve(int.MaxValue)));
        }
    }

    private sealed class RecordingObserver : IContextObserver
    {
        public List<ContextCompactionRecord> Compacted { get; } = [];

        public ValueTask OnContextAssembledAsync(
            ContextObservation observation, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask OnContextCompactedAsync(
            ContextCompactionRecord record, CancellationToken ct = default)
        {
            Compacted.Add(record);
            return ValueTask.CompletedTask;
        }
    }
}
