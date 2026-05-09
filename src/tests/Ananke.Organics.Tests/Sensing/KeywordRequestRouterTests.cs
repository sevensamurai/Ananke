using Ananke.Organics.Sensing;
using Shouldly;

namespace Ananke.Organics.Tests.Sensing;

[TestFixture]
public class KeywordRequestRouterTests
{
    private InMemoryCapabilityMap _landscape = null!;
    private KeywordRequestRouter _router = null!;

    [SetUp]
    public void SetUp()
    {
        _landscape = new InMemoryCapabilityMap();
        _router = new KeywordRequestRouter(_landscape);
    }

    private static WorkflowSignal MakeSignal(string workflowName, string domain) => new()
    {
        WorkflowName = workflowName,
        Domain = domain,
        Capabilities = [$"{domain}-tool"],
        Timestamp = DateTimeOffset.UtcNow
    };

    // ── Domain keyword matching ─────────────────────────────────────

    [Test]
    public async Task RouteAsync_MatchesDomainKeyword()
    {
        _landscape.Register(MakeSignal("browse-cell", "browse"));
        _landscape.Register(MakeSignal("payment-cell", "payment"));

        var result = await _router.RouteAsync("I want to browse books");

        result.ShouldBe("browse-cell");
    }

    [Test]
    public async Task RouteAsync_CaseInsensitiveMatch()
    {
        _landscape.Register(MakeSignal("search-cell", "search"));

        var result = await _router.RouteAsync("SEARCH for something");

        result.ShouldBe("search-cell");
    }

    // ── Round-robin ─────────────────────────────────────────────────

    [Test]
    public async Task RouteAsync_MultipleCellsSameDomain_RoundRobins()
    {
        _landscape.Register(MakeSignal("search-1", "search"));
        _landscape.Register(MakeSignal("search-2", "search"));

        var results = new HashSet<string>();
        for (var i = 0; i < 10; i++)
            results.Add(await _router.RouteAsync("search for books"));

        results.Count.ShouldBe(2);
        results.ShouldContain("search-1");
        results.ShouldContain("search-2");
    }

    // ── Fallback ────────────────────────────────────────────────────

    [Test]
    public async Task RouteAsync_NoMatch_FallsBackToAliveCell()
    {
        _landscape.Register(MakeSignal("general-cell", "general"));

        var result = await _router.RouteAsync("something completely unrelated");

        result.ShouldBe("general-cell");
    }

    // ── Error cases ─────────────────────────────────────────────────

    [Test]
    public void RouteAsync_NoAliveCells_Throws()
    {
        Should.ThrowAsync<InvalidOperationException>(
            () => _router.RouteAsync("hello"));
    }
}
