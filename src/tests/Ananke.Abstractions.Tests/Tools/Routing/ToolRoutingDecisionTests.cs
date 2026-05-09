using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Shouldly;

namespace Ananke.Abstractions.Tests.Tools.Routing;

[TestFixture]
public sealed class ToolRoutingDecisionTests
{
    [Test]
    public void SelectedTools_DefaultsToEmpty()
    {
        var decision = new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.High,
        };

        decision.SelectedTools.ShouldBeEmpty();
    }

    [Test]
    public void RequiredFields_UseTools_MustBeSet()
    {
        var decision = new ToolRoutingDecision
        {
            UseTools = false,
            Confidence = RoutingConfidence.Low,
        };

        decision.UseTools.ShouldBeFalse();
    }

    [Test]
    public void RequiredFields_Confidence_MustBeSet()
    {
        var decision = new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.Medium,
        };

        decision.Confidence.ShouldBe(RoutingConfidence.Medium);
    }

    [Test]
    public void OptionalFields_DefaultToNull_OrFalse()
    {
        var decision = new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.High,
        };

        decision.ArgumentHint.ShouldBeNull();
        decision.Terminal.ShouldBeFalse();
        decision.Rationale.ShouldBeNull();
    }

    [Test]
    public void SelectedTools_CanBePopulated()
    {
        var entry = new ToolMemoryEntry
        {
            ToolName = "tool_a",
            KitName = "kit",
            Description = "desc",
        };

        var decision = new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.High,
            SelectedTools = [entry],
        };

        decision.SelectedTools.ShouldHaveSingleItem();
        decision.SelectedTools[0].ToolName.ShouldBe("tool_a");
    }
}
