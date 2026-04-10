using Ananke.Orchestration.Knowledge;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Learning;
using Shouldly;

namespace Ananke.Learning.Tests;

[TestFixture]
public class EmpiricalMemoryToolsTests
{
    private InMemoryEmpiricalMemory _memory = null!;
    private InMemoryEmbedder _embedder = null!;

    [SetUp]
    public void SetUp()
    {
        _embedder = new InMemoryEmbedder();
        _memory = new InMemoryEmpiricalMemory(_embedder);
    }

    [Test]
    public void Create_ReturnsToolKitWithThreeTools()
    {
        var kit = EmpiricalMemoryTools.Create(_memory);

        kit.Name.ShouldBe("empirical");
        kit.Tools.ShouldContainKey("recall_empirical");
        kit.Tools.ShouldContainKey("commit_insight");
        kit.Tools.ShouldContainKey("reinforce_empirical");
        kit.Tools.Count.ShouldBe(3);
    }

    [Test]
    public async Task RecallTool_CallsRecallAsync_ReturnsFormattedResults()
    {
        await _memory.CommitAsync(new EmpiricalEntry
        {
            Id = "p1",
            Kind = EmpiricalKind.Pattern,
            Tags = ["gc", "timeout"],
            Source = "test",
            Description = SemanticDescription.FromText("GC pause causes timeout"),
            Confidence = 0.8f,
            ObservationCount = 3,
            Evidence = ["log-1"],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow
        });

        var kit = EmpiricalMemoryTools.Create(_memory);
        var tool = kit.Tools["recall_empirical"];

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["situation"] = "GC pause" });

        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("GC pause causes timeout");
        result.Value.ShouldContain("Pattern");
        result.Value.ShouldContain("confidence: 0.80");
    }

    [Test]
    public async Task CommitTool_CallsCommitAsync_ReturnsEntryId()
    {
        var kit = EmpiricalMemoryTools.Create(_memory);
        var tool = kit.Tools["commit_insight"];

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["description"] = "High CPU correlates with slow queries",
            ["kind"] = "pattern"
        });

        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("Entry committed");
        result.Value.ShouldContain("kind: Pattern");
        _memory.Count.ShouldBe(1);
    }

    [Test]
    public async Task CommitTool_InvalidKind_ReturnsError()
    {
        var kit = EmpiricalMemoryTools.Create(_memory);
        var tool = kit.Tools["commit_insight"];

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["description"] = "something",
            ["kind"] = "invalid_kind"
        });

        result.IsError.ShouldBeTrue();
        result.Value.ShouldContain("Invalid kind");
        _memory.Count.ShouldBe(0);
    }

    [Test]
    public async Task ReinforceTool_CallsReinforceAsync()
    {
        await _memory.CommitAsync(new EmpiricalEntry
        {
            Id = "p1",
            Kind = EmpiricalKind.Pattern,
            Tags = [],
            Source = "test",
            Description = SemanticDescription.FromText("test pattern"),
            Confidence = 0.5f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow
        });

        var kit = EmpiricalMemoryTools.Create(_memory);
        var tool = kit.Tools["reinforce_empirical"];

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["entry_id"] = "p1" });

        result.IsError.ShouldBeFalse();
        result.Value.ShouldContain("reinforced");

        var entry = await _memory.GetAsync("p1");
        entry!.Confidence.ShouldBeGreaterThan(0.5f);
        entry.ObservationCount.ShouldBe(2);
    }

    [Test]
    public async Task ReinforceTool_NonexistentEntry_ReturnsError()
    {
        var kit = EmpiricalMemoryTools.Create(_memory);
        var tool = kit.Tools["reinforce_empirical"];

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["entry_id"] = "nonexistent" });

        result.IsError.ShouldBeTrue();
        result.Value.ShouldContain("not found");
    }
}
