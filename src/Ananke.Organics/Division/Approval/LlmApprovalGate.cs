using Ananke.Abstractions.Agents;

namespace Ananke.Organics.Division.Approval;

/// <summary>
/// <see cref="IDivisionApprovalGate"/> that sends the proposed division plan to
/// an LLM supervisor for review. The model is asked to approve, reject, or
/// suggest modifications based on the plan details and complexity metrics.
/// </summary>
/// <remarks>
/// <para>
/// This gate is useful for autonomous oversight: a cheaper or more conservative
/// model can review division decisions made by a threshold policy, catching
/// cases where structural heuristics propose a split that doesn't make
/// semantic sense (e.g. splitting tightly coupled tools across children).
/// </para>
/// <para>
/// The model response is parsed for approval keywords. If the response contains
/// <c>"APPROVED"</c> the plan proceeds; if it contains <c>"REJECTED"</c> it is
/// blocked. Any other response is treated as a rejection with the model's
/// explanation preserved in <see cref="DivisionApproval.Reason"/>.
/// </para>
/// </remarks>
/// <param name="model">The LLM to use for supervision.</param>
/// <param name="systemPrompt">
/// Optional system prompt override. When <see langword="null"/>, a default
/// prompt that explains the reviewer role is used.
/// </param>
public sealed class LlmApprovalGate(
    IAgentModel model,
    string? systemPrompt = null) : IDivisionApprovalGate
{
    private const string DefaultSystemPrompt = """
        You are a division supervisor for an autonomous agent kernel. Your job is to
        review proposed cell division plans and decide whether they should proceed.

        A cell divides when its structural complexity (tool count, tag clusters,
        routing entropy) exceeds thresholds. You must evaluate whether the proposed
        split is semantically coherent — do the child cells have clear, distinct
        domains? Are tightly coupled tools kept together?

        Respond with EXACTLY one of:
        - "APPROVED: <reason>" if the division makes sense
        - "REJECTED: <reason>" if the division should be blocked

        Be concise. One or two sentences for the reason.
        """;

    /// <inheritdoc />
    public async Task<DivisionApproval> ReviewAsync(
        DivisionPlan plan,
        ComplexitySnapshot snapshot,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);

        var prompt = FormatPrompt(plan, snapshot);

        var request = new AgentRequest
        {
            Messages =
            [
                AgentMessage.System(systemPrompt ?? DefaultSystemPrompt),
                AgentMessage.User(prompt)
            ]
        };

        var response = await model.GenerateAsync(request, ct).ConfigureAwait(false);
        var text = response.Text?.Trim() ?? "";

        return ParseResponse(text);
    }

    private static string FormatPrompt(DivisionPlan plan, ComplexitySnapshot snapshot)
    {
        var children = string.Join("\n", plan.Children.Select(c =>
            $"  - {c.Name} (domain: {c.Domain}, tools: [{string.Join(", ", c.Tools)}], " +
            $"jobs: [{string.Join(", ", c.Jobs)}])"));

        return $"""
            Cell "{plan.ParentWorkflow}" is proposed for division.

            Reason: {plan.Reason}

            Complexity metrics:
              Tools: {snapshot.ToolCount}
              Jobs: {snapshot.JobCount}
              Tag clusters: {snapshot.TagClusterCount}
              Routing entropy: {snapshot.RoutingEntropy:F2}
              Resource span: {snapshot.ResourceSpan}
              Context utilization: {snapshot.ContextUtilization:P0}

            Proposed children:
            {children}

            Should this division proceed?
            """;
    }

    private static DivisionApproval ParseResponse(string text)
    {
        if (text.StartsWith("APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            var reason = text.Length > 9 ? text[9..].TrimStart(':', ' ') : "LLM approved";
            return DivisionApproval.Approve(reason, reviewedBy: "llm-supervisor");
        }

        if (text.StartsWith("REJECTED", StringComparison.OrdinalIgnoreCase))
        {
            var reason = text.Length > 9 ? text[9..].TrimStart(':', ' ') : "LLM rejected";
            return DivisionApproval.Reject(reason, reviewedBy: "llm-supervisor");
        }

        // Unstructured response — treat as rejection with full explanation.
        return DivisionApproval.Reject(
            reason: $"LLM response did not follow expected format: {text}",
            reviewedBy: "llm-supervisor");
    }
}
