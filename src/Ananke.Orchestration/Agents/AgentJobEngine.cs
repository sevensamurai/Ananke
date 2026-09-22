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
using Ananke.Orchestration.Streaming;
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
    private readonly ToolOutputPolicy? _toolOutputPolicy;

    /// <summary>
    /// What the tool schemas cost. Sent on every call, invisible to a context strategy, and computed
    /// once because the tool set does not change for the life of a job.
    /// </summary>
    private readonly int _toolSchemaTokens;

    /// <summary>What the pinned contract costs in every assembly. Computed once; it does not vary.</summary>
    /// <remarks>
    /// The contract itself is not held as a field. It is rendered into <see cref="_systemPrompt"/>
    /// at construction and that is the only thing anything here needs; keeping the structure around
    /// for a slice that does not exist yet would be a field with no reader.
    /// </remarks>
    private readonly int _contractTokens;
    private readonly ILogger _logger;
    private readonly IHallucinationObserver? _hallucinationObserver;
    private readonly ITrajectoryObserver? _trajectoryObserver;
    private readonly string? _kitName;

    private readonly AgentResponseFormat? _responseFormat;
    private readonly Func<string, TResult> _extractResult;
    private readonly string _finalCallSpanSuffix;
    private readonly string? _coercionPrompt;

    /// <summary>
    /// Sampling temperature applied to every request this job makes. <see langword="null"/> leaves
    /// it to the provider.
    /// </summary>
    private readonly double? _temperature;

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
        string? coercionPrompt,
        double? temperature = null,
        ToolOutputPolicy? toolOutputPolicy = null,
        AgentContract? contract = null)
    {
        Name = name;
        _model = model;
        _temperature = temperature;
        _promptBuilder = promptBuilder;
        _mapResult = mapResult;
        // Composed once, here, rather than at each call site. The contract is pinned by being part
        // of what every assembly carries by construction — there is no path through this type that
        // can assemble a request and leave it out, which is the property a per-call-site render
        // would not have.
        _systemPrompt = AgentContract.Compose(systemPrompt, contract);
        _contractTokens = contract is null
            ? 0
            : ApproximateTokenCounter.Instance.EstimateTokens(contract.Render());
        _tools = tools;
        _toolSchemaTokens = tools is null
            ? 0
            : RequestTokenEstimator.Estimate(new AgentRequest { Messages = [], Tools = tools });
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
        _toolOutputPolicy = toolOutputPolicy;
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
            var projection = await CompactAsync(messages, ct).ConfigureAwait(false);
            if (!ReferenceEquals(projection.Messages, messages))
            {
                historyCount = 0;
                messages = [.. projection.Messages];
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
                : await ExecuteFinalCallAsync(
                    messages, snapshotBuilder, compactBeforeSending: false, ct).ConfigureAwait(false);
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
    /// <remarks>
    /// <c>compactBeforeSending</c> says whether the context strategy still has to run: <c>false</c>
    /// on the toolless path, where assembly already compacted and re-running would emit a second
    /// record for a single model call; <c>true</c> for the coercion round, where the tool loop has
    /// grown the message list since — the loop compacts what it *sends* each round but never writes
    /// the result back, so this call would otherwise ship the whole uncompacted history.
    /// </remarks>
    private async Task<TResult> ExecuteFinalCallAsync(
        List<AgentMessage> messages,
        TrajectorySnapshotBuilder? snapshotBuilder,
        bool compactBeforeSending,
        CancellationToken ct)
    {
        var parentSpan = WorkflowTraceContext.Value?.CurrentSpan;
        await using var llmSpan = parentSpan?.StartSpan($"{Name}/{_finalCallSpanSuffix}", SpanKind.LlmCall);

        IReadOnlyList<AgentMessage> outbound = messages;
        if (compactBeforeSending && _contextStrategy is not null)
            outbound = (await CompactAsync(messages, ct).ConfigureAwait(false)).Messages;

        // A configured limit has to bind here too. Both tool-loop enforcement points sit inside the
        // loop, and a job with no tools never enters it — so before this, WithContextLimit was
        // silently inert for every toolless agent. Measured on what is actually about to be sent.
        if (_maxContextTokens.HasValue)
            EnforceContextLimit(outbound, toolRound: null);

        var request = new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = outbound,
            ResponseFormat = _responseFormat,
            Metadata = BuildMetadata(),
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? false,
            Temperature = _temperature
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
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? false,
            Temperature = _temperature
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
                    snapshotBuilder?.RecordToolCall(hallucinated: false, faulted: true, call.FunctionName, call.Arguments);
                }
                else if (_toolExecutors!.TryGetValue(call.FunctionName, out var executor))
                {
                    toolResult = await executor.ExecuteAsync(args, ct).ConfigureAwait(false);
                    snapshotBuilder?.RecordToolCall(
                        hallucinated: false, faulted: toolResult.IsError, call.FunctionName, call.Arguments);
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
                    snapshotBuilder?.RecordToolCall(hallucinated: true, faulted: false, call.FunctionName, call.Arguments);
                }

                var trace = WorkflowTraceContext.Value;

                await WorkflowEventReporting.ReportAsync(new AgentToolCalled
                {
                    WorkflowName = trace?.WorkflowName ?? string.Empty,
                    ExecutionId = trace?.ExecutionId ?? string.Empty,
                    AgentName = Name,
                    ToolName = call.FunctionName,
                    Arguments = call.Arguments,
                    Result = toolResult.Value.Length <= AgentToolCalled.ResultCap
                        ? toolResult.Value
                        : toolResult.Value[..AgentToolCalled.ResultCap],
                    ResultLength = toolResult.Value.Length,
                    IsError = toolResult.IsError
                }, ct).ConfigureAwait(false);

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

                var carried = await CapToolOutputAsync(
                    call.FunctionName, toolResult.Value, toolSpan, ct).ConfigureAwait(false);
                messages.Add(AgentMessage.ToolResult(call.Id, carried));
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
                requestMessages = (await CompactAsync(messages, ct).ConfigureAwait(false)).Messages;

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
            return await ExecuteFinalCallAsync(
                messages, snapshotBuilder, compactBeforeSending: true, ct).ConfigureAwait(false);
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
    private void EnforceContextLimit(IReadOnlyList<AgentMessage> measured, int? toolRound)
    {
        var preFlight = new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = measured,
            Tools = _tools,
            Metadata = BuildMetadata(),
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? false,
        };

        var stage = toolRound is { } round ? $"tool round {round + 1}" : "the model call";

        var estimated = RequestTokenEstimator.Estimate(preFlight);
        if (estimated > _maxContextTokens!.Value)
            throw new InvalidOperationException(
                $"[{Name}] Estimated context ({estimated:N0} tokens) exceeds the configured limit " +
                $"of {_maxContextTokens.Value:N0} tokens before {stage}.");
        if (estimated > (int)(_maxContextTokens.Value * 0.8))
            _logger.LogWarning(
                "[{AgentName}] Context approaching limit: ~{Estimated:N0}/{Max:N0} estimated tokens (before {Stage})",
                Name, estimated, _maxContextTokens.Value, stage);
    }

    /// <summary>
    /// Bounds one tool result before it enters the message list, spilling or truncating it per the
    /// configured <see cref="ToolOutputPolicy"/>. Without a policy the result is carried whole,
    /// which is the behaviour every agent has today.
    /// </summary>
    /// <remarks>
    /// This runs at the point of arrival rather than at assembly on purpose. A four-megabyte tool
    /// result that reaches the message list is already in the window for every subsequent round;
    /// bounding it later means having paid for it in between.
    /// </remarks>
    private async Task<string> CapToolOutputAsync(
        string toolName, string value, ISpan? toolSpan, CancellationToken ct)
    {
        if (_toolOutputPolicy is null)
            return value;

        var (capped, record) = await ToolOutputHygiene
            .CapAsync(_toolOutputPolicy, toolName, value, ct).ConfigureAwait(false);

        if (record is null)
            return capped;

        toolSpan?.SetAttribute("tool.output_capped", "true");
        toolSpan?.SetAttribute("tool.output_chars_after", record.CharsAfter.ToString());
        toolSpan?.SetAttribute("tool.output_recoverable", record.Recoverable ? "true" : "false");

        _logger.LogInformation(
            "[{AgentName}] Tool '{Tool}' returned {Before:N0} characters; carried {After:N0} inline ({Disposition}).",
            Name, toolName, record.CharsBefore, record.CharsAfter,
            record.Recoverable ? $"spilled to {record.Spilled!.Locator}" : "tail discarded");

        await ContextObserving.ReportAsync(record, ct).ConfigureAwait(false);

        return capped;
    }

    /// <summary>
    /// Applies the context strategy under a budget reconciled from the configured limit and the
    /// selected model's real window, and reports what it withheld.
    /// </summary>
    /// <remarks>
    /// <b>This is where three numbers that each claimed to be "the context budget" finally meet.</b>
    /// The developer's configured limit is the allocation; the model's real window is the cap; the
    /// strategy's own constructor value is the fallback when there is no allocation. Capacity only
    /// ever lowers the figure — a small configured budget is never inflated to fill a large window,
    /// because that decision needs to know what the work is for, and nothing here does yet.
    /// </remarks>
    private async Task<ContextProjection> CompactAsync(
        IReadOnlyList<AgentMessage> messages, CancellationToken ct)
    {
        var budget = BuildContextBudget(messages);

        var input = messages;
        ToolOutputPruningRecord? pruning = null;
        ContextProjection? projection = null;

        if (_toolOutputPolicy is not null
            && ToolOutputHygiene.Prune(messages, _toolOutputPolicy) is { } reduced)
        {
            input = reduced.Messages;

            // The point of pruning first: if shedding stale tool output is enough on its own, the
            // strategy never runs — and a summary that never happens is a lossy rewrite that never
            // happens. Dropping a stale grep dump whose conclusion is already in the transcript
            // costs the run nothing it needs; paraphrasing the transcript to make room for it costs
            // fidelity everywhere. Not paying for the call is a side effect, not the reason.
            //
            // Claimed only when there is an explicit allocation, because that is the one case where
            // the ceiling the strategy would have enforced is knowable from out here: Resolve()
            // returns it exactly, without needing the strategy's own configured fallback. With only
            // a window known the strategy may well be configured tighter, and skipping it on the
            // window's authority would quietly overrule the caller.
            var ceiling = budget.HasAllocation ? budget.Resolve(0) : 0;
            var averted = ceiling > 0 && MeasureRequest(input) <= ceiling;
            if (averted)
                projection = ContextProjection.Unchanged(input, ceiling);

            pruning = new ToolOutputPruningRecord
            {
                ReplacedCount = reduced.ReplacedCount,
                CharsBefore = reduced.CharsBefore,
                CharsAfter = reduced.CharsAfter,
                AvertedCompaction = averted
            };
        }

        projection ??= await _contextStrategy!
            .ApplyAsync(input, _systemPrompt, budget, ct).ConfigureAwait(false);

        if (ContextObserving.Current is not null)
        {
            await ContextObserving.ReportAsync(new ContextCompactionRecord
            {
                Budget = budget,
                Projection = projection,
                InputMessageCount = messages.Count,
                ContractTokens = _contractTokens,
                Pruning = pruning
            }, ct).ConfigureAwait(false);
        }

        return projection;
    }

    /// <summary>
    /// Reports what this assembly cost against the window it has to fit, once per assembly rather
    /// than once per retry — a retried call is the same prompt, and counting it twice would make a
    /// flaky provider look like window pressure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A routed model reports this itself, from the place that selected the profile. Everything else
    /// is reported here, because otherwise the figure that matters most — how often an assembled
    /// prompt did not fit — would be collectable only from workflows that happened to use a router,
    /// and a baseline that silently covers a subset of runs is worse than no baseline.
    /// </para>
    /// <para>
    /// Nothing is reported when the model cannot say what its window is: an observation with no
    /// capacity in it cannot answer the question, and recording it as though it could would let a
    /// run with no measurable calls read as a run with no problems.
    /// </para>
    /// </remarks>
    private ValueTask ObserveAssemblyAsync(AgentRequest request, CancellationToken ct)
    {
        if (ContextObserving.Current is null || _model is RoutedAgentModel)
            return ValueTask.CompletedTask;

        var window = _model is IModelContextResolver resolver
            ? resolver.ResolveContextWindow(request)
            : ModelContextWindow.Unknown;

        return ContextObserving.ReportAsync(new ContextObservation
        {
            ModelName = window.ModelName,
            PromptTokens = RequestTokenEstimator.Estimate(request),
            ContextTokens = window.ContextTokens,
            ContractTokens = _contractTokens
        }, ct);
    }

    /// <summary>Estimates what one message list costs as an assembled request, tool schemas included.</summary>
    private int MeasureRequest(IReadOnlyList<AgentMessage> messages) =>
        RequestTokenEstimator.Estimate(new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = messages,
            Tools = _tools,
            Metadata = BuildMetadata()
        });

    /// <summary>
    /// Builds the budget for one assembly. The window is resolved from the model itself when it can
    /// answer — a routed model forwards its router's answer — so a strategy configured for one
    /// window is no longer applied blindly to whichever model the router happened to pick.
    /// </summary>
    private ContextBudget BuildContextBudget(IReadOnlyList<AgentMessage> messages)
    {
        var window = ModelContextWindow.Unknown;
        if (_model is IModelContextResolver resolver)
        {
            window = resolver.ResolveContextWindow(new AgentRequest
            {
                SystemPrompt = _systemPrompt,
                Messages = messages,
                Tools = _tools,
                Metadata = BuildMetadata()
            });
        }

        if (!window.IsKnown && _maxContextTokens is null)
            return ContextBudget.Unspecified;

        return new ContextBudget
        {
            MaxTokens = _maxContextTokens,
            ModelContextTokens = window.ContextTokens,
            ModelName = window.ModelName,
            ReservedTokens = _toolSchemaTokens
        };
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


    /// <summary>
    /// Default retry predicate: HTTP 429 rate-limit errors that waiting can clear (see
    /// <see cref="ResilientAgentModel.IsTransientRateLimit"/>) and <see cref="TimeoutException"/>.
    /// Everything else — 4xx auth/validation errors, <see cref="GuardrailException"/>, a 429 that
    /// says the account is out of allowance, arbitrary application exceptions — is treated as
    /// non-retryable and rethrown on the first occurrence.
    /// </summary>
    /// <remarks>
    /// The exclusion is not a tuning choice. A depleted account answers 429 exactly as a busy one
    /// does, so without it the engine spends three round trips <em>per job</em> on a wall that will
    /// not move, and the provider's own explanation reaches the reader on the third attempt rather
    /// than the first.
    /// </remarks>
    public static bool DefaultShouldRetry(Exception ex) =>
        ResilientAgentModel.IsTransientRateLimit(ex) || ex is TimeoutException;

    private async Task<AgentResponse> GenerateWithRetryAsync(
        AgentRequest request,
        TrajectorySnapshotBuilder? snapshotBuilder,
        ISpan? span,
        CancellationToken ct)
    {
        await ObserveAssemblyAsync(request, ct).ConfigureAwait(false);

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

                // What the provider asked for wins over what we would have guessed. A limit measured
                // in minutes outlives three exponential backoffs from a one-second base, so ignoring
                // the stated delay spends every attempt before the window it is waiting for moves.
                var stated = ProviderRetryDelay.From(ex);
                var delayMs = stated is { } wait
                    ? (int)wait.TotalMilliseconds
                    : (int)(_retryBaseDelay.TotalMilliseconds * Math.Pow(2, retries - 1));

                _logger.LogWarning(ex,
                    "[{AgentName}] LLM call failed (attempt {Attempt}/{Max}), retrying in {Delay}ms{Source}",
                    Name, retries, _maxRetryAttempts, delayMs,
                    stated is null ? "" : " (the provider asked for it)");
                snapshotBuilder?.RecordRetry();
                span?.RecordRetry(retries, ex.Message);
                ToolMetrics.ModelRetry.Add(1,
                    new KeyValuePair<string, object?>("agent_id", Name));
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        span?.SetAttribute("gen_ai.retry_count", retries.ToString());

        // The provider's own words, in the message rather than only on the inner exception. What
        // records a failure keeps `Exception.Message` — a plan node's failure, a log line — so a
        // wrapper that says only how many attempts were made hides the one sentence that explains
        // them.
        throw new InvalidOperationException(
            $"[{Name}] LLM call failed after {_maxRetryAttempts} attempts: {lastException!.Message}",
            lastException);
    }
}
