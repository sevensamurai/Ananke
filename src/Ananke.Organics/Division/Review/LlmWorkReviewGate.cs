using System.Text.Json;
using Ananke.Abstractions.Agents;

namespace Ananke.Organics.Division.Review;

/// <summary>
/// <see cref="IWorkReviewGate"/> that asks an LLM reviewer for a structured JSON verdict.
/// </summary>
/// <param name="model">The model used to review the work item.</param>
/// <param name="systemPrompt">
/// Optional system prompt override. When <see langword="null"/>, a default reviewer prompt is used.
/// </param>
public sealed class LlmWorkReviewGate(
    IAgentModel model,
    string? systemPrompt = null) : IWorkReviewGate
{
    private const string DefaultReviewerId = "llm-reviewer";

    private const string DefaultSystemPrompt = """
        You are a work review gate for an autonomous engineering system.
        Review the submitted work item and respond with strict JSON.

        Respond with exactly one JSON object using this schema:
        {
          \"outcome\": \"Approved|Rejected|Revised\",
          \"comment\": \"short explanation\",
          \"reviewerId\": \"optional reviewer identifier\"
        }

        Keep the comment concise and do not emit markdown or extra text.
        """;

    /// <inheritdoc />
    public async Task<WorkReviewDecision> ReviewAsync(WorkItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var request = new AgentRequest
        {
            Messages =
            [
                AgentMessage.System(systemPrompt ?? DefaultSystemPrompt),
                AgentMessage.User(FormatPrompt(item))
            ]
        };

        var response = await model.GenerateAsync(request, ct).ConfigureAwait(false);
        var text = response.Text?.Trim() ?? string.Empty;

        return ParseResponse(text);
    }

    private static string FormatPrompt(WorkItem item) => $"""
        Work item:
          Id: {item.Id}
          Title: {item.Title}
          Kind: {item.Kind}

        Payload:
        {item.Payload}
        """;

    private static WorkReviewDecision ParseResponse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            var outcomeText = root.TryGetProperty("outcome", out var outcomeProperty)
                ? outcomeProperty.GetString()
                : null;
            var comment = root.TryGetProperty("comment", out var commentProperty)
                ? commentProperty.GetString()
                : null;
            var reviewerId = root.TryGetProperty("reviewerId", out var reviewerIdProperty)
                ? reviewerIdProperty.GetString()
                : null;

            if (!Enum.TryParse<WorkReviewOutcome>(outcomeText, ignoreCase: true, out var outcome))
            {
                return WorkReviewDecision.Reject(
                    comment: $"LLM response did not contain a valid outcome: {text}",
                    reviewerId: DefaultReviewerId);
            }

            return new WorkReviewDecision
            {
                Outcome = outcome,
                Comment = string.IsNullOrWhiteSpace(comment) ? "LLM review completed" : comment,
                ReviewerId = string.IsNullOrWhiteSpace(reviewerId) ? DefaultReviewerId : reviewerId
            };
        }
        catch (JsonException)
        {
            return WorkReviewDecision.Reject(
                comment: $"LLM response was not valid JSON: {text}",
                reviewerId: DefaultReviewerId);
        }
    }
}
