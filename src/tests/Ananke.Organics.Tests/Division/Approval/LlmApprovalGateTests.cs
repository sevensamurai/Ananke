using Ananke.Abstractions.Agents;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Shouldly;

namespace Ananke.Organics.Tests;

[TestFixture]
public class LlmApprovalGateTests
{
    private static DivisionPlan CreatePlan() => new()
    {
        ParentWorkflow = "test-cell",
        Children =
        [
            new ChildSpec { Name = "test-a", Domain = "a", Tools = ["t1"], Jobs = ["j1"] },
            new ChildSpec { Name = "test-b", Domain = "b", Tools = ["t2"], Jobs = ["j2"] }
        ],
        Reason = "surface tension"
    };

    private static ComplexitySnapshot CreateSnapshot() => new()
    {
        WorkflowName = "test-cell",
        ToolCount = 8,
        JobCount = 4,
        TagClusterCount = 3,
        RoutingEntropy = 0.7f,
        ResourceSpan = 2,
        ContextUtilization = 0.4f,
        MeasuredAt = DateTimeOffset.UtcNow
    };

    [Test]
    public async Task ReviewAsync_ApprovedResponse_ReturnsApproved()
    {
        var model = new FakeAgentModel("APPROVED: Looks like a clean split");
        var gate = new LlmApprovalGate(model);

        var result = await gate.ReviewAsync(CreatePlan(), CreateSnapshot());

        result.IsApproved.ShouldBeTrue();
        result.Reason.ShouldBe("Looks like a clean split");
        result.ReviewedBy.ShouldBe("llm-supervisor");
    }

    [Test]
    public async Task ReviewAsync_RejectedResponse_ReturnsRejected()
    {
        var model = new FakeAgentModel("REJECTED: Tools are too coupled to split");
        var gate = new LlmApprovalGate(model);

        var result = await gate.ReviewAsync(CreatePlan(), CreateSnapshot());

        result.IsApproved.ShouldBeFalse();
        result.Reason.ShouldBe("Tools are too coupled to split");
    }

    [Test]
    public async Task ReviewAsync_UnstructuredResponse_TreatedAsRejection()
    {
        var model = new FakeAgentModel("I think maybe we should wait");
        var gate = new LlmApprovalGate(model);

        var result = await gate.ReviewAsync(CreatePlan(), CreateSnapshot());

        result.IsApproved.ShouldBeFalse();
        result.Reason.ShouldContain("did not follow expected format");
    }

    [Test]
    public async Task ReviewAsync_CustomSystemPrompt_PassedToModel()
    {
        string? capturedSystem = null;
        var model = new FakeAgentModel("APPROVED: ok", onRequest: req =>
        {
            capturedSystem = req.Messages[0].Content;
        });
        var gate = new LlmApprovalGate(model, systemPrompt: "You are a custom reviewer.");

        await gate.ReviewAsync(CreatePlan(), CreateSnapshot());

        capturedSystem.ShouldBe("You are a custom reviewer.");
    }

    [Test]
    public async Task ReviewAsync_PromptContainsPlanDetails()
    {
        string? capturedPrompt = null;
        var model = new FakeAgentModel("APPROVED: ok", onRequest: req =>
        {
            capturedPrompt = req.Messages[1].Content;
        });
        var gate = new LlmApprovalGate(model);

        await gate.ReviewAsync(CreatePlan(), CreateSnapshot());

        capturedPrompt.ShouldNotBeNull();
        capturedPrompt.ShouldContain("test-cell");
        capturedPrompt.ShouldContain("test-a");
        capturedPrompt.ShouldContain("test-b");
        capturedPrompt.ShouldContain("surface tension");
    }

    [Test]
    public void ReviewAsync_NullPlan_Throws()
    {
        var model = new FakeAgentModel("APPROVED: ok");
        var gate = new LlmApprovalGate(model);

        Should.ThrowAsync<ArgumentNullException>(
            () => gate.ReviewAsync(null!, CreateSnapshot()));
    }

    [Test]
    public void ReviewAsync_NullSnapshot_Throws()
    {
        var model = new FakeAgentModel("APPROVED: ok");
        var gate = new LlmApprovalGate(model);

        Should.ThrowAsync<ArgumentNullException>(
            () => gate.ReviewAsync(CreatePlan(), null!));
    }

    /// <summary>Minimal fake that returns a fixed text response.</summary>
    private sealed class FakeAgentModel(string responseText, Action<AgentRequest>? onRequest = null) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            onRequest?.Invoke(request);
            return Task.FromResult(new AgentResponse { Text = responseText });
        }
    }
}
