using Ananke.Organics.Healing;

namespace Ananke.Organics.Division;

/// <summary>
/// Factory methods that produce pre-populated <see cref="FailureClassifierBuilder"/>
/// instances tuned for specific AI provider error vocabularies.
/// </summary>
public static class FailureClassifierProfiles
{
    /// <summary>
    /// Returns a builder pre-loaded with upstream failure patterns for the
    /// OpenAI API (HTTP rate limits, quota errors, server errors, network
    /// transients, and common exception type names).
    /// </summary>
    public static FailureClassifierBuilder OpenAI() =>
        new FailureClassifierBuilder()
            // HTTP status codes and messages
            .AddPattern(FailureOrigin.Upstream, "429")
            .AddPattern(FailureOrigin.Upstream, "502")
            .AddPattern(FailureOrigin.Upstream, "503")
            .AddPattern(FailureOrigin.Upstream, "504")
            .AddPattern(FailureOrigin.Upstream, "Too Many Requests")
            .AddPattern(FailureOrigin.Upstream, "Service Unavailable")
            .AddPattern(FailureOrigin.Upstream, "Bad Gateway")
            .AddPattern(FailureOrigin.Upstream, "Gateway Timeout")
            // Network / timeout
            .AddPattern(FailureOrigin.Upstream, "HttpRequestException")
            .AddPattern(FailureOrigin.Upstream, "TaskCanceledException")
            .AddPattern(FailureOrigin.Upstream, "TimeoutException")
            .AddPattern(FailureOrigin.Upstream, "SocketException")
            .AddPattern(FailureOrigin.Upstream, "IOException")
            .AddPattern(FailureOrigin.Upstream, "timed out")
            .AddPattern(FailureOrigin.Upstream, "connection refused")
            .AddPattern(FailureOrigin.Upstream, "network error")
            // OpenAI-specific
            .AddPattern(FailureOrigin.Upstream, "rate limit")
            .AddPattern(FailureOrigin.Upstream, "Rate limit")
            .AddPattern(FailureOrigin.Upstream, "quota exceeded")
            .AddPattern(FailureOrigin.Upstream, "overloaded")
            .AddPattern(FailureOrigin.Upstream, "model_not_available")
            .AddPattern(FailureOrigin.Upstream, "server_error")
            .AddPattern(FailureOrigin.Upstream, "InternalServerError")
            .AddPattern(FailureOrigin.Upstream, "ServiceUnavailable");

    /// <summary>
    /// Returns a builder pre-loaded with upstream failure patterns for the
    /// Anthropic Claude API.
    /// </summary>
    public static FailureClassifierBuilder Anthropic() =>
        new FailureClassifierBuilder()
            .AddPattern(FailureOrigin.Upstream, "429")
            .AddPattern(FailureOrigin.Upstream, "529")
            .AddPattern(FailureOrigin.Upstream, "overloaded_error")
            .AddPattern(FailureOrigin.Upstream, "rate_limit_error")
            .AddPattern(FailureOrigin.Upstream, "api_error")
            .AddPattern(FailureOrigin.Upstream, "Too Many Requests")
            .AddPattern(FailureOrigin.Upstream, "HttpRequestException")
            .AddPattern(FailureOrigin.Upstream, "TaskCanceledException")
            .AddPattern(FailureOrigin.Upstream, "TimeoutException")
            .AddPattern(FailureOrigin.Upstream, "timed out")
            .AddPattern(FailureOrigin.Upstream, "connection refused");

    /// <summary>
    /// Returns a builder pre-loaded with upstream failure patterns for the
    /// Google Gemini / Vertex AI API.
    /// </summary>
    public static FailureClassifierBuilder Google() =>
        new FailureClassifierBuilder()
            .AddPattern(FailureOrigin.Upstream, "429")
            .AddPattern(FailureOrigin.Upstream, "503")
            .AddPattern(FailureOrigin.Upstream, "RESOURCE_EXHAUSTED")
            .AddPattern(FailureOrigin.Upstream, "UNAVAILABLE")
            .AddPattern(FailureOrigin.Upstream, "quota")
            .AddPattern(FailureOrigin.Upstream, "Too Many Requests")
            .AddPattern(FailureOrigin.Upstream, "HttpRequestException")
            .AddPattern(FailureOrigin.Upstream, "TaskCanceledException")
            .AddPattern(FailureOrigin.Upstream, "TimeoutException")
            .AddPattern(FailureOrigin.Upstream, "timed out")
            .AddPattern(FailureOrigin.Upstream, "connection refused");
}
