using Ananke.Organics.Kernel.Snapshots;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel.Snapshots;

[TestFixture]
public class WorkflowSnapshotBuilderTests
{
    [Test]
    public void Build_MinimalCell_HasDefaults()
    {
        var snap = new WorkflowSnapshotBuilder("my-cell", "catalog").Build();

        snap.Name.ShouldBe("my-cell");
        snap.Domain.ShouldBe("catalog");
        snap.SplitFrom.ShouldBeNull();
        snap.Tools.ShouldBeEmpty();
        snap.Connections.ShouldContain("handle-request -> respond");
        snap.Jobs.ShouldContainKey("handle-request");
        snap.Jobs["handle-request"].Type.ShouldBe("agent");
        snap.Jobs.ShouldContainKey("respond");
        snap.Jobs["respond"].Type.ShouldBe("code");
        snap.Models.ShouldContainKey("default");
        snap.Models["default"].Provider.ShouldBe("openai");
        snap.MemoryProfile.ShouldBeNull();
    }

    [Test]
    public void Tools_FromToolKit_SetsToolNames()
    {
        var toolKit = new ToolKit("test")
            .AddTool("tool_a", "desc", (_) => ToolResult.Ok("ok"), "p", "desc")
            .AddTool("tool_b", "desc", (_) => ToolResult.Ok("ok"), "p", "desc");

        var snap = new WorkflowSnapshotBuilder("cell", "domain")
            .Tools(toolKit)
            .Build();

        snap.Tools.ShouldBe(["tool_a", "tool_b"]);
    }

    [Test]
    public void Tools_FromList_SetsToolNames()
    {
        var snap = new WorkflowSnapshotBuilder("cell", "domain")
            .Tools(["search", "lookup"])
            .Build();

        snap.Tools.ShouldBe(["search", "lookup"]);
    }

    [Test]
    public void DividedFrom_SetsLineage()
    {
        var snap = new WorkflowSnapshotBuilder("child", "orders")
            .SplitFrom("parent")
            .Build();

        snap.SplitFrom.ShouldBe("parent");
    }

    [Test]
    public void Memory_SetsDomainsAndLineage()
    {
        var snap = new WorkflowSnapshotBuilder("cell", "orders")
            .Memory(["orders", "general"], lineageTags: ["bookstore"])
            .Build();

        snap.MemoryProfile.ShouldNotBeNull();
        snap.MemoryProfile.Domains.ShouldBe(["orders", "general"]);
        snap.MemoryProfile.LineageTags.ShouldBe(["bookstore"]);
    }

    [Test]
    public void Memory_NoLineage_DefaultsToEmpty()
    {
        var snap = new WorkflowSnapshotBuilder("cell", "orders")
            .Memory(["orders"])
            .Build();

        snap.MemoryProfile.ShouldNotBeNull();
        snap.MemoryProfile.LineageTags.ShouldBeEmpty();
    }

    [Test]
    public void Model_OverridesDefault()
    {
        var snap = new WorkflowSnapshotBuilder("cell", "domain")
            .Model("default", "anthropic", "claude-4-sonnet")
            .Build();

        snap.Models["default"].Provider.ShouldBe("anthropic");
        snap.Models["default"].Model.ShouldBe("claude-4-sonnet");
    }

    [Test]
    public void Model_AddsMultipleAliases()
    {
        var snap = new WorkflowSnapshotBuilder("cell", "domain")
            .Model("fast", "openai", "gpt-4o-mini")
            .Model("smart", "anthropic", "claude-4-sonnet")
            .Build();

        snap.Models.Count.ShouldBeGreaterThanOrEqualTo(3); // default + fast + smart
        snap.Models["fast"].Provider.ShouldBe("openai");
        snap.Models["smart"].Provider.ShouldBe("anthropic");
    }

    [Test]
    public void AgentJob_ReplacesDefault()
    {
        var snap = new WorkflowSnapshotBuilder("cell", "domain")
            .AgentJob("handle-request", systemPrompt: "Be helpful")
            .Build();

        snap.Jobs["handle-request"].SystemPrompt.ShouldBe("Be helpful");
    }

    [Test]
    public void CodeJob_AddsCodeJob()
    {
        var snap = new WorkflowSnapshotBuilder("cell", "domain")
            .CodeJob("transform")
            .Build();

        snap.Jobs["transform"].Type.ShouldBe("code");
    }

    [Test]
    public void Connections_OverridesDefault()
    {
        var snap = new WorkflowSnapshotBuilder("cell", "domain")
            .Connections("plan -> execute", "execute -> respond")
            .Build();

        snap.Connections.ShouldBe(["plan -> execute", "execute -> respond"]);
    }

    [Test]
    public void NullName_Throws()
    {
        Should.Throw<ArgumentException>(() => new WorkflowSnapshotBuilder(null!, "domain"));
    }

    [Test]
    public void NullDomain_Throws()
    {
        Should.Throw<ArgumentException>(() => new WorkflowSnapshotBuilder("cell", null!));
    }

    [Test]
    public void NullToolKit_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new WorkflowSnapshotBuilder("cell", "domain").Tools((ToolKit)null!));
    }

    [Test]
    public void FullBuilder_AllFieldsSet()
    {
        var snap = new WorkflowSnapshotBuilder("bookstore-orders", "orders")
            .Tools(["create_order", "process_payment"])
            .SplitFrom("bookstore-general")
            .Model("default", "openai", "gpt-4o-mini")
            .AgentJob("handle-request", systemPrompt: "Handle orders")
            .CodeJob("respond")
            .Connections("handle-request -> respond")
            .Memory(["orders", "general"], lineageTags: ["bookstore"])
            .Build();

        snap.Name.ShouldBe("bookstore-orders");
        snap.Domain.ShouldBe("orders");
        snap.SplitFrom.ShouldBe("bookstore-general");
        snap.Tools.ShouldBe(["create_order", "process_payment"]);
        snap.Jobs["handle-request"].SystemPrompt.ShouldBe("Handle orders");
        snap.MemoryProfile!.Domains.ShouldBe(["orders", "general"]);
    }
}
