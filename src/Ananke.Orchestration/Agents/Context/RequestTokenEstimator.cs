using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Estimates the assembled size of a whole <see cref="AgentRequest"/> — system prompt, messages
/// and tool schemas — using <see cref="ApproximateTokenCounter"/>.
/// </summary>
/// <remarks>
/// One definition, used by anything that needs to ask "how big is this request". Tool schemas are
/// included because they are sent on every call and are frequently the largest single contributor.
/// </remarks>
internal static class RequestTokenEstimator
{
    internal static int Estimate(AgentRequest request, ITokenCounter? counter = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        counter ??= ApproximateTokenCounter.Instance;

        var total = counter.EstimateTokens(request.SystemPrompt ?? string.Empty);

        foreach (var message in request.Messages)
            total += counter.EstimateTokens(message);

        if (request.Tools is { Count: > 0 })
        {
            foreach (var tool in request.Tools)
            {
                total += counter.EstimateTokens(tool.Name)
                    + counter.EstimateTokens(tool.Description)
                    + counter.EstimateTokens(tool.ParametersJsonSchema);
            }
        }

        return total;
    }
}
