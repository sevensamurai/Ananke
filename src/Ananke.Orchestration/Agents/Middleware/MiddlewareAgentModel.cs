using System.Runtime.CompilerServices;

using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Middleware;

/// <summary>
/// Decorator that applies a pipeline of <see cref="IAgentModelMiddleware"/> instances
/// around any <see cref="IStreamingAgentModel"/>. Composes with
/// <see cref="ResilientAgentModel"/> and <see cref="CachingAgentModel"/>.
/// </summary>
/// <remarks>
/// <para><b>Composition order (outermost → innermost):</b></para>
/// <code>
/// User code
///   → MiddlewareAgentModel (PII redaction, guardrails, logging)
///     → ResilientAgentModel (429 retry)
///       → CachingAgentModel (response cache)
///         → OpenAIChatAgentModel / AnthropicAgentModel / GeminiAgentModel
/// </code>
/// <para>
/// Each layer is optional and independently composable.
/// </para>
/// <para><b>Middleware execution order:</b></para>
/// <list type="bullet">
///   <item><c>OnBeforeGenerateAsync</c> — runs first-to-last (request pipeline)</item>
///   <item><c>OnAfterGenerateAsync</c> — runs last-to-first (response pipeline / onion)</item>
/// </list>
/// <para><b>Streaming behavior:</b></para>
/// <list type="bullet">
///   <item><c>OnBeforeGenerateAsync</c> runs before the stream starts (transforms the request)</item>
///   <item><c>OnAfterGenerateAsync</c> runs after the stream completes (transforms the final
///     <see cref="AgentResponse"/> carried by the last chunk)</item>
///   <item>Individual stream chunks are <b>not</b> intercepted — this preserves streaming latency</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var model = MiddlewareAgentModel.Wrap(innerModel,
///     new LoggingAgentModelMiddleware(loggerFactory),
///     new GuardrailAgentModelMiddleware(denyPatterns));
///
/// var response = await model.GenerateAsync(request, ct);
/// </code>
/// </example>
public sealed class MiddlewareAgentModel : IStreamingAgentModel
{
    private readonly IStreamingAgentModel _inner;
    private readonly IReadOnlyList<IAgentModelMiddleware> _middlewares;

    /// <summary>
    /// Creates a middleware-wrapped model.
    /// </summary>
    /// <param name="inner">The model to delegate to after all <c>OnBefore</c> transforms.</param>
    /// <param name="middlewares">
    /// Middleware pipeline. <c>OnBefore</c> runs first-to-last;
    /// <c>OnAfter</c> runs last-to-first.
    /// </param>
    public MiddlewareAgentModel(
        IStreamingAgentModel inner,
        IEnumerable<IAgentModelMiddleware> middlewares)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(middlewares);
        _inner = inner;
        _middlewares = middlewares.ToList();
    }

    /// <summary>
    /// Convenience factory that wraps a model with one or more middlewares.
    /// </summary>
    public static MiddlewareAgentModel Wrap(
        IStreamingAgentModel inner,
        params IAgentModelMiddleware[] middlewares) =>
        new(inner, middlewares);

    /// <summary>
    /// Convenience factory that wraps a non-streaming <see cref="IAgentModel"/> by
    /// adapting it to <see cref="IStreamingAgentModel"/> first.
    /// </summary>
    public static MiddlewareAgentModel Wrap(
        IAgentModel inner,
        params IAgentModelMiddleware[] middlewares) =>
        new(inner as IStreamingAgentModel ?? new NonStreamingAdapter(inner), middlewares);

    /// <inheritdoc />
    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var transformed = await RunBeforePipelineAsync(request, ct);
        var response = await _inner.GenerateAsync(transformed, ct);
        return await RunAfterPipelineAsync(response, transformed, ct);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var transformed = await RunBeforePipelineAsync(request, ct);

        await foreach (var chunk in _inner.GenerateStreamAsync(transformed, ct))
        {
            if (chunk.CompletedResponse is not null)
            {
                // Run the after-pipeline on the final assembled response
                var transformedResponse = await RunAfterPipelineAsync(
                    chunk.CompletedResponse, transformed, ct);
                yield return chunk with { CompletedResponse = transformedResponse };
            }
            else
            {
                yield return chunk;
            }
        }
    }

    private async Task<AgentRequest> RunBeforePipelineAsync(AgentRequest request, CancellationToken ct)
    {
        var current = request;
        for (var i = 0; i < _middlewares.Count; i++)
            current = await _middlewares[i].OnBeforeGenerateAsync(current, ct);
        return current;
    }

    private async Task<AgentResponse> RunAfterPipelineAsync(
        AgentResponse response, AgentRequest request, CancellationToken ct)
    {
        var current = response;
        // Reverse order (onion model)
        for (var i = _middlewares.Count - 1; i >= 0; i--)
            current = await _middlewares[i].OnAfterGenerateAsync(current, request, ct);
        return current;
    }

    /// <summary>
    /// Adapter for non-streaming models, buffers the full response into a single-chunk stream.
    /// </summary>
    private sealed class NonStreamingAdapter(IAgentModel inner) : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            inner.GenerateAsync(request, ct);

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await inner.GenerateAsync(request, ct);
            if (response.Text is not null)
                yield return new AgentStreamChunk { TextDelta = response.Text };
            yield return new AgentStreamChunk { CompletedResponse = response };
        }
    }
}
