using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Shouldly;

namespace Ananke.Abstractions.Tests.Tools.Routing;

[TestFixture]
public sealed class ToolRoutingRequestTests
{
    [Test]
    public void MaxSelected_DefaultsTo8()
    {
        var request = new ToolRoutingRequest
        {
            UserMessage = "hello",
            Candidates = [],
        };

        request.MaxSelected.ShouldBe(8);
    }

    [Test]
    public void MaxSelected_CanBeOverridden()
    {
        var request = new ToolRoutingRequest
        {
            UserMessage = "hello",
            Candidates = [],
            MaxSelected = 3,
        };

        request.MaxSelected.ShouldBe(3);
    }

    [Test]
    public void ConversationDigest_DefaultsToNull()
    {
        var request = new ToolRoutingRequest
        {
            UserMessage = "hello",
            Candidates = [],
        };

        request.ConversationDigest.ShouldBeNull();
    }

    [Test]
    public void Candidates_IsStoredAsProvided()
    {
        var entry = new ToolMemoryEntry
        {
            ToolName = "tool_x",
            KitName = "kit",
            Description = "does stuff",
        };

        var request = new ToolRoutingRequest
        {
            UserMessage = "query",
            Candidates = [entry],
        };

        request.Candidates.ShouldHaveSingleItem();
        request.Candidates[0].ToolName.ShouldBe("tool_x");
    }
}
