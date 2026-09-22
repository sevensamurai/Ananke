using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Covers the one-time warning raised when a free, lowest-tier model wins on price over a paid model
/// that was equally eligible.
/// </summary>
/// <remarks>
/// The hazard is real and silent: local and self-hosted models cost nothing, so under
/// <see cref="RoutingStrategy.CheapestFit"/> a zero-cost profile sorts ahead of everything it
/// qualifies against, and the speed tie-break then prefers the smallest of them. Adding one local
/// model for privacy re-routes work nobody intended to move. These tests pin both halves of the
/// answer — that it is reported, and that it stays quiet when there is nothing to report.
/// </remarks>
[TestFixture]
public class ZeroCostRoutingWarningTests
{
    [Test]
    public void ZeroCostModel_DisplacingAPaidCandidate_WarnsOncePerProcess()
    {
        var logger = new CollectingLogger();
        var freeName = UniqueName("free");

        var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit, logger)
            .AddModel(Free(freeName))
            .AddModel(Paid(UniqueName("paid")));

        var request = TextRequest();
        router.Select(request);
        router.Select(request);
        router.Select(request);

        var warnings = logger.Messages.Where(m => m.Contains(freeName)).ToList();
        warnings.Count.ShouldBe(1, "The warning is a one-time nudge, not per-request noise");
        warnings[0].ShouldContain("MinIntelligenceTier", Case.Insensitive);
    }

    [Test]
    public void ZeroCostModel_AsTheOnlyCandidate_IsSilent()
    {
        // A purely local setup has no hazard: nothing was displaced. Warning here would fire on
        // every request and teach people to filter the channel.
        var logger = new CollectingLogger();
        var freeName = UniqueName("free-only");

        new CapabilityModelRouter(RoutingStrategy.CheapestFit, logger)
            .AddModel(Free(freeName))
            .Select(TextRequest());

        logger.Messages.ShouldNotContain(m => m.Contains(freeName));
    }

    [Test]
    public void ZeroCostModel_TheCallerRatedHigher_IsSilent()
    {
        // An IntelligenceTier above 1 on a free model is a considered judgement about what it can be
        // trusted with, not an accident of pricing.
        var logger = new CollectingLogger();
        var freeName = UniqueName("free-rated");

        new CapabilityModelRouter(RoutingStrategy.CheapestFit, logger)
            .AddModel(Free(freeName) with { IntelligenceTier = 3 })
            .AddModel(Paid(UniqueName("paid")))
            .Select(TextRequest());

        logger.Messages.ShouldNotContain(m => m.Contains(freeName));
    }

    [Test]
    public void PaidModelSelected_IsSilent()
    {
        var logger = new CollectingLogger();
        var paidName = UniqueName("paid-only");

        new CapabilityModelRouter(RoutingStrategy.CheapestFit, logger)
            .AddModel(Paid(paidName))
            .Select(TextRequest());

        logger.Messages.ShouldNotContain(m => m.Contains(paidName));
    }

    [Test]
    public void ZeroCostModel_WinningUnderAStrategyThatDoesNotRankOnPrice_IsSilent()
    {
        // FastestFit ranks on speed and the free model happens to be fastest. It won on the axis the
        // caller chose, which is not the hazard this warning is about.
        var logger = new CollectingLogger();
        var freeName = UniqueName("free-fast");

        new CapabilityModelRouter(RoutingStrategy.FastestFit, logger)
            .AddModel(Free(freeName) with { SpeedTier = 5 })
            .AddModel(Paid(UniqueName("paid-slow")) with { SpeedTier = 2 })
            .Select(TextRequest());

        logger.Messages.ShouldNotContain(m => m.Contains(freeName));
    }

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// The warning ledger is static and per-process, so every test needs a name no other test has
    /// used — otherwise the second test to run sees a suppressed warning and fails for the wrong
    /// reason.
    /// </summary>
    private static string UniqueName(string prefix) => $"test-{prefix}-{Guid.NewGuid():N}";

    private static ModelProfile Free(string name) =>
        new() { Name = name, Model = new FakeModel(), IntelligenceTier = 1 };

    private static ModelProfile Paid(string name) =>
        new() { Name = name, Model = new FakeModel(), IntelligenceTier = 1, CostPer1KTokens = 0.002m };

    private static AgentRequest TextRequest() => new() { Messages = [AgentMessage.User("hi")] };

    private sealed class FakeModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
