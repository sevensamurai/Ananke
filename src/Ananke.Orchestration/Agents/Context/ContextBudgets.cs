using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>The budget one assembly is compacted against.</summary>
internal static class ContextBudgets
{
    /// <summary>
    /// The configured limit, with the window of the model that will answer <paramref name="request"/>
    /// when the model can say — a routed model forwards its router's answer.
    /// </summary>
    /// <param name="model">The model the request goes to.</param>
    /// <param name="request">What is about to be sent.</param>
    /// <param name="maxContextTokens">The caller's own limit, if any.</param>
    /// <param name="reservedTokens">What the tool schemas take before any message.</param>
    internal static ContextBudget For(
        IAgentModel model, AgentRequest request, int? maxContextTokens, int reservedTokens)
    {
        var window = model is IModelContextResolver resolver
            ? resolver.ResolveContextWindow(request)
            : ModelContextWindow.Unknown;

        if (!window.IsKnown && maxContextTokens is null)
            return ContextBudget.Unspecified;

        return new ContextBudget
        {
            MaxTokens = maxContextTokens,
            ModelContextTokens = window.ContextTokens,
            ModelName = window.ModelName,
            ReservedTokens = reservedTokens
        };
    }
}
