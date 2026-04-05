using System.Text.Json;
using Ananke.Abstractions.Tracing;
using Ananke.Orchestration.Jobs;
using Ananke.Abstractions.Memory;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;

using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

public sealed class AgentJob<TState, TResponse> : IJob<TState> where TResponse : class
{
    private readonly IAgentModel _model;
    private readonly Func<TState, string> _promptBuilder;
    private readonly Func<TState, TResponse, TState> _mapResult;
    private readonly string? _systemPrompt;
    private readonly IReadOnlyList<AgentTool>? _tools;
    private readonly IReadOnlyDictionary<string, ToolDefinition>? _toolExecutors;
    private readonly Action<TState, TResponse>? _onResponse;
    private readonly int _maxToolRounds;
    private readonly ResiliencePipeline<AgentResponse> _retryPipeline;
    private readonly int? _maxContextTokens;
    private readonly IConversationMemory? _memory;
    private readonly Func<TState, string>? _sessionIdBuilder;
    private readonly IContextStrategy? _contextStrategy;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private AgentJob(
        string name,
        IAgentModel model,
        Func<TState, string> promptBuilder,
        Func<TState, TResponse, TState> mapResult,
        string? systemPrompt,
        IReadOnlyList<AgentTool>? tools,
        IReadOnlyDictionary<string, ToolDefinition>? toolExecutors,
        Action<TState, TResponse>? onResponse,
        int maxToolRounds,
        int maxRetryAttempts,
        TimeSpan retryBaseDelay,
        int? maxContextTokens,
        IConversationMemory? memory,
        Func<TState, string>? sessionIdBuilder,
        IContextStrategy? contextStrategy,
        ILogger logger)
    {
        Name = name;
        _model = model;
        _promptBuilder = promptBuilder;
        _mapResult = mapResult;
        _systemPrompt = systemPrompt;
        _tools = tools;
        _toolExecutors = toolExecutors;
        _onResponse = onResponse;
        _maxToolRounds = maxToolRounds;
        _retryPipeline = new ResiliencePipelineBuilder<AgentResponse>()
            .AddRetry(new RetryStrategyOptions<AgentResponse>
            {
                MaxRetryAttempts = maxRetryAttempts - 1,
                BackoffType = DelayBackoffType.Exponential,
                Delay = retryBaseDelay,
                UseJitter = false,
                ShouldHandle = new PredicateBuilder<AgentResponse>()
                    .Handle<Exception>(ex => ex is not OperationCanceledException),
                OnRetry = args =>
                {
                    logger.LogWarning(args.Outcome.Exception,
                        "[{AgentName}] LLM call failed (attempt {Attempt}/{MaxAttempts}), retrying in {DelayMs}ms",
                        name, args.AttemptNumber + 1, maxRetryAttempts, (int)args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
        _maxContextTokens = maxContextTokens;
        _memory = memory;
        _sessionIdBuilder = sessionIdBuilder;
        _contextStrategy = contextStrategy;
        _logger = logger;
    }

    public string Name { get; }

    private Dictionary<string, string> BuildMetadata()
    {
        var metadata = new Dictionary<string, string> { ["agent"] = Name };

        if (WorkflowTraceContext.Value is { } trace)
        {
            metadata["workflow"] = trace.WorkflowName;
            metadata["execution_id"] = trace.ExecutionId;
            if (trace.CurrentJob is not null)
                metadata["job"] = trace.CurrentJob;
        }

        return metadata;
    }

    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct = default)
    {
        var userPrompt = _promptBuilder(state);
        var messages = new List<AgentMessage>();

        // Load prior conversation history from memory
        string? sessionId = null;
        var historyCount = 0;
        if (_memory is not null && _sessionIdBuilder is not null)
        {
            sessionId = _sessionIdBuilder(state);
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var history = await _memory.GetHistoryAsync(sessionId, ct);
                messages.AddRange(history);
                historyCount = history.Count;
            }
        }

        messages.Add(AgentMessage.User(userPrompt));

        // Apply context strategy to compact messages before the first LLM call
        if (_contextStrategy is not null)
        {
            var compacted = await _contextStrategy.ApplyAsync(messages, _systemPrompt, ct);
            if (!ReferenceEquals(compacted, messages))
            {
                historyCount = 0; // compacted list may have dropped history messages
                messages = [.. compacted];
            }
        }

        TResponse response = _tools is not null
            ? await ExecuteWithToolsAsync(messages, ct)
            : await ExecuteStructuredAsync(messages, ct);

        // Persist only new messages (skip the loaded history)
        if (_memory is not null && sessionId is not null && historyCount < messages.Count)
        {
            var newMessages = messages.GetRange(historyCount, messages.Count - historyCount);
            await _memory.AddAsync(sessionId, newMessages, ct);
        }

        _onResponse?.Invoke(state, response);
        return _mapResult(state, response);
    }

    private async Task<TResponse> ExecuteStructuredAsync(
        List<AgentMessage> messages,
        CancellationToken ct)
    {
        var parentSpan = WorkflowTraceContext.Value?.CurrentSpan;
        await using var llmSpan = parentSpan?.StartSpan($"{Name}/structured", SpanKind.LlmCall);

        var request = new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = messages,
            ResponseFormat = new AgentResponseFormat(
                typeof(TResponse).Name,
                JsonSchemaGenerator.Generate<TResponse>()),
            Metadata = BuildMetadata(),
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? true
        };

        var result = await GenerateWithRetryAsync(request, ct);
        var text = result.Text
            ?? throw new InvalidOperationException($"[{Name}] LLM returned empty response.");

        llmSpan?.SetAttribute("response_length", text.Length.ToString());

        return JsonSerializer.Deserialize<TResponse>(text, JsonOptions)
            ?? throw new InvalidOperationException(
                $"[{Name}] Failed to deserialize response to {typeof(TResponse).Name}.");
    }

    private async Task<TResponse> ExecuteWithToolsAsync(
        List<AgentMessage> messages,
        CancellationToken ct)
    {
        var parentSpan = WorkflowTraceContext.Value?.CurrentSpan;
        await using var llmSpan = parentSpan?.StartSpan($"{Name}/tool-loop", SpanKind.LlmCall);
        var toolRound = 0;

        var request = new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = messages,
            Tools = _tools,
            Metadata = BuildMetadata(),
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? true
        };

        var result = await GenerateWithRetryAsync(request, ct);

        while (result.RequiresAction)
        {
            if (toolRound >= _maxToolRounds)
                throw new InvalidOperationException(
                    $"[{Name}] Tool-calling loop exceeded the maximum of {_maxToolRounds} rounds. " +
                    $"Adjust the system prompt or call WithMaxToolRounds() to increase the limit.");

            toolRound++;
            messages.Add(AgentMessage.Assistant(
                result.Text ?? string.Empty, result.ToolCalls));

            var hasNonRetryable = false;

            foreach (var call in result.ToolCalls!)
            {
                await using var toolSpan = llmSpan?.StartSpan($"tool:{call.FunctionName}", SpanKind.ToolCall);
                toolSpan?.SetAttribute("tool_round", toolRound.ToString());

                var args = ParseToolArgs(call.Arguments);
                var toolResult = _toolExecutors!.TryGetValue(call.FunctionName, out var executor)
                    ? await executor.ExecuteAsync(args, ct)
                    : ToolResult.Error($"Unknown tool: {call.FunctionName}");

                if (toolResult.IsError)
                {
                    toolSpan?.SetAttribute("tool.error", "true");
                    _logger.LogWarning(
                        "[{AgentName}] Tool '{Tool}' returned error: {Error}",
                        Name, call.FunctionName, toolResult.Value);

                    if (!toolResult.IsRetryable)
                    {
                        toolSpan?.SetAttribute("tool.retryable", "false");
                        hasNonRetryable = true;
                    }
                }

                toolSpan?.SetAttribute("output_length", toolResult.Value.Length.ToString());
                messages.Add(AgentMessage.ToolResult(call.Id, toolResult.Value));
            }

            if (hasNonRetryable)
            {
                messages.Add(AgentMessage.User(
                    "One or more tools returned a permanent error that will not succeed on retry. " +
                    "Do not call those tools again. Proceed with your best answer using any information you already have."));
            }

            if (_maxContextTokens.HasValue)
            {
                var estimated = EstimateTokens(request);
                if (estimated > _maxContextTokens.Value)
                    throw new InvalidOperationException(
                        $"[{Name}] Estimated context ({estimated:N0} tokens) exceeds the configured limit " +
                        $"of {_maxContextTokens.Value:N0} tokens after tool round {toolRound}.");
                if (estimated > (int)(_maxContextTokens.Value * 0.8))
                    _logger.LogWarning(
                        "[{AgentName}] Context approaching limit: ~{Estimated:N0}/{Max:N0} estimated tokens (round {Round})",
                        Name, estimated, _maxContextTokens.Value, toolRound);
            }

            // Apply context strategy before re-requesting in the tool loop
            IReadOnlyList<AgentMessage> requestMessages = messages;
            if (_contextStrategy is not null)
            {
                requestMessages = await _contextStrategy.ApplyAsync(messages, _systemPrompt, ct);
            }

            request = request with { Messages = requestMessages };
            result = await GenerateWithRetryAsync(request, ct);
        }

        llmSpan?.SetAttribute("tool_rounds", toolRound.ToString());

        messages.Add(AgentMessage.Assistant(result.Text ?? string.Empty));
        messages.Add(AgentMessage.User(
            $"Based on everything above, provide your final response as JSON matching the {typeof(TResponse).Name} schema."));

        return await ExecuteStructuredAsync(messages, ct);
    }

    private static IReadOnlyDictionary<string, object?> ParseToolArgs(string arguments)
    {
        var dict = new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(arguments);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.Clone();
        }
        return dict;
    }

    private static int EstimateTokens(AgentRequest request) =>
        ((request.SystemPrompt?.Length ?? 0) +
         request.Messages.Sum(m => (m.Content?.Length ?? 0) + (m.ToolCalls?.Sum(tc => tc.Arguments.Length + tc.FunctionName.Length) ?? 0)) +
         (request.Tools?.Sum(t => t.Name.Length + t.Description.Length + t.ParametersJsonSchema.Length) ?? 0)) / 4;

    private async Task<AgentResponse> GenerateWithRetryAsync(AgentRequest request, CancellationToken ct)
    {
        var response = await _retryPipeline.ExecuteAsync(
            async token => await _model.GenerateAsync(request, token), ct);
        TokenUsageCapture.Accumulate(response);
        return response;
    }

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
        private int? _maxContextTokens;
        private IConversationMemory? _memory;
        private Func<TState, string>? _sessionIdBuilder;
        private IContextStrategy? _contextStrategy;
        private ILoggerFactory? _loggerFactory;

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

        public Builder WithRetry(int maxAttempts = 3, TimeSpan? baseDelay = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
            _maxRetryAttempts = maxAttempts;
            _retryBaseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
            return this;
        }

        public Builder WithContextLimit(int maxTokens)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 1);
            _maxContextTokens = maxTokens;
            return this;
        }

        /// <summary>
        /// Enables conversation memory for multi-turn agent interactions.
        /// Prior conversation history is loaded before each call and new messages
        /// are persisted after execution.
        /// </summary>
        /// <param name="memory">The conversation memory store.</param>
        /// <param name="sessionId">
        /// Extracts the session identifier from the workflow state. Each unique session ID
        /// gets isolated conversation history.
        /// </param>
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
        /// When set, the message history is passed through the strategy before
        /// building the <see cref="AgentRequest"/>. Use with
        /// <see cref="WithContextLimit"/> to set the token budget, or configure
        /// the budget directly on the strategy.
        /// </summary>
        /// <param name="strategy">The context compaction strategy.</param>
        public Builder WithContextStrategy(IContextStrategy strategy)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            _contextStrategy = strategy;
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

            if (_toolKit is not null)
            {
                tools = _toolKit.Tools.Values.Select(t =>
                    new AgentTool(t.Name, t.Description, t.ParametersJsonSchema)).ToList();
                toolExecutors = _toolKit.Tools;
            }

            ILogger logger = _loggerFactory?.CreateLogger($"Ananke.Orchestration.AgentJob.{_name}")
                ?? NullLogger.Instance;

            return new AgentJob<TState, TResponse>(
                _name, _model, _promptBuilder, _mapResult, _systemPrompt,
                tools, toolExecutors, _onResponse, _maxToolRounds, _maxRetryAttempts, _retryBaseDelay,
                _maxContextTokens, _memory, _sessionIdBuilder, _contextStrategy, logger);
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
