using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tracing;
using Ananke.Abstractions.Trajectory;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Agents.Trajectory;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tracing;
using Microsoft.Extensions.Logging;

using Ananke.Orchestration.Usage;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Shared execution engine behind <see cref="AgentJob{TState, TResponse}"/> and
/// <see cref="TextAgentJob{TState}"/>. The two public types are deliberately kept separate —
/// structured-vs-text output is a real type-level distinction, and only the structured path
/// pays for a second, JSON-coercion model call after the tool loop — but everything else
/// (the retry loop, the tool-calling loop, context-limit enforcement, conversation memory,
/// trajectory/hallucination reporting) was, before this type existed, copy-pasted verbatim
/// between the two and had drifted three times (see the backlog's Q6/Q33/Q34 rows). This is
/// now the single place that logic lives.
/// </summary>
/// <remarks>
/// The only behaviour that varies between a structured and a text job is captured by the four
/// constructor parameters at the end of the list, rather than by subclassing:
/// <list type="bullet">
///   <item><c>responseFormat</c> — carried on the request; <see langword="null"/> for text jobs.</item>
///   <item><c>extractResult</c> — turns the model's raw text into <typeparamref name="TResult"/> (JSON deserialize, or the text itself).</item>
///   <item><c>finalCallSpanSuffix</c> — the trace span name for the single/final model call ("structured" or "plain").</item>
///   <item>
///     <c>coercionPrompt</c> — when non-<see langword="null"/>, the tool loop ends
///     with one more model call carrying <c>responseFormat</c> (the structured
///     path's JSON-coercion round); when <see langword="null"/>, the tool loop's own last
///     response is the final answer (the text path — no second call).
///   </item>
/// </list>
/// </remarks>
internal sealed class AgentJobEngine<TState, TResult>
{
    private readonly IAgentModel _model;
    private readonly Func<TState, string> _promptBuilder;
    private readonly Func<TState, TResult, TState> _mapResult;
    private readonly string? _systemPrompt;
    private readonly IReadOnlyList<AgentTool>? _tools;
    private readonly IReadOnlyDictionary<string, ToolDefinition>? _toolExecutors;
    private readonly Action<TState, TResult>? _onResponse;
    private readonly int _maxToolRounds;
    private readonly int _maxRetryAttempts;
    private readonly TimeSpan _retryBaseDelay;
    private readonly Func<Exception, bool> _shouldRetry;
    private readonly int? _maxContextTokens;
    private readonly ContextLimitMode _contextLimitMode;
    private readonly IConversationMemory? _memory;
    private readonly Func<TState, string>? _sessionIdBuilder;
    private readonly IContextStrategy? _contextStrategy;
    private readonly ILogger _logger;
    private readonly IHallucinationObserver? _hallucinationObserver;
    private readonly ITrajectoryObserver? _trajectoryObserver;
    private readonly string? _kitName;

    private readonly AgentResponseFormat? _responseFormat;
    private readonly Func<string, TResult> _extractResult;
    private readonly string _finalCallSpanSuffix;
    private readonly string? _coercionPrompt;

    public AgentJobEngine(
        string name,
        IAgentModel model,
        Func<TState, string> promptBuilder,
        Func<TState, TResult, TState> mapResult,
        string? systemPrompt,
        IReadOnlyList<AgentTool>? tools,
        IReadOnlyDictionary<string, ToolDefinition>? toolExecutors,
        Action<TState, TResult>? onResponse,
        int maxToolRounds,
        int maxRetryAttempts,
        TimeSpan retryBaseDelay,
        Func<Exception, bool> shouldRetry,
        int? maxContextTokens,
        ContextLimitMode contextLimitMode,
        IConversationMemory? memory,
        Func<TState, string>? sessionIdBuilder,
        IContextStrategy? contextStrategy,
        ILogger logger,
        IHallucinationObserver? hallucinationObserver,
        ITrajectoryObserver? trajectoryObserver,
        string? kitName,
        AgentResponseFormat? responseFormat,
        Func<string, TResult> extractResult,
        string finalCallSpanSuffix,
        string? coercionPrompt)
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
        _maxRetryAttempts = maxRetryAttempts;
        _retryBaseDelay = retryBaseDelay;
        _shouldRetry = shouldRetry;
        _maxContextTokens = maxContextTokens;
        _contextLimitMode = contextLimitMode;
        _memory = memory;
        _sessionIdBuilder = sessionIdBuilder;
        _contextStrategy = contextStrategy;
        _logger = logger;
        _hallucinationObserver = hallucinationObserver;
        _trajectoryObserver = trajectoryObserver;
        _kitName = kitName;
        _responseFormat = responseFormat;
        _extractResult = extractResult;
        _finalCallSpanSuffix = finalCallSpanSuffix;
        _coercionPrompt = coercionPrompt;
    }

    public string Name { get; }

    public bool HasProfileAwareModel => _model is RoutedAgentModel rm && rm.HasCostResolver;

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

        string? sessionId = null;
        var historyCount = 0;
        if (_memory is not null && _sessionIdBuilder is not null)
        {
            sessionId = _sessionIdBuilder(state);
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var history = await _memory.GetHistoryAsync(sessionId, ct).ConfigureAwait(false);
                messages.AddRange(history);
                historyCount = history.Count;
            }
        }

        messages.Add(AgentMessage.User(userPrompt));

        if (_contextStrategy is not null)
        {
            var compacted = await _contextStrategy.ApplyAsync(messages, _systemPrompt, ct).ConfigureAwait(false);
            if (!ReferenceEquals(compacted, messages))
            {
                historyCount = 0;
                messages = [.. compacted];
            }
        }

        var snapshotBuilder = _trajectoryObserver is not null
            ? new TrajectorySnapshotBuilder(Name, _trajectoryObserver)
            : null;

        TResult response = default!;
        var succeeded = false;
        try
        {
            response = _tools is not null
                ? await ExecuteWithToolsAsync(messages, snapshotBuilder, ct).ConfigureAwait(false)
                : await ExecuteFinalCallAsync(messages, snapshotBuilder, ct).ConfigureAwait(false);
            succeeded = true;
        }
        finally
        {
            if (snapshotBuilder is not null)
                await snapshotBuilder.CompleteAsync(succeeded, ct: ct).ConfigureAwait(false);
        }

        if (_memory is not null && sessionId is not null && historyCount < messages.Count)
        {
            var newMessages = messages.GetRange(historyCount, messages.Count - historyCount);
            await _memory.AddAsync(sessionId, newMessages, ct).ConfigureAwait(false);
        }

        _onResponse?.Invoke(state, response);
        return _mapResult(state, response);
    }

    /// <summary>
    /// The single model call used both as the whole job (no tools configured) and as the
    /// structured path's JSON-coercion round after the tool loop. Carries
    /// <see cref="_responseFormat"/> (or none, for text jobs).
    /// </summary>
    private async Task<TResult> ExecuteFinalCallAsync(
        List<AgentMessage> messages,
        TrajectorySnapshotBuilder? snapshotBuilder,
        CancellationToken ct)
    {
        var parentSpan = WorkflowTraceContext.Value?.CurrentSpan;
        await using var llmSpan = parentSpan?.StartSpan($"{Name}/{_finalCallSpanSuffix}", SpanKind.LlmCall);

        var request = new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = messages,
            ResponseFormat = _responseFormat,
            Metadata = BuildMetadata(),
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? false
        };

        var result = await GenerateWithRetryAsync(request, snapshotBuilder, llmSpan, ct).ConfigureAwait(false);
        var text = result.Text
            ?? throw new InvalidOperationException($"[{Name}] LLM returned empty response.");

        llmSpan?.SetAttribute("response_length", text.Length.ToString());

        // Record the model's own turn so IConversationMemory persists both sides of the
        // exchange. Without this, an agent using WithMemory() writes the user prompt but
        // never its own answer, and the next turn reloads a one-sided history.
        messages.Add(AgentMessage.Assistant(text));

        return _extractResult(text);
    }

    private async Task<TResult> ExecuteWithToolsAsync(
        List<AgentMessage> messages,
        TrajectorySnapshotBuilder? snapshotBuilder,
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
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? false
        };

        var result = await GenerateWithRetryAsync(request, snapshotBuilder, llmSpan, ct).ConfigureAwait(false);

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

                ToolResult toolResult;

                if (!TryParseToolArgs(call.Arguments, call.FunctionName, out var args, out var parseError))
                {
                    toolSpan?.SetAttribute("tool.malformed_arguments", "true");
                    toolResult = parseError;
                    snapshotBuilder?.RecordToolCall(hallucinated: false, faulted: true);
                }
                else if (_toolExecutors!.TryGetValue(call.FunctionName, out var executor))
                {
                    toolResult = await executor.ExecuteAsync(args, ct).ConfigureAwait(false);
                    snapshotBuilder?.RecordToolCall(hallucinated: false, faulted: toolResult.IsError);
                }
                else
                {
                    var evt = new HallucinatedToolCallEvent
                    {
                        RequestedToolName = call.FunctionName,
                        RequestedKitName = _kitName,
                        AgentId = Name,
                        EpisodeId = snapshotBuilder?.EpisodeId ?? string.Empty,
                        OccurredAt = TimeProvider.System.GetUtcNow(),
                    };

                    if (_hallucinationObserver is not null)
                        await _hallucinationObserver.ReportAsync(evt, ct).ConfigureAwait(false);

                    ToolMetrics.HallucinationReported.Add(1,
                        new KeyValuePair<string, object?>("agent_id", Name),
                        new KeyValuePair<string, object?>("kit", _kitName ?? string.Empty),
                        new KeyValuePair<string, object?>("requested_name", call.FunctionName));

                    toolSpan?.SetAttribute("tool.hallucination", "true");
                    toolSpan?.SetAttribute("tool.hallucination.requested_name", call.FunctionName);

                    toolResult = ToolResult.Error(
                        $"Unknown tool '{call.FunctionName}': this tool is not registered. Do not call it again.");
                    snapshotBuilder?.RecordToolCall(hallucinated: true, faulted: false);
                }

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

            // PreCompaction fails on the raw accumulated history without paying for the strategy,
            // which may itself issue a model call.
            if (_maxContextTokens.HasValue && _contextLimitMode == ContextLimitMode.PreCompaction)
                EnforceContextLimit(messages, toolRound);

            IReadOnlyList<AgentMessage> requestMessages = messages;
            if (_contextStrategy is not null)
                requestMessages = await _contextStrategy.ApplyAsync(messages, _systemPrompt, ct).ConfigureAwait(false);

            // PostCompaction (the default) measures what is actually about to be sent, so a
            // strategy that brings the payload back under the limit prevents the throw.
            if (_maxContextTokens.HasValue && _contextLimitMode == ContextLimitMode.PostCompaction)
                EnforceContextLimit(requestMessages, toolRound);

            request = request with { Messages = requestMessages };
            result = await GenerateWithRetryAsync(request, snapshotBuilder, llmSpan, ct).ConfigureAwait(false);
        }

        llmSpan?.SetAttribute("tool_rounds", toolRound.ToString());

        if (_coercionPrompt is not null)
        {
            // Structured path: one more call, carrying _responseFormat, to coerce the loop's
            // final answer into the schema. result.Text is intentionally not null-checked
            // here — ExecuteFinalCallAsync validates its own (the follow-up call's) response.
            messages.Add(AgentMessage.Assistant(result.Text ?? string.Empty));
            messages.Add(AgentMessage.User(_coercionPrompt));
            return await ExecuteFinalCallAsync(messages, snapshotBuilder, ct).ConfigureAwait(false);
        }

        // Text path: no follow-up call, so the loop's own last response is the final answer
        // and must be validated here.
        var text = result.Text
            ?? throw new InvalidOperationException($"[{Name}] LLM returned empty response after tool loop.");
        messages.Add(AgentMessage.Assistant(text));
        return _extractResult(text);
    }

    /// <summary>
    /// Estimates the token cost of <paramref name="measured"/> and throws once it exceeds the
    /// configured limit, warning at 80%. Which message list is passed in — the raw accumulated
    /// history or the post-compaction payload — is decided by <see cref="ContextLimitMode"/>.
    /// </summary>
    private void EnforceContextLimit(IReadOnlyList<AgentMessage> measured, int toolRound)
    {
        var preFlight = new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = measured,
            Tools = _tools,
            Metadata = BuildMetadata(),
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? false,
        };

        var estimated = EstimateTokens(preFlight);
        if (estimated > _maxContextTokens!.Value)
            throw new InvalidOperationException(
                $"[{Name}] Estimated context ({estimated:N0} tokens) exceeds the configured limit " +
                $"of {_maxContextTokens.Value:N0} tokens before tool round {toolRound + 1}.");
        if (estimated > (int)(_maxContextTokens.Value * 0.8))
            _logger.LogWarning(
                "[{AgentName}] Context approaching limit: ~{Estimated:N0}/{Max:N0} estimated tokens (pre-round {Round})",
                Name, estimated, _maxContextTokens.Value, toolRound + 1);
    }

    /// <summary>
    /// Parses tool-call arguments, tolerating malformed JSON from the model instead of throwing.
    /// A parse failure produces a retryable <see cref="ToolResult.Error(string)"/> asking the
    /// model to re-emit the call — the same self-correction shape as an unknown tool name.
    /// </summary>
    private static bool TryParseToolArgs(
        string arguments,
        string functionName,
        out IReadOnlyDictionary<string, object?> args,
        out ToolResult parseError)
    {
        try
        {
            args = ParseToolArgs(arguments);
            parseError = default!;
            return true;
        }
        catch (JsonException)
        {
            args = null!;
            parseError = ToolResult.Error(
                $"Invalid JSON arguments for '{functionName}'. Re-emit the call with valid JSON.");
            return false;
        }
    }

    private static IReadOnlyDictionary<string, object?> ParseToolArgs(string arguments)
    {
        var dict = new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(arguments);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    private static int EstimateTokens(AgentRequest request) =>
        ((request.SystemPrompt?.Length ?? 0) +
         request.Messages.Sum(m => (m.Content?.Length ?? 0) +
            (m.ToolCalls?.Sum(tc => tc.Arguments.Length + tc.FunctionName.Length) ?? 0)) +
         (request.Tools?.Sum(t => t.Name.Length + t.Description.Length + t.ParametersJsonSchema.Length) ?? 0)) / 4;

    /// <summary>
    /// Default retry predicate: HTTP 429 rate-limit errors (see
    /// <see cref="ResilientAgentModel.IsRateLimitException"/>) and <see cref="TimeoutException"/>.
    /// Everything else — 4xx auth/validation errors, <see cref="GuardrailException"/>, arbitrary
    /// application exceptions — is treated as non-retryable and rethrown on the first occurrence.
    /// </summary>
    public static bool DefaultShouldRetry(Exception ex) =>
        ResilientAgentModel.IsRateLimitException(ex) || ex is TimeoutException;

    private async Task<AgentResponse> GenerateWithRetryAsync(
        AgentRequest request,
        TrajectorySnapshotBuilder? snapshotBuilder,
        ISpan? span,
        CancellationToken ct)
    {
        Exception? lastException = null;
        var retries = 0;

        for (var attempt = 0; attempt < _maxRetryAttempts; attempt++)
        {
            try
            {
                var response = await _model.GenerateAsync(request, ct).ConfigureAwait(false);
                await UsageRecording.ReportAsync(response, ct).ConfigureAwait(false);
                if (retries > 0)
                    span?.SetAttribute("gen_ai.retry_count", retries.ToString());
                return response;
            }
            catch (OperationCanceledException) { throw; }
            catch (GuardrailException) { throw; }
            catch (Exception ex) when (!_shouldRetry(ex))
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt == _maxRetryAttempts - 1)
                    break;

                retries++;
                var delayMs = (int)(_retryBaseDelay.TotalMilliseconds * Math.Pow(2, retries - 1));
                _logger.LogWarning(ex,
                    "[{AgentName}] LLM call failed (attempt {Attempt}/{Max}), retrying in {Delay}ms",
                    Name, retries, _maxRetryAttempts, delayMs);
                snapshotBuilder?.RecordRetry();
                span?.RecordRetry(retries, ex.Message);
                ToolMetrics.ModelRetry.Add(1,
                    new KeyValuePair<string, object?>("agent_id", Name));
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        span?.SetAttribute("gen_ai.retry_count", retries.ToString());
        throw new InvalidOperationException(
            $"[{Name}] LLM call failed after {_maxRetryAttempts} attempts.", lastException!);
    }
}
