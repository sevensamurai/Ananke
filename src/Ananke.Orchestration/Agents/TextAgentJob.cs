using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Abstractions.Tracing;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;

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
public sealed class TextAgentJob<TState> : IJob<TState>
{
    private readonly IAgentModel _model;
    private readonly Func<TState, string> _promptBuilder;
    private readonly Func<TState, string, TState> _mapResult;
    private readonly string? _systemPrompt;
    private readonly IReadOnlyList<AgentTool>? _tools;
    private readonly IReadOnlyDictionary<string, ToolDefinition>? _toolExecutors;
    private readonly Action<TState, string>? _onResponse;
    private readonly int _maxToolRounds;
    private readonly ResiliencePipeline<AgentResponse> _retryPipeline;
    private readonly int? _maxContextTokens;
    private readonly IConversationMemory? _memory;
    private readonly Func<TState, string>? _sessionIdBuilder;
    private readonly IContextStrategy? _contextStrategy;
    private readonly ILogger _logger;

    private TextAgentJob(
        string name,
        IAgentModel model,
        Func<TState, string> promptBuilder,
        Func<TState, string, TState> mapResult,
        string? systemPrompt,
        IReadOnlyList<AgentTool>? tools,
        IReadOnlyDictionary<string, ToolDefinition>? toolExecutors,
        Action<TState, string>? onResponse,
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

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct = default)
    {
        var userPrompt = _promptBuilder(state);
        var messages = new List<AgentMessage>();

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

        if (_contextStrategy is not null)
        {
            var compacted = await _contextStrategy.ApplyAsync(messages, _systemPrompt, ct);
            if (!ReferenceEquals(compacted, messages))
            {
                historyCount = 0;
                messages = [.. compacted];
            }
        }

        string text = _tools is not null
            ? await ExecuteWithToolsAsync(messages, ct)
            : await ExecutePlainAsync(messages, ct);

        if (_memory is not null && sessionId is not null && historyCount < messages.Count)
        {
            var newMessages = messages.GetRange(historyCount, messages.Count - historyCount);
            await _memory.AddAsync(sessionId, newMessages, ct);
        }

        _onResponse?.Invoke(state, text);
        return _mapResult(state, text);
    }

    private async Task<string> ExecutePlainAsync(List<AgentMessage> messages, CancellationToken ct)
    {
        var parentSpan = WorkflowTraceContext.Value?.CurrentSpan;
        await using var llmSpan = parentSpan?.StartSpan($"{Name}/plain", SpanKind.LlmCall);

        var request = new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = messages,
            Metadata = BuildMetadata(),
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? true
        };

        var result = await GenerateWithRetryAsync(request, ct);
        var text = result.Text
            ?? throw new InvalidOperationException($"[{Name}] LLM returned empty response.");

        llmSpan?.SetAttribute("response_length", text.Length.ToString());
        messages.Add(AgentMessage.Assistant(text));
        return text;
    }

    private async Task<string> ExecuteWithToolsAsync(List<AgentMessage> messages, CancellationToken ct)
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
            messages.Add(AgentMessage.Assistant(result.Text ?? string.Empty, result.ToolCalls));

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

            IReadOnlyList<AgentMessage> requestMessages = messages;
            if (_contextStrategy is not null)
                requestMessages = await _contextStrategy.ApplyAsync(messages, _systemPrompt, ct);

            request = request with { Messages = requestMessages };
            result = await GenerateWithRetryAsync(request, ct);
        }

        llmSpan?.SetAttribute("tool_rounds", toolRound.ToString());

        var text = result.Text
            ?? throw new InvalidOperationException($"[{Name}] LLM returned empty response after tool loop.");

        messages.Add(AgentMessage.Assistant(text));
        return text;
    }

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

    private static IReadOnlyDictionary<string, object?> ParseToolArgs(string arguments)
    {
        var dict = new Dictionary<string, object?>();
        using var doc = System.Text.Json.JsonDocument.Parse(arguments);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    private static int EstimateTokens(AgentRequest request) =>
        ((request.SystemPrompt?.Length ?? 0) +
         request.Messages.Sum(m => (m.Content?.Length ?? 0) +
            (m.ToolCalls?.Sum(tc => tc.Arguments.Length + tc.FunctionName.Length) ?? 0)) +
         (request.Tools?.Sum(t => t.Name.Length + t.Description.Length + t.ParametersJsonSchema.Length) ?? 0)) / 4;

    private async Task<AgentResponse> GenerateWithRetryAsync(AgentRequest request, CancellationToken ct)
    {
        var response = await _retryPipeline.ExecuteAsync(
            async token => await _model.GenerateAsync(request, token), ct);
        TokenUsageCapture.Accumulate(response);
        return response;
    }

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
        private int? _maxContextTokens;
        private IConversationMemory? _memory;
        private Func<TState, string>? _sessionIdBuilder;
        private IContextStrategy? _contextStrategy;
        private ILoggerFactory? _loggerFactory;

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

        /// <summary>Configures retry behaviour for transient LLM failures.</summary>
        public Builder WithRetry(int maxAttempts = 3, TimeSpan? baseDelay = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
            _maxRetryAttempts = maxAttempts;
            _retryBaseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
            return this;
        }

        /// <summary>Sets a token budget for context window management.</summary>
        public Builder WithContextLimit(int maxTokens)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 1);
            _maxContextTokens = maxTokens;
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

            if (_toolKit is not null)
            {
                tools = _toolKit.Tools.Values.Select(t =>
                    new AgentTool(t.Name, t.Description, t.ParametersJsonSchema)).ToList();
                toolExecutors = _toolKit.Tools;
            }

            ILogger logger = _loggerFactory?.CreateLogger($"Ananke.Orchestration.TextAgentJob.{_name}")
                ?? NullLogger.Instance;

            return new TextAgentJob<TState>(
                _name, _model, _promptBuilder, _mapResult, _systemPrompt,
                tools, toolExecutors, _onResponse, _maxToolRounds, _maxRetryAttempts, _retryBaseDelay,
                _maxContextTokens, _memory, _sessionIdBuilder, _contextStrategy, logger);
        }
    }
}
