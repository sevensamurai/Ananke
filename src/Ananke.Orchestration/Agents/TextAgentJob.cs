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

/// <summary>
/// An agent job that returns the model's plain-text response without JSON structured output.
/// Use <see cref="AgentJobFactory"/> to create an instance
/// via a fluent builder.
/// </summary>
/// <remarks>
/// This is the simplified counterpart to <see cref="AgentJob{TState, TResponse}"/>:
/// no <c>TResponse</c> type parameter, no JSON schema enforcement, no deserialization.
/// Ideal for summarisation, classification, chat, and other tasks where the model
/// output is consumed as a string.
/// </remarks>
/// <typeparam name="TState">The workflow state type.</typeparam>
public sealed class TextAgentJob<TState> : IJob<TState>, IProfileAwareJob
{
    private readonly AgentJobEngine<TState, string> _engine;

    private TextAgentJob(AgentJobEngine<TState, string> engine) => _engine = engine;

    /// <inheritdoc />
    public string Name => _engine.Name;

    bool IProfileAwareJob.HasProfileAwareModel => _engine.HasProfileAwareModel;

    /// <inheritdoc />
    public Task<TState> ExecuteAsync(TState state, CancellationToken ct = default) =>
        _engine.ExecuteAsync(state, ct);

    /// <summary>Fluent builder for <see cref="TextAgentJob{TState}"/>.</summary>
    public sealed class Builder
    {
        private readonly string _name;
        private readonly IAgentModel _model;
        private string? _systemPrompt;
        private Func<TState, string>? _promptBuilder;
        private Func<TState, string, TState>? _mapResult;
        private Action<TState, string>? _onResponse;
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

        internal Builder(string name, IAgentModel model)
        {
            _name = name;
            _model = model;
        }

        internal Builder(string name, IModelRouter router)
        {
            _name = name;
            _model = new RoutedAgentModel(router);
        }

        /// <summary>Sets the system prompt sent with every LLM call.</summary>
        public Builder WithSystemPrompt(string systemPrompt)
        {
            _systemPrompt = systemPrompt;
            return this;
        }

        /// <summary>Builds the user prompt from the current workflow state.</summary>
        public Builder WithPrompt(Func<TState, string> promptBuilder)
        {
            _promptBuilder = promptBuilder;
            return this;
        }

        /// <summary>Registers a <see cref="ToolKit"/> for the agent's tool-calling loop.</summary>
        public Builder WithTools(ToolKit toolKit)
        {
            _toolKit = toolKit;
            return this;
        }

        /// <summary>Sets the maximum number of tool-calling rounds before forcing a final answer.</summary>
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

        /// <summary>Sets a token budget for the tool-calling loop's context window.</summary>
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

        /// <summary>Enables conversation memory for multi-turn interactions.</summary>
        public Builder WithMemory(IConversationMemory memory, Func<TState, string> sessionId)
        {
            ArgumentNullException.ThrowIfNull(memory);
            ArgumentNullException.ThrowIfNull(sessionId);
            _memory = memory;
            _sessionIdBuilder = sessionId;
            return this;
        }

        /// <summary>Registers a <see cref="ITrajectoryObserver"/> to receive a snapshot after each run.</summary>
        public Builder WithTrajectoryObserver(ITrajectoryObserver observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            _trajectoryObserver = observer;
            return this;
        }

        /// <summary>Sets a logger factory for structured logging.</summary>
        public Builder WithLogger(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _loggerFactory = loggerFactory;
            return this;
        }

        /// <summary>Sets the context strategy applied before each LLM call.</summary>
        public Builder WithContextStrategy(IContextStrategy strategy)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            _contextStrategy = strategy;
            return this;
        }

        /// <summary>Registers a side-effect callback invoked with the LLM's text response.</summary>
        public Builder OnResponse(Action<TState, string> handler)
        {
            _onResponse = handler;
            return this;
        }

        /// <summary>
        /// Maps the model's text response back into the workflow state.
        /// The <paramref name="mapper"/> receives the current state and the LLM text.
        /// </summary>
        public Builder MapResult(Func<TState, string, TState> mapper)
        {
            _mapResult = mapper;
            return this;
        }

        /// <summary>Builds the <see cref="TextAgentJob{TState}"/>.</summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <see cref="WithPrompt"/> or <see cref="MapResult"/> was not called.
        /// </exception>
        public TextAgentJob<TState> Build()
        {
            ArgumentNullException.ThrowIfNull(_promptBuilder,
                "Prompt builder is required. Call WithPrompt().");
            ArgumentNullException.ThrowIfNull(_mapResult,
                "Result mapper is required. Call MapResult().");

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

            ILogger logger = _loggerFactory?.CreateLogger($"Ananke.Orchestration.TextAgentJob.{_name}")
                ?? NullLogger.Instance;

            var engine = new AgentJobEngine<TState, string>(
                _name, _model, _promptBuilder, _mapResult, _systemPrompt,
                tools, toolExecutors, _onResponse, _maxToolRounds, _maxRetryAttempts, _retryBaseDelay,
                _shouldRetry ?? AgentJobEngine<TState, string>.DefaultShouldRetry,
                _maxContextTokens, _contextLimitMode, _memory, _sessionIdBuilder, _contextStrategy, logger,
                hallucinationObserver, _trajectoryObserver, kitName,
                responseFormat: null,
                extractResult: text => text,
                finalCallSpanSuffix: "plain",
                coercionPrompt: null);

            return new TextAgentJob<TState>(engine);
        }
    }
}
