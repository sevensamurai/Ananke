using Ananke.Learning.EmpiricalMemory;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Learning.Tests;

/// <summary>
/// Corpus allocation and depth: recall that fits a supplied token budget by going shallower before
/// it goes narrower, and that says which half of the tier each entry belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Recall was count-budgeted — <c>TopK</c> whole entries at a fixed per-entry price — so it could
/// not express "fit in 4k" and could not trade coverage against detail at all. It also had no way
/// to say which recalled content may be contradicted by what the agent observes now, which is the
/// safety rule the tier ordering exists to enforce rather than hope for.
/// </para>
/// <para>
/// <b>The two authority tests below are deliberately opposite.</b> Poisoning an <em>advisory</em>
/// entry tests that observed fact wins. Poisoning a <em>verified record</em> tests the opposite
/// rule, and scoring it with the advisory instrument would penalise exactly the behaviour required.
/// </para>
/// </remarks>
[TestFixture]
public class CorpusAllocationTests
{
    private InMemoryEmpiricalMemory _memory = null!;

    [SetUp]
    public void SetUp() =>
        // Dedup off: several of these tests seed a set of deliberately similar entries to fill a
        // budget with, and merging them would silently defeat the thing under test.
        _memory = new InMemoryEmpiricalMemory(new InMemoryEmbedder(), dedupThreshold: 1.0f);

    // ── Depth is three prices for the same entry ──

    [Test]
    public void Render_DeeperDepths_CostStrictlyMore()
    {
        var match = Match(Rich("p1"));

        var abstractOnly = EmpiricalRecallRenderer.EstimateTokens(match, RecallDepth.Abstract);
        var entry = EmpiricalRecallRenderer.EstimateTokens(match, RecallDepth.Entry);
        var full = EmpiricalRecallRenderer.EstimateTokens(match, RecallDepth.Full);

        abstractOnly.ShouldBeLessThan(entry);
        entry.ShouldBeLessThan(full);
    }

    [Test]
    public void Render_Abstract_CarriesIdentityAndSummaryAndNothingElse()
    {
        var rendered = EmpiricalRecallRenderer.Render(Match(Rich("p1")), RecallDepth.Abstract);

        rendered.ShouldContain("p1");
        rendered.ShouldContain("a summary");
        rendered.ShouldNotContain("Mechanism");
        rendered.ShouldNotContain("Condition");
    }

    [Test]
    public void Render_Entry_CarriesWhatDecidesApplicabilityButNotTheProcedure()
    {
        var rendered = EmpiricalRecallRenderer.Render(Match(Rich("p1")), RecallDepth.Entry);

        rendered.ShouldContain("Condition: gc pause");
        rendered.ShouldContain("Effect: timeouts");
        rendered.ShouldNotContain("Mechanism");   // the expensive half
        rendered.ShouldNotContain("Evidence");
    }

    [Test]
    public void Render_Full_CarriesEverything()
    {
        var rendered = EmpiricalRecallRenderer.Render(Match(Rich("p1")), RecallDepth.Full);

        rendered.ShouldContain("Mechanism: heap grows");
        rendered.ShouldContain("Evidence: log-1");
        rendered.ShouldContain("Source: test");
    }

    // ── Spending the allocation ──

    [Test]
    public void Apply_NoBudget_ChangesNothing()
    {
        var ranked = Ranked(4);

        var allocation = RecallBudget.Apply(ranked, new RecallOptions());

        allocation.Matches.ShouldBe(ranked);
        allocation.Depth.ShouldBeNull();
        allocation.Omitted.ShouldBe(0);
    }

    [Test]
    public void Apply_AGenerousBudget_KeepsEveryEntryAtFullDepth()
    {
        var ranked = Ranked(4);

        var allocation = RecallBudget.Apply(ranked, new RecallOptions { TokenBudget = 100_000 });

        allocation.Depth.ShouldBe(RecallDepth.Full);
        allocation.Matches.Count.ShouldBe(4);
        allocation.Matches.ShouldAllBe(m => m.Depth == RecallDepth.Full);
    }

    [Test]
    public void Apply_ATightBudget_GoesShallowerBeforeItGoesNarrower()
    {
        // The load-bearing preference: an unannounced partial list reads as a complete one, so
        // dropping entries is the last resort rather than the first.
        var ranked = Ranked(4);
        var budget = RecallBudget.Apply(ranked, new RecallOptions { TokenBudget = 100_000 }).EstimatedTokens / 3;

        var allocation = RecallBudget.Apply(ranked, new RecallOptions { TokenBudget = budget });

        allocation.Matches.Count.ShouldBe(4);              // coverage kept
        allocation.Depth.ShouldNotBe(RecallDepth.Full);    // detail given up instead
        allocation.Omitted.ShouldBe(0);
        allocation.EstimatedTokens.ShouldBeLessThanOrEqualTo(budget);
    }

    [Test]
    public void Apply_ABudgetTooSmallEvenForAbstracts_DropsAndReportsWhatItDropped()
    {
        var ranked = Ranked(4);
        var oneAbstract = EmpiricalRecallRenderer.EstimateTokens(ranked[0], RecallDepth.Abstract);

        var allocation = RecallBudget.Apply(
            ranked, new RecallOptions { TokenBudget = (oneAbstract * 2) + 1 });

        allocation.Depth.ShouldBe(RecallDepth.Abstract);
        allocation.Matches.Count.ShouldBe(2);
        allocation.Considered.ShouldBe(4);
        allocation.Omitted.ShouldBe(2);
        allocation.EstimatedTokens.ShouldBeLessThanOrEqualTo((oneAbstract * 2) + 1);
    }

    [Test]
    public void Apply_AnExplicitDepth_InvertsTheTradeAndDropsEntriesInstead()
    {
        var ranked = Ranked(4);
        var oneFull = EmpiricalRecallRenderer.EstimateTokens(ranked[0], RecallDepth.Full);

        var allocation = RecallBudget.Apply(ranked, new RecallOptions
        {
            TokenBudget = (oneFull * 2) + 1,
            Depth = RecallDepth.Full
        });

        allocation.Depth.ShouldBe(RecallDepth.Full);
        allocation.Matches.Count.ShouldBe(2);
        allocation.Omitted.ShouldBe(2);
    }

    [Test]
    public void Apply_DropsFromTheTail_SoTheBestMatchesSurvive()
    {
        var ranked = Ranked(4);
        var oneAbstract = EmpiricalRecallRenderer.EstimateTokens(ranked[0], RecallDepth.Abstract);

        var allocation = RecallBudget.Apply(ranked, new RecallOptions { TokenBudget = oneAbstract });

        allocation.Matches.Count.ShouldBe(1);
        allocation.Matches[0].Entry.Id.ShouldBe(ranked[0].Entry.Id);
    }

    // ── Through the store ──

    [Test]
    public async Task Recall_WithABudget_ReturnsEntriesTaggedWithTheDepthThatFits()
    {
        for (var i = 0; i < 4; i++)
            await _memory.CommitAsync(Rich($"p{i}", $"gc pause causes timeouts in service {i}"));

        var generous = await _memory.RecallAsync("gc pause", new RecallOptions { TopK = 4 });
        var full = generous.Sum(m => EmpiricalRecallRenderer.EstimateTokens(m, RecallDepth.Full));

        var tight = await _memory.RecallAsync(
            "gc pause", new RecallOptions { TopK = 4, TokenBudget = full / 3 });

        tight.Count.ShouldBe(4);
        tight.ShouldAllBe(m => m.Depth != null && m.Depth != RecallDepth.Full);
    }

    [Test]
    public async Task Recall_ABudgetCanOnlyShrinkTheResult_NeverEnlargeIt()
    {
        for (var i = 0; i < 6; i++)
            await _memory.CommitAsync(Rich($"p{i}", $"gc pause causes timeouts in service {i}"));

        var recalled = await _memory.RecallAsync(
            "gc pause", new RecallOptions { TopK = 2, TokenBudget = 1_000_000 });

        recalled.Count.ShouldBe(2);
    }

    // ── Claim B, first half: the advisory corpus is contradictable ──

    [Test]
    public async Task AdvisoryEntries_AreLabelledAsContradictableByWhatIsObservedNow()
    {
        await _memory.CommitAsync(Rich("stale", "always restart the service first"));

        var text = await RecallText();

        text.ShouldContain(EmpiricalRecallRenderer.AdvisoryTag);
        text.ShouldContain("trust what you observe");
        text.ShouldNotContain(EmpiricalRecallRenderer.RecordTag);
    }

    // ── Claim B, second half: a verified record is not discardable by opinion ──

    [Test]
    public async Task VerifiedRecords_AreLabelledWithTheOracleThatJudgedThem()
    {
        await _memory.CommitAsync(Rich("verified", "the loader handles empty files") with
        {
            Verification = new EmpiricalVerification
            {
                Oracle = "dotnet test",
                Passed = true,
                VerifiedAt = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero)
            }
        });

        var text = await RecallText();

        text.ShouldContain(EmpiricalRecallRenderer.RecordTag);
        text.ShouldContain("dotnet test");     // superseding it is a concrete instruction
        text.ShouldContain("2026-08-20");
        text.ShouldContain("supersede it only by re-running that check");
        text.ShouldNotContain("trust what you observe");
    }

    [Test]
    public async Task BothHalvesPresent_EachIsGivenItsOwnRule()
    {
        await _memory.CommitAsync(Rich("advice", "prefer canary deploys"));
        await _memory.CommitAsync(Rich("record", "prefer canary deploys, checked") with
        {
            Verification = new EmpiricalVerification
            {
                Oracle = "deploy-audit",
                Passed = false,
                VerifiedAt = DateTimeOffset.UtcNow
            }
        });

        var text = await RecallText("canary");

        text.ShouldContain(EmpiricalRecallRenderer.AdvisoryTag);
        text.ShouldContain(EmpiricalRecallRenderer.RecordTag);
        text.ShouldContain("trust what you observe");
        text.ShouldContain("supersede it only by re-running that check");
    }

    [Test]
    public void AFailedCheck_IsStillARecord()
    {
        // A recorded failure is as much an observed fact as a recorded pass, and losing that
        // distinction would let a run quietly retry something already known not to work.
        var entry = Rich("p1") with
        {
            Verification = new EmpiricalVerification
            {
                Oracle = "build",
                Passed = false,
                VerifiedAt = DateTimeOffset.UtcNow
            }
        };

        entry.IsVerifiedRecord.ShouldBeTrue();
        EmpiricalRecallRenderer.Render(Match(entry), RecallDepth.Abstract)
            .ShouldContain("failed \"build\"");
    }

    // ── Coverage ──

    [Test]
    public async Task RecallTool_WhenTheBudgetDropsMatches_SaysSoRatherThanLookingComplete()
    {
        for (var i = 0; i < 5; i++)
            await _memory.CommitAsync(Rich($"p{i}", $"gc pause causes timeouts in service {i}"));

        var ranked = await _memory.RecallAsync("gc pause", new RecallOptions { TopK = 5 });
        var oneAbstract = EmpiricalRecallRenderer.EstimateTokens(ranked[0], RecallDepth.Abstract);

        var kit = EmpiricalMemoryTools.Create(
            _memory,
            recallOptions: new RecallOptions { TopK = 5, TokenBudget = (oneAbstract * 2) + 1 });

        var result = await kit.Tools["recall_empirical"].ExecuteAsync(
            new Dictionary<string, object?> { ["situation"] = "gc pause" });

        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("were not loaded");
        result.Value.ShouldContain("Abstract depth");
    }

    [Test]
    public async Task RecallTool_WithNoBudget_RendersExactlyAsItAlwaysHas()
    {
        await _memory.CommitAsync(Rich("p1", "gc pause causes timeouts"));

        var kit = EmpiricalMemoryTools.Create(_memory);
        var result = await kit.Tools["recall_empirical"].ExecuteAsync(
            new Dictionary<string, object?> { ["situation"] = "gc pause" });

        result.Value.ShouldContain("Mechanism: heap grows");   // full depth
        result.Value.ShouldNotContain("were not loaded");
        result.Value.ShouldNotContain("depth to fit");
    }

    // ── Fixtures ──

    private async Task<string> RecallText(string situation = "restart")
    {
        var kit = EmpiricalMemoryTools.Create(_memory);
        var result = await kit.Tools["recall_empirical"].ExecuteAsync(
            new Dictionary<string, object?> { ["situation"] = situation }).ConfigureAwait(false);

        result.IsError.ShouldBeFalse();
        return result.Value;
    }

    private static EmpiricalMatch Match(EmpiricalEntry entry) => new() { Entry = entry, Score = 0.9f };

    private static IReadOnlyList<EmpiricalMatch> Ranked(int count) =>
        [.. Enumerable.Range(0, count).Select(i =>
            new EmpiricalMatch { Entry = Rich($"p{i}"), Score = 1f - (i * 0.1f) })];

    private static EmpiricalEntry Rich(string id, string summary = "a summary") => new()
    {
        Id = id,
        Kind = EmpiricalKind.Pattern,
        Tags = ["gc", "timeout"],
        Source = "test",
        Description = SemanticDescription.FromText(summary),
        Confidence = 0.8f,
        ObservationCount = 3,
        Evidence = ["log-1"],
        FirstObserved = DateTimeOffset.UtcNow,
        LastObserved = DateTimeOffset.UtcNow,
        Condition = "gc pause",
        Effect = "timeouts",
        Mechanism = "heap grows until the collector stalls the request thread for long enough",
        Latency = TimeSpan.FromMinutes(3)
    };
}
