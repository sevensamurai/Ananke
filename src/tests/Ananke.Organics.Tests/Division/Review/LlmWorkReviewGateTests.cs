using Ananke.Abstractions.Agents;
using Ananke.Organics.Division.Review;
using Shouldly;

namespace Ananke.Organics.Tests;

[TestFixture]
public class LlmWorkReviewGateTests
{
    private static WorkItem CreateWorkItem() => new()
    {
        Id = "pr-123",
        Title = "Refactor approval gates",
        Kind = WorkItemKind.PullRequest,
        Payload = "Diff summary and rationale"
    };

    [Test]
    public async Task ReviewAsync_ApprovedJson_ReturnsApproved()
    {
        var model = new FakeAgentModel("{\"outcome\":\"Approved\",\"comment\":\"Looks good\",\"reviewerId\":\"llm-a\"}");
        var gate = new LlmWorkReviewGate(model);

        var result = await gate.ReviewAsync(CreateWorkItem());

        result.Outcome.ShouldBe(WorkReviewOutcome.Approved);
        result.Comment.ShouldBe("Looks good");
        result.ReviewerId.ShouldBe("llm-a");
    }

    [Test]
    public async Task ReviewAsync_RejectedJson_ReturnsRejected()
    {
        var model = new FakeAgentModel("{\"outcome\":\"Rejected\",\"comment\":\"Needs tests\"}");
        var gate = new LlmWorkReviewGate(model);

        var result = await gate.ReviewAsync(CreateWorkItem());

        result.Outcome.ShouldBe(WorkReviewOutcome.Rejected);
        result.Comment.ShouldBe("Needs tests");
        result.ReviewerId.ShouldBe("llm-reviewer");
    }

    [Test]
    public async Task ReviewAsync_InvalidJson_ReturnsRejected()
    {
        var model = new FakeAgentModel("not-json");
        var gate = new LlmWorkReviewGate(model);

        var result = await gate.ReviewAsync(CreateWorkItem());

        result.Outcome.ShouldBe(WorkReviewOutcome.Rejected);
        result.Comment.ShouldContain("not valid JSON");
    }

    private sealed class FakeAgentModel(string responseText) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = responseText });
    }
}
