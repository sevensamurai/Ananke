namespace Ananke.Orchestration.Agents.Middleware;

/// <summary>
/// Controls how <see cref="MiddlewareAgentModel"/> applies its after-pipeline (response
/// middleware — guardrails, redaction, logging) to a streaming response.
/// </summary>
public enum StreamingMode
{
    /// <summary>
    /// Individual chunks reach the consumer as they arrive; the after-pipeline runs only once,
    /// on the final assembled response. Lowest latency, but a guardrail that blocks the final
    /// response runs <em>after</em> the content has already streamed to the consumer — the
    /// exception only stops the final <c>CompletedResponse</c> chunk from carrying a clean
    /// result, it can't un-send chunks already yielded.
    /// </summary>
    PassThrough,

    /// <summary>
    /// All chunks are collected before anything reaches the consumer; the after-pipeline runs
    /// once the stream completes, and chunks are replayed only if it passes. A blocking
    /// exception (e.g. <see cref="GuardrailException"/>) propagates before any chunk is
    /// yielded — no partial leakage. Trades streaming latency (the consumer sees nothing until
    /// the full response is in and validated) for that guarantee. Recommended whenever a
    /// guardrail's deny rules carry PII or security semantics.
    /// </summary>
    Buffered
}
