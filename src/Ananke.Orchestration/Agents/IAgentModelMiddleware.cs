using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Intercepts individual LLM calls within any <see cref="IAgentModel"/> implementation.
/// Middlewares are invoked in registration order around each
/// <see cref="IAgentModel.GenerateAsync"/> and
/// <see cref="IStreamingAgentModel.GenerateStreamAsync"/> call.
/// </summary>
/// <remarks>
/// <para>
/// Use middleware for cross-cutting concerns that apply at the LLM call level:
/// PII redaction, prompt injection detection, content guardrails, logging, and
/// per-call observability.
/// </para>
/// <para>
/// Compose via <see cref="MiddlewareAgentModel"/>:
/// </para>
/// <code>
/// var model = MiddlewareAgentModel.Wrap(innerModel,
///     new LoggingAgentModelMiddleware(loggerFactory),
///     new GuardrailAgentModelMiddleware(denyPatterns));
/// </code>
/// <para>
/// Middleware ordering follows the registration order:
/// <c>OnBeforeGenerateAsync</c> runs first-to-last,
/// <c>OnAfterGenerateAsync</c> runs last-to-first (onion model).
/// </para>
/// </remarks>
public interface IAgentModelMiddleware
{
    /// <summary>
    /// Called before the request is sent to the model. Return a modified request
    /// to transform what the model sees (e.g., redact PII, inject system guardrails).
    /// Return the original <paramref name="request"/> unchanged if no transformation is needed.
    /// </summary>
    /// <param name="request">The outgoing request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The (possibly modified) request to forward.</returns>
    Task<AgentRequest> OnBeforeGenerateAsync(
        AgentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Called after the model returns a complete response. Return a modified response
    /// to transform what the caller sees (e.g., filter unsafe content, validate structure).
    /// Return the original <paramref name="response"/> unchanged if no transformation is needed.
    /// </summary>
    /// <param name="response">The model's response.</param>
    /// <param name="request">The request that produced this response (after all <c>OnBefore</c> transforms).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The (possibly modified) response to return.</returns>
    Task<AgentResponse> OnAfterGenerateAsync(
        AgentResponse response, AgentRequest request, CancellationToken ct = default);
}
