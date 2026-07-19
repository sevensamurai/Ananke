using Ananke.Organics.Sensing;
using Shouldly;

namespace Ananke.Organics.Tests.Sensing;

[TestFixture]
public class InMemoryCapabilityMapTests
{
    private InMemoryCapabilityMap _landscape = null!;

    [SetUp]
    public void SetUp()
    {
        _landscape = new InMemoryCapabilityMap(signalTimeout: TimeSpan.FromSeconds(5));
    }

    private static WorkflowSignal MakeSignal(
        string workflowName,
        string domain,
        DateTimeOffset? timestamp = null,
        IReadOnlyList<string>? capabilities = null) => new()
        {
            WorkflowName = workflowName,
            Domain = domain,
            Capabilities = capabilities ?? ["tool-a"],
            Timestamp = timestamp ?? DateTimeOffset.UtcNow
        };

    // ── Absorb + Sense ──────────────────────────────────────────────

    [Test]
    public void AbsorbSignal_SenseByDomain_ReturnsAliveCell()
    {
        _landscape.Register(MakeSignal("cell-1", "search"));

        var result = _landscape.Discover("search");

        result.Count.ShouldBe(1);
        result[0].WorkflowName.ShouldBe("cell-1");
        result[0].Domain.ShouldBe("search");
        result[0].Alive.ShouldBeTrue();
    }

    [Test]
    public void Sense_DifferentDomain_ReturnsEmpty()
    {
        _landscape.Register(MakeSignal("cell-1", "search"));

        var result = _landscape.Discover("payment");

        result.ShouldBeEmpty();
    }

    [Test]
    public void Sense_IsCaseInsensitive()
    {
        _landscape.Register(MakeSignal("cell-1", "Search"));

        var result = _landscape.Discover("SEARCH");

        result.Count.ShouldBe(1);
    }

    [Test]
    public void SignalTimeout_SenseExcludesExpiredCell()
    {
        var expired = DateTimeOffset.UtcNow.AddSeconds(-10);
        _landscape.Register(MakeSignal("cell-1", "search", timestamp: expired));

        var result = _landscape.Discover("search");

        result.ShouldBeEmpty();
    }

    [Test]
    public void MultipleCellsSameDomain_SenseReturnsAll()
    {
        _landscape.Register(MakeSignal("cell-1", "search"));
        _landscape.Register(MakeSignal("cell-2", "search"));

        var result = _landscape.Discover("search");

        result.Count.ShouldBe(2);
    }

    // ── SenseAll ────────────────────────────────────────────────────

    [Test]
    public void SenseAll_ReturnsOnlyAlive()
    {
        var expired = DateTimeOffset.UtcNow.AddSeconds(-10);
        _landscape.Register(MakeSignal("alive-cell", "search"));
        _landscape.Register(MakeSignal("dead-cell", "payment", timestamp: expired));

        var result = _landscape.DiscoverAll();

        result.Count.ShouldBe(1);
        result[0].WorkflowName.ShouldBe("alive-cell");
    }

    // ── Forget ──────────────────────────────────────────────────────

    [Test]
    public void Forget_RemovesCellImmediately()
    {
        _landscape.Register(MakeSignal("cell-1", "search"));

        _landscape.Remove("cell-1");

        _landscape.Discover("search").ShouldBeEmpty();
        _landscape.DiscoverAll().ShouldBeEmpty();
    }

    [Test]
    public void Forget_UnknownCell_NoOp()
    {
        Should.NotThrow(() => _landscape.Remove("nonexistent"));
    }

    // ── Signal update ───────────────────────────────────────────────

    [Test]
    public void AbsorbUpdatesExistingSignal()
    {
        _landscape.Register(MakeSignal("cell-1", "search", capabilities: ["old-tool"]));
        _landscape.Register(MakeSignal("cell-1", "search", capabilities: ["new-tool"]));

        var result = _landscape.Discover("search");

        result.Count.ShouldBe(1);
        result[0].Capabilities.ShouldContain("new-tool");
        result[0].Capabilities.ShouldNotContain("old-tool");
    }
}
