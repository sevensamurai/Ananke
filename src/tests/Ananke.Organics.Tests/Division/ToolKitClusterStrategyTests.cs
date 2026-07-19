using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Division;
using Shouldly;

namespace Ananke.Organics.Tests.Division;

[TestFixture]
public class ToolKitClusterStrategyTests
{
    private static WorkflowManifest MakeManifest() => new()
    {
        Name = "test",
        Models = new Dictionary<string, ModelDefinition>
        {
            ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
        },
        Jobs = new Dictionary<string, JobDefinition>
        {
            ["handle-request"] = new() { Type = "agent", ModelAlias = "default" },
            ["respond"] = new() { Type = "code" }
        },
        Connections = ["handle-request -> respond"]
    };

    private static ToolKit MakeCatalogKit() => new ToolKit("catalog-tools")
        .AddTool("search", "Search", (_) => ToolResult.Ok("ok"), "q", "query")
        .AddTool("details", "Details", (_) => ToolResult.Ok("ok"), "id", "id");

    private static ToolKit MakeOrderKit() => new ToolKit("order-tools")
        .AddTool("pay", "Pay", (_) => ToolResult.Ok("ok"), "amt", "amount")
        .AddTool("ship", "Ship", (_) => ToolResult.Ok("ok"), "id", "order")
        .AddTool("returns", "Returns", (_) => ToolResult.Ok("ok"), "id", "order");

    [Test]
    public void Split_TwoKits_ProducesTwoChildren()
    {
        var strategy = new ToolKitClusterStrategy(MakeCatalogKit(), MakeOrderKit());

        var children = strategy.Split("bookstore-general", MakeManifest());

        children.Count.ShouldBe(2);
    }

    [Test]
    public void Split_ChildNames_DerivedFromParentAndKit()
    {
        var strategy = new ToolKitClusterStrategy(MakeCatalogKit(), MakeOrderKit());

        var children = strategy.Split("bookstore-general", MakeManifest());

        children[0].Name.ShouldBe("bookstore-general-catalog");
        children[1].Name.ShouldBe("bookstore-general-order");
    }

    [Test]
    public void Split_Domains_DerivedFromKitNames()
    {
        var strategy = new ToolKitClusterStrategy(MakeCatalogKit(), MakeOrderKit());

        var children = strategy.Split("parent", MakeManifest());

        children[0].Domain.ShouldBe("catalog");
        children[1].Domain.ShouldBe("order");
    }

    [Test]
    public void Split_ToolsAssigned_MatchKit()
    {
        var strategy = new ToolKitClusterStrategy(MakeCatalogKit(), MakeOrderKit());

        var children = strategy.Split("parent", MakeManifest());

        children[0].Tools.ShouldBe(["search", "details"]);
        children[1].Tools.ShouldBe(["pay", "ship", "returns"]);
    }

    [Test]
    public void Split_AllChildrenShareParentJobs()
    {
        var strategy = new ToolKitClusterStrategy(MakeCatalogKit(), MakeOrderKit());

        var children = strategy.Split("parent", MakeManifest());

        foreach (var child in children)
            child.Jobs.ShouldBe(["handle-request", "respond"]);
    }

    [Test]
    public void Split_SingleKit_ReturnsEmpty()
    {
        var strategy = new ToolKitClusterStrategy(MakeCatalogKit());

        var children = strategy.Split("parent", MakeManifest());

        children.ShouldBeEmpty();
    }

    [Test]
    public void Split_ThreeKits_ProducesThreeChildren()
    {
        var paymentKit = new ToolKit("payment-toolkit")
            .AddTool("charge", "Charge", (_) => ToolResult.Ok("ok"), "amt", "amount");

        var strategy = new ToolKitClusterStrategy(MakeCatalogKit(), MakeOrderKit(), paymentKit);

        var children = strategy.Split("parent", MakeManifest());

        children.Count.ShouldBe(3);
        children[2].Name.ShouldBe("parent-payment");
        children[2].Domain.ShouldBe("payment");
        children[2].Tools.ShouldBe(["charge"]);
    }

    [Test]
    public void Split_KitNameWithoutSuffix_UsedAsIs()
    {
        var kit = new ToolKit("analytics")
            .AddTool("report", "Report", (_) => ToolResult.Ok("ok"), "id", "id");

        var strategy = new ToolKitClusterStrategy(kit, MakeCatalogKit());

        var children = strategy.Split("parent", MakeManifest());

        children[0].Domain.ShouldBe("analytics");
        children[0].Name.ShouldBe("parent-analytics");
    }

    [Test]
    public void Split_IntegratesWithThresholdPolicy()
    {
        var strategy = new ToolKitClusterStrategy(MakeCatalogKit(), MakeOrderKit());
        var policy = new ThresholdDivisionPolicy(
            minTools: 4, minClusters: 2,
            clusterStrategy: strategy.Split);

        var snapshot = new ComplexitySnapshot
        {
            WorkflowName = "bookstore",
            ToolCount = 5,
            JobCount = 2,
            TagClusterCount = 2,
            RoutingEntropy = 0.8f,
            ResourceSpan = 2,
            ContextUtilization = 0.4f,
            MeasuredAt = DateTimeOffset.UtcNow
        };

        var plan = policy.EvaluateAsync(snapshot, MakeManifest()).Result;

        plan.ShouldNotBeNull();
        plan.Children.Count.ShouldBe(2);
        plan.Children[0].Tools.ShouldContain("search");
        plan.Children[1].Tools.ShouldContain("pay");
    }
}
