using Ananke.Abstractions.Tools.Routing;

namespace Ananke.Orchestration.Tools.Routing;

/// <summary>
/// Produces the system and user prompt text that an <see cref="LlmRouterStage"/> sends
/// to the cheap model in order to obtain a <see cref="ToolRoutingDecision"/>.
/// </summary>
public interface IRoutingPromptTemplate
{
    /// <summary>
    /// Renders the system prompt that instructs the model to return a JSON routing decision.
    /// </summary>
    /// <param name="request">The routing request containing candidates and conversation context.</param>
    string RenderSystemPrompt(ToolRoutingRequest request);

    /// <summary>
    /// Renders the user-turn prompt that presents the actual user message (and optional
    /// digest) to the cheap model.
    /// </summary>
    /// <param name="request">The routing request.</param>
    string RenderUserPrompt(ToolRoutingRequest request);

    /// <summary>
    /// Renders a corrective system prompt used on the retry pass when the first response
    /// could not be parsed.
    /// </summary>
    /// <param name="request">The original routing request.</param>
    /// <param name="previousResponse">The raw text the model returned that failed to parse.</param>
    string RenderRetrySystemPrompt(ToolRoutingRequest request, string previousResponse);
}
