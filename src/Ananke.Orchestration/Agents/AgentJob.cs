using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Trajectory;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Agents;

public sealed class AgentJob<TState, TResponse> : IJob<TState>, IProfileAwareJob where TResponse : class
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Cache the generated JSON schema once per (TResponse, TState) pair to avoid
    // redundant reflection work on every structured agent call.
    private static readonly string CachedResponseSchema = JsonSchemaGenerator.Generate<TResponse>();

    private readonly AgentJobEngine<TState, TResponse> _engine;

    private AgentJob(AgentJobEngine<TState, TResponse> engine) => _engine = engine;

    public string Name => _engine.Name;

    bool IProfileAwareJob.HasProfileAwareModel => _engine.HasProfileAwareModel;

    public Task<TState> ExecuteAsync(TState state, CancellationToken ct = default) =>
        _engine.ExecuteAsync(state, ct);

    /// <summary>Fluent builder for <see cref="AgentJob{TState, TResponse}"/>.</summary>
    /// <remarks>
    /// <b>Thread safety:</b> <c>Builder</c> is not thread-safe.
    /// Configure and call <see cref="Build"/> on a single thread.
    /// </remarks>
    public sealed class Builder
    {
        private readonly string _name;
        private readonly IAgentModel _model;
        private string? _systemPrompt;
        private Func<TState, string>? _promptBuilder;
        private Func<TState, TResponse, TState>? _mapResult;
        private Action<TState, TResponse>? _onResponse;
        private ToolKit? _toolKit;
        private int _maxToolRounds = 3;
        private int _maxRetryAttempts = 3;
        private TimeSpan _retryBaseDelay = TimeSpan.FromSeconds(1);
        private Func<Exception, bool>? _shouldRetry;
        private int? _maxContextTokens;
        private ContextLimitMode _contextLimitMode = ContextLimitMode.PostCompaction;
        private IConversationMemory? _memory;
        private Func<TState, string>? _sessionIdBuilder;
        private IContextStrategy? _contextStrategy;
        private ILoggerFactory? _loggerFactory;
        private ITrajectoryObserver? _trajectoryObserver;

        public Builder(string name, IAgentModel model)
        {
            _name = name;
            _model = model;
        }

        public Builder(string name, IModelRouter router)
        {
            _name = name;
            _model = new RoutedAgentModel(router);
        }

        public Builder WithSystemPrompt(string systemPrompt)
        {
            _systemPrompt = systemPrompt;
            return this;
        }

        public Builder WithPrompt(Func<TState, string> promptBuilder)
        {
            _promptBuilder = promptBuilder;
            return this;
        }

        public Builder WithTools(ToolKit toolKit)
        {
            _toolKit = toolKit;
            return this;
        }

        public Builder WithMaxToolRounds(int max)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
            _maxToolRounds = max;
            return this;
        }

        /// <summary>
        /// Configures retry behaviour for transient LLM failures.
        /// </summary>
        /// <param name="maxAttempts">Maximum number of call attempts (including the first). Default 3.</param>
        /// <param name="baseDelay">Initial delay between retries (exponential backoff). Default 1 second.</param>
        /// <param name="shouldRetry">
        /// Predicate deciding whether an exception is worth retrying. Defaults to
        /// <see cref="ResilientAgentModel.IsRateLimitException"/> or a <see cref="TimeoutException"/>.
        /// A non-retryable exception is rethrown immediately — no backoff, no attempt burned.
        /// <see cref="GuardrailException"/> is never retried regardless of this predicate.
        /// </param>
        public Builder WithRetry(int maxAttempts = 3, TimeSpan? baseDelay = null, Func<Exception, bool>? shouldRetry = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
            _maxRetryAttempts = maxAttempts;
            _retryBaseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
            _shouldRetry = shouldRetry;
            return this;
        }

        /// <summary>
        /// Sets a token budget for the tool-calling loop's context window.
        /// </summary>
        /// <param name="maxTokens">Estimated-token ceiling. Exceeding it throws.</param>
        /// <param name="mode">
        /// Whether the ceiling is measured before or after <see cref="WithContextStrategy"/>
        /// compaction. Defaults to <see cref="ContextLimitMode.PostCompaction"/> — the limit
        /// applies to what is actually sent. Only meaningful alongside a context strategy.
        /// </param>
        public Builder WithContextLimit(
            int maxTokens,
            ContextLimitMode mode = ContextLimitMode.PostCompaction)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 1);
            _maxContextTokens = maxTokens;
            _contextLimitMode = mode;
            return this;
        }

        /// <summary>
        /// Enables conversation memory for multi-turn agent interactions.
        /// Prior conversation history is loaded before each call and new messages
        /// are persisted after execution.
        /// </summary>
        public Builder WithMemory(IConversationMemory memory, Func<TState, string> sessionId)
        {
            ArgumentNullException.ThrowIfNull(memory);
            ArgumentNullException.ThrowIfNull(sessionId);
            _memory = memory;
            _sessionIdBuilder = sessionId;
            return this;
        }

        public Builder WithLogger(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _loggerFactory = loggerFactory;
            return this;
        }

        /// <summary>
        /// Sets the context strategy applied before each LLM call.
        /// </summary>
        public Builder WithContextStrategy(IContextStrategy strategy)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            _contextStrategy = strategy;
            return this;
        }

        /// <summary>Registers an <see cref="ITrajectoryObserver"/> to receive a snapshot after each run.</summary>
        public Builder WithTrajectoryObserver(ITrajectoryObserver observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            _trajectoryObserver = observer;
            return this;
        }

        public Builder OnResponse(Action<TState, TResponse> handler)
        {
            _onResponse = handler;
            return this;
        }

        public Builder MapResult(Func<TState, TResponse, TState> mapper)
        {
            _mapResult = mapper;
            return this;
        }

        public AgentJob<TState, TResponse> Build()
        {
            ArgumentNullException.ThrowIfNull(_promptBuilder, "Prompt builder is required. Call WithPrompt().");
            ArgumentNullException.ThrowIfNull(_mapResult, "Result mapper is required. Call MapResult().");

            IReadOnlyList<AgentTool>? tools = null;
            IReadOnlyDictionary<string, ToolDefinition>? toolExecutors = null;
            IHallucinationObserver? hallucinationObserver = null;
            string? kitName = null;

            if (_toolKit is not null)
            {
                tools = _toolKit.Tools.Values.Select(t =>
                    new AgentTool(t.Name, t.Description, t.ParametersJsonSchema)).ToList();
                toolExecutors = _toolKit.Tools;
                hallucinationObserver = _toolKit.HallucinationObserver;
                kitName = _toolKit.Name;
            }

            ILogger logger = _loggerFactory?.CreateLogger($"Ananke.Orchestration.AgentJob.{_name}")
                ?? NullLogger.Instance;

            var responseFormat = new AgentResponseFormat(typeof(TResponse).Name, CachedResponseSchema);

            var engine = new AgentJobEngine<TState, TResponse>(
                _name, _model, _promptBuilder, _mapResult, _systemPrompt,
                tools, toolExecutors, _onResponse, _maxToolRounds, _maxRetryAttempts, _retryBaseDelay,
                _shouldRetry ?? AgentJobEngine<TState, TResponse>.DefaultShouldRetry,
                _maxContextTokens, _contextLimitMode, _memory, _sessionIdBuilder, _contextStrategy, logger,
                hallucinationObserver, _trajectoryObserver, kitName,
                responseFormat: responseFormat,
                extractResult: text => JsonSerializer.Deserialize<TResponse>(text, JsonOptions)
                    ?? throw new InvalidOperationException(
                        $"[{_name}] Failed to deserialize response to {typeof(TResponse).Name}."),
                finalCallSpanSuffix: "structured",
                coercionPrompt: $"Based on everything above, provide your final response as JSON matching the {typeof(TResponse).Name} schema.");

            return new AgentJob<TState, TResponse>(engine);
        }
    }
}

public static class AgentJobFactory
{
    /// <summary>
    /// Creates a builder for a structured agent job that deserializes the model's response
    /// to <typeparamref name="TResponse"/> via JSON schema enforcement.
    /// </summary>
    public static AgentJob<TState, TResponse>.Builder Create<TState, TResponse>(
        string name, IAgentModel model) where TResponse : class
        => new(name, model);

    /// <summary>
    /// Creates a builder for a structured agent job using a <see cref="IModelRouter"/>
    /// for capability-based model selection.
    /// </summary>
    public static AgentJob<TState, TResponse>.Builder Create<TState, TResponse>(
        string name, IModelRouter router) where TResponse : class
        => new(name, router);

    /// <summary>
    /// Creates a builder for a plain-text agent job — the simplest way to call an LLM
    /// within a workflow. No JSON schema, no <c>TResponse</c> type parameter.
    /// The model's text response is passed directly to <see cref="TextAgentJob{TState}.Builder.MapResult"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var agent = AgentJobFactory.Create&lt;MyState&gt;("summarize", model)
    ///     .WithSystemPrompt("You summarize text concisely.")
    ///     .WithPrompt(s =&gt; s.Input)
    ///     .MapResult((s, text) =&gt; s with { Summary = text })
    ///     .Build();
    /// </code>
    /// </example>
    public static TextAgentJob<TState>.Builder Create<TState>(
        string name, IAgentModel model)
        => new(name, model);

    /// <summary>
    /// Creates a builder for a plain-text agent job using a <see cref="IModelRouter"/>
    /// for capability-based model selection.
    /// </summary>
    public static TextAgentJob<TState>.Builder Create<TState>(
        string name, IModelRouter router)
        => new(name, router);
}
