using System.Text.RegularExpressions;

using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Middleware;

/// <summary>
/// Middleware that rejects model responses matching configurable deny rules.
/// When a response is blocked, a <see cref="GuardrailException"/> is thrown.
/// </summary>
/// <remarks>
/// <para>
/// Guards are evaluated against the response text after the model completes.
/// If any deny rule matches, the response is rejected and a
/// <see cref="GuardrailException"/> is thrown containing the rule name.
/// </para>
/// <para>
/// Two configuration modes:
/// </para>
/// <list type="bullet">
///   <item><b>Regex patterns:</b> Compiled regexes matched against response text.</item>
///   <item><b>Delegate predicates:</b> Arbitrary functions for complex validation logic.</item>
/// </list>
/// <para>
/// Register via <see cref="MiddlewareAgentModel.Wrap(IStreamingAgentModel, IAgentModelMiddleware[])"/>:
/// </para>
/// <code>
/// var guardrail = new GuardrailAgentModelMiddleware.Builder()
///     .DenyPattern("pii-ssn", @"\b\d{3}-\d{2}-\d{4}\b")
///     .DenyWhen("empty-response", (response, _) => string.IsNullOrWhiteSpace(response.Text))
///     .Build();
///
/// var model = MiddlewareAgentModel.Wrap(innerModel, guardrail);
/// </code>
/// <para>
/// <b>Streaming:</b> for <see cref="MiddlewareAgentModel.GenerateStreamAsync"/>, this middleware
/// only runs once the response is fully assembled — under the default
/// <see cref="StreamingMode.PassThrough"/>, that means a blocked response's content has already
/// streamed to the consumer chunk-by-chunk by the time the <see cref="GuardrailException"/> is
/// thrown; the exception stops the final clean result from being delivered, but can't un-send
/// what already went out. Whenever a deny rule here carries PII or security semantics (like the
/// <c>pii-ssn</c> example above), wrap with <see cref="StreamingMode.Buffered"/> instead —
/// <c>MiddlewareAgentModel.Wrap(innerModel, StreamingMode.Buffered, guardrail)</c> — so the
/// guardrail runs before any chunk reaches the consumer.
/// </para>
/// </remarks>
public sealed class GuardrailAgentModelMiddleware : IAgentModelMiddleware
{
    private readonly IReadOnlyList<GuardrailRule> _rules;

    private GuardrailAgentModelMiddleware(IReadOnlyList<GuardrailRule> rules) =>
        _rules = rules;

    /// <inheritdoc />
    public Task<AgentRequest> OnBeforeGenerateAsync(AgentRequest request, CancellationToken ct = default) =>
        Task.FromResult(request);

    /// <inheritdoc />
    public Task<AgentResponse> OnAfterGenerateAsync(
        AgentResponse response, AgentRequest request, CancellationToken ct = default)
    {
        foreach (var rule in _rules)
        {
            if (rule.IsViolated(response, request))
                throw new GuardrailException(rule.Name, response);
        }

        return Task.FromResult(response);
    }

    /// <summary>
    /// Fluent builder for <see cref="GuardrailAgentModelMiddleware"/>.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<GuardrailRule> _rules = [];

        /// <summary>
        /// Adds a regex deny pattern. If the response text matches, the response is rejected.
        /// </summary>
        /// <param name="name">Human-readable rule name included in the exception message.</param>
        /// <param name="pattern">Regex pattern to match against <see cref="AgentResponse.Text"/>.</param>
        /// <param name="options">Regex options. Defaults to <see cref="RegexOptions.None"/>.</param>
        public Builder DenyPattern(string name, string pattern, RegexOptions options = RegexOptions.None)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
            _rules.Add(new RegexRule(name, new Regex(pattern, options | RegexOptions.Compiled)));
            return this;
        }

        /// <summary>
        /// Adds a delegate-based deny rule. If the predicate returns <c>true</c>, the response is rejected.
        /// </summary>
        /// <param name="name">Human-readable rule name included in the exception message.</param>
        /// <param name="predicate">
        /// Predicate receiving the response and request. Return <c>true</c> to reject the response.
        /// </param>
        public Builder DenyWhen(string name, Func<AgentResponse, AgentRequest, bool> predicate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(predicate);
            _rules.Add(new DelegateRule(name, predicate));
            return this;
        }

        /// <summary>
        /// Builds the guardrail middleware with all configured rules.
        /// </summary>
        /// <exception cref="InvalidOperationException">No rules were configured.</exception>
        public GuardrailAgentModelMiddleware Build()
        {
            if (_rules.Count == 0)
                throw new InvalidOperationException("GuardrailAgentModelMiddleware requires at least one rule.");

            return new GuardrailAgentModelMiddleware([.. _rules]);
        }
    }

    // ── Rule types ──────────────────────────────────────────────

    private abstract record GuardrailRule(string Name)
    {
        public abstract bool IsViolated(AgentResponse response, AgentRequest request);
    }

    private sealed record RegexRule(string Name, Regex Pattern) : GuardrailRule(Name)
    {
        public override bool IsViolated(AgentResponse response, AgentRequest request) =>
            response.Text is not null && Pattern.IsMatch(response.Text);
    }

    private sealed record DelegateRule(
        string Name, Func<AgentResponse, AgentRequest, bool> Predicate) : GuardrailRule(Name)
    {
        public override bool IsViolated(AgentResponse response, AgentRequest request) =>
            Predicate(response, request);
    }
}

/// <summary>
/// Thrown when a <see cref="GuardrailAgentModelMiddleware"/> rule rejects a model response.
/// </summary>
public sealed class GuardrailException : Exception
{
    /// <summary>Name of the guardrail rule that was violated.</summary>
    public string RuleName { get; }

    /// <summary>The response that was blocked.</summary>
    public AgentResponse BlockedResponse { get; }

    /// <summary>
    /// Creates a new guardrail exception.
    /// </summary>
    public GuardrailException(string ruleName, AgentResponse blockedResponse)
        : base($"Response blocked by guardrail rule '{ruleName}'.")
    {
        RuleName = ruleName;
        BlockedResponse = blockedResponse;
    }
}
