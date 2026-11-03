using System.Text.Json;
using System.Text.Json.Nodes;
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
/// trajectory/hallucination reporting) is shared, and this is the single place it lives.
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

    /// <summary>How the state is mapped when a call to a tool that ends the job ended it; null leaves it as it was.</summary>
    private readonly Func<TState, string, TState>? _endedByTool;
    private readonly string? _systemPrompt;
    private readonly IReadOnlyList<AgentTool>? _tools;
    private readonly IReadOnlyDictionary<string, ToolDefinition>? _toolExecutors;
    private readonly Action<TState, TResult>? _onResponse;
    private readonly int _maxToolRounds;
    private readonly int? _maxRepeatedToolRounds;
    private readonly int _maxRetryAttempts;
    private readonly TimeSpan _retryBaseDelay;
    private readonly Func<Exception, bool> _shouldRetry;
    private readonly int? _maxContextTokens;
    private readonly ContextLimitMode _contextLimitMode;
    private readonly IConversationMemory? _memory;
    private readonly Func<TState, string>? _sessionIdBuilder;
    private readonly IContextStrategy? _contextStrategy;
    private readonly ToolOutputPolicy? _toolOutputPolicy;
    private readonly TimeProvider _timeProvider;

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

    /// <summary>
    /// The fields of an endpoint's own that every request this job makes carries; <see langword="null"/>
    /// carries none.
    /// </summary>
    private readonly IReadOnlyDictionary<string, JsonNode>? _requestFields;

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
        IReadOnlyDictionary<string, JsonNode>? requestFields = null,
        ToolOutputPolicy? toolOutputPolicy = null,
        AgentContract? contract = null,
        TimeProvider? timeProvider = null,
        int? maxRepeatedToolRounds = null,
        Func<TState, string, TState>? endedByTool = null)
    {
        Name = name;
        _model = model;
        _temperature = temperature;
        _requestFields = requestFields;
        _promptBuilder = promptBuilder;
        _mapResult = mapResult;
        _endedByTool = endedByTool;
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
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
        _hallucinationObserver = hallucinationObserver;
        _trajectoryObserver = trajectoryObserver;
        _kitName = kitName;
        _responseFormat = responseFormat;
        _extractResult = extractResult;
        _finalCallSpanSuffix = finalCallSpanSuffix;
        _coercionPrompt = coercionPrompt;
        _maxRepeatedToolRounds = maxRepeatedToolRounds;
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

        // Reference equality: the loop's own scaffolding (a permanent-error nudge, the
        // structured path's coercion prompt) is never something the user said, so it never
        // reaches IConversationMemory even though it lives in this same list.
        var synthetic = new HashSet<AgentMessage>(ReferenceEqualityComparer.Instance);

        var snapshotBuilder = _trajectoryObserver is not null
            ? new TrajectorySnapshotBuilder(Name, _trajectoryObserver, _timeProvider)
            : null;

        TResult response = default!;
        string? endedWith = null;
        var succeeded = false;
        try
        {
            if (_tools is not null)
                (response, endedWith) = await ExecuteWithToolsAsync(messages, synthetic, snapshotBuilder, ct).ConfigureAwait(false);
            else
                response = await ExecuteFinalCallAsync(messages, synthetic, snapshotBuilder, ct).ConfigureAwait(false);

            succeeded = true;
        }
        finally
        {
            if (snapshotBuilder is not null)
                await snapshotBuilder.CompleteAsync(succeeded, ct: ct).ConfigureAwait(false);
        }

        if (_memory is not null && sessionId is not null && historyCount < messages.Count)
        {
            var newMessages = messages
                .GetRange(historyCount, messages.Count - historyCount)
                .Where(m => !synthetic.Contains(m))
                .ToList();
            await _memory.AddAsync(sessionId, newMessages, ct).ConfigureAwait(false);
        }

        // A call to a tool that ends the job was its last act, and no answer was asked for to map.
        if (endedWith is not null)
            return _endedByTool is null ? state : _endedByTool(state, endedWith);

        _onResponse?.Invoke(state, response);
        return _mapResult(state, response);
    }

    /// <summary>
    /// The single model call used both as the whole job (no tools configured) and as the
    /// structured path's JSON-coercion round after the tool loop. Carries
    /// <see cref="_responseFormat"/> (or none, for text jobs).
    /// </summary>
    /// <remarks>
    /// Compacts fresh from <paramref name="messages"/> every time it runs, never writing the result
    /// back — on the toolless path this is the run's only compaction; for the coercion round it is
    /// necessary, because the tool loop compacts what it *sends* each round but never writes the
    /// result back either, so the message list has grown since the loop's own last compaction.
    /// </remarks>
    private async Task<TResult> ExecuteFinalCallAsync(
        List<AgentMessage> messages,
        HashSet<AgentMessage> synthetic,
        TrajectorySnapshotBuilder? snapshotBuilder,
        CancellationToken ct)
    {
        var parentSpan = WorkflowTraceContext.Value?.CurrentSpan;
        await using var llmSpan = parentSpan?.StartSpan($"{Name}/{_finalCallSpanSuffix}", SpanKind.LlmCall);

        var request = new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = await FinalCallMessagesAsync(messages, ct).ConfigureAwait(false),
            ResponseFormat = _responseFormat,
            Metadata = BuildMetadata(),
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? false,
            Temperature = _temperature,
#pragma warning disable ANNKEEXP003 // a request's own fields are experimental
            RequestFields = _requestFields
#pragma warning restore ANNKEEXP003
        };

        var text = await GenerateFinalTextAsync(request, messages, snapshotBuilder, llmSpan, ct).ConfigureAwait(false);

        try
        {
            return _extractResult(text);
        }
        catch (JsonException ex)
        {
            // A provider that is only asked for the schema, not one that enforces it, sometimes
            // wraps the reply in a code fence or adds a sentence of preamble — JsonReply already
            // recovers that; a reply that still doesn't parse after that is a real defect, not a
            // formatting slip, so only one repair round is spent on it.
            var repair = AgentMessage.User(
                $"Your reply was not valid JSON for the schema ({ex.Message}). Reply with the JSON object only.");
            synthetic.Add(repair);
            messages.Add(repair);

            request = request with { Messages = await FinalCallMessagesAsync(messages, ct).ConfigureAwait(false) };
            var repairText = await GenerateFinalTextAsync(request, messages, snapshotBuilder, llmSpan, ct).ConfigureAwait(false);
            return _extractResult(repairText);
        }
    }

    /// <summary>
    /// What a final call sends: <paramref name="messages"/> compacted by the context strategy, then
    /// held to the configured limit. Run for each call, the repair round included, since each sends
    /// a list that has grown since the last.
    /// </summary>
    private async Task<IReadOnlyList<AgentMessage>> FinalCallMessagesAsync(
        List<AgentMessage> messages, CancellationToken ct)
    {
        IReadOnlyList<AgentMessage> outbound = messages;
        if (_contextStrategy is not null)
            outbound = (await CompactAsync(messages, ct).ConfigureAwait(false)).Messages;

        // A job with no tools never enters the tool loop, where the limit is otherwise enforced.
        // Measured on what is actually about to be sent.
        if (_maxContextTokens.HasValue)
            EnforceContextLimit(outbound, toolRound: null);

        return outbound;
    }

    /// <summary>Sends one request, records the reply as the assistant turn, and returns its text.</summary>
    private async Task<string> GenerateFinalTextAsync(
        AgentRequest request,
        List<AgentMessage> messages,
        TrajectorySnapshotBuilder? snapshotBuilder,
        ISpan? llmSpan,
        CancellationToken ct)
    {
        var result = await GenerateWithRetryAsync(request, snapshotBuilder, llmSpan, ct).ConfigureAwait(false);

        // A structured reply cut off at the limit cannot be repaired by asking again, and would
        // otherwise surface as a JSON error that names the symptom rather than the cause.
        if (request.ResponseFormat is not null && result.StopReason == AgentStopReason.MaxTokens)
        {
            throw new InvalidOperationException(
                $"[{Name}] The reply was cut off at the output-token limit before its JSON was complete. "
                + "Raise the model's output-token limit, or ask for less.");
        }

        var text = result.Text
            ?? throw new InvalidOperationException($"[{Name}] LLM returned empty response.");

        llmSpan?.SetAttribute("response_length", text.Length.ToString());

        // Record the model's own turn so IConversationMemory persists both sides of the
        // exchange. Without this, an agent using WithMemory() writes the user prompt but
        // never its own answer, and the next turn reloads a one-sided history.
        messages.Add(AgentMessage.Assistant(text));

        return text;
    }

    /// <summary>
    /// The tool loop, then the answer: what the model said last, or one more call that turns it into
    /// the schema. When a call to a tool that ends the job ran, no answer is asked for, and what the
    /// model wrote with that call comes back as <c>EndedWith</c> instead.
    /// </summary>
    private async Task<(TResult Response, string? EndedWith)> ExecuteWithToolsAsync(
        List<AgentMessage> messages,
        HashSet<AgentMessage> synthetic,
        TrajectorySnapshotBuilder? snapshotBuilder,
        CancellationToken ct)
    {
        var parentSpan = WorkflowTraceContext.Value?.CurrentSpan;
        await using var llmSpan = parentSpan?.StartSpan($"{Name}/tool-loop", SpanKind.LlmCall);
        var toolRound = 0;

        IReadOnlyList<AgentMessage> initialMessages = messages;
        if (_contextStrategy is not null)
            initialMessages = (await CompactAsync(messages, ct).ConfigureAwait(false)).Messages;

        var request = new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = initialMessages,
            Tools = _tools,
            Metadata = BuildMetadata(),
            StoreCompletions = WorkflowTraceContext.Value?.StoreCompletions ?? false,
            Temperature = _temperature,
#pragma warning disable ANNKEEXP003 // a request's own fields are experimental
            RequestFields = _requestFields
#pragma warning restore ANNKEEXP003
        };

        var result = await GenerateWithRetryAsync(request, snapshotBuilder, llmSpan, ct).ConfigureAwait(false);

        // A call to a tool that ends the turn has run; and, per call, the answer it was given and the
        // message that carried it.
        var turnEnded = false;
        string? endedWith = null;
        var repeatedRounds = 0;
        var answered = new Dictionary<string, (string Value, AgentMessage Message)>(StringComparer.Ordinal);

        while (result.RequiresAction)
        {
            var stop = turnEnded
                    ? (Stub: "Not executed: a call before it ended this turn.",
                       Nudge: "Your turn has ended. Answer now with what you have done; do not call tools.",
                       Failure: $"[{Name}] The model went on calling tools after a call ended its turn, "
                                + "and again when asked to answer without them.")
                : toolRound >= _maxToolRounds
                    ? (Stub: "Not executed: the tool-call limit for this turn was reached.",
                       Nudge: "You have reached the tool-call limit. Answer now with what you have; do not call tools.",
                       Failure: $"[{Name}] Tool-calling loop exceeded the maximum of {_maxToolRounds} rounds. "
                                + "Adjust the system prompt or call WithMaxToolRounds() to increase the limit.")
                : ((string Stub, string Nudge, string Failure)?)null;

            if (stop is { } why)
            {
                toolRound++;
                var finished = await FinishTurnAsync(
                    messages, synthetic, result, request, snapshotBuilder, llmSpan, toolRound,
                    why.Stub, why.Nudge, why.Failure, runCallsThatEndTheTurn: !turnEnded, ct).ConfigureAwait(false);

                if (finished is null)
                {
                    llmSpan?.SetAttribute("tool_rounds", toolRound.ToString());
                    return (default!, result.Text ?? string.Empty);
                }

                result = finished;
                break;
            }

            toolRound++;
            messages.Add(AgentMessage.Assistant(
                result.Text ?? string.Empty, result.ToolCalls));

            var hasNonRetryable = false;
            var onlyRepeats = true;

            foreach (var call in result.ToolCalls!)
            {
                if (turnEnded)
                {
                    messages.Add(AgentMessage.ToolResult(call.Id, "Not executed: a call before it ended this turn."));
                    continue;
                }

                var (toolResult, tool, carried) = await RunCallAsync(call, toolRound, llmSpan, snapshotBuilder, ct).ConfigureAwait(false);

                if (toolResult.IsError && !toolResult.IsRetryable)
                    hasNonRetryable = true;

                var answer = AgentMessage.ToolResult(call.Id, carried);

                if (_maxRepeatedToolRounds is not null)
                {
                    var key = CallKey(call);

                    // A repeat only if the model was sent the earlier answer: one a context strategy
                    // withheld from the last request is sent again, and asking for it is no loop.
                    if (answered.TryGetValue(key, out var before) && before.Value == toolResult.Value
                        && request.Messages.Any(sent => ReferenceEquals(sent, before.Message)))
                    {
                        answer = AgentMessage.ToolResult(
                            call.Id, $"Answered before in this turn: {call.FunctionName} with these arguments gave this same answer.");
                    }
                    else
                    {
                        answered[key] = (toolResult.Value, answer);
                        onlyRepeats = false;
                    }
                }

                messages.Add(answer);

                if (tool?.EndsTurn == true && !toolResult.IsError)
                    turnEnded = true;

                if (tool?.EndsJob == true && !toolResult.IsError)
                {
                    turnEnded = true;
                    endedWith = result.Text ?? string.Empty;
                }
            }

            if (endedWith is not null)
            {
                llmSpan?.SetAttribute("tool_rounds", toolRound.ToString());
                return (default!, endedWith);
            }

            repeatedRounds = onlyRepeats ? repeatedRounds + 1 : 0;

            // A model that only repeats what it was already answered is looping: asked to answer
            // instead, it would be paid to go on, so the run ends as it does on a repeating reply.
            if (repeatedRounds >= _maxRepeatedToolRounds)
            {
                throw new RepeatedOutputException(
                    $"[{Name}] The model repeated calls already answered in this turn, {repeatedRounds} rounds in a row: "
                    + string.Join(", ", result.ToolCalls!.Select(call => call.FunctionName).Distinct(StringComparer.Ordinal)) + ".");
            }

            if (hasNonRetryable)
            {
                var nudge = AgentMessage.User(
                    "One or more tools returned a permanent error that will not succeed on retry. " +
                    "Do not call those tools again. Proceed with your best answer using any information you already have.");
                synthetic.Add(nudge);
                messages.Add(nudge);
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
            var coercion = AgentMessage.User(_coercionPrompt);
            synthetic.Add(coercion);
            messages.Add(coercion);
            return (await ExecuteFinalCallAsync(messages, synthetic, snapshotBuilder, ct).ConfigureAwait(false), null);
        }

        // Text path: no follow-up call, so the loop's own last response is the final answer
        // and must be validated here.
        var text = result.Text
            ?? throw new InvalidOperationException($"[{Name}] LLM returned empty response after tool loop.");
        messages.Add(AgentMessage.Assistant(text));
        return (_extractResult(text), null);
    }

    /// <summary>
    /// Runs one call the model asked for — or reports it as unknown or malformed — records it, and
    /// returns its result, the tool it reached, and the text carried back to the model.
    /// </summary>
    private async Task<(ToolResult Result, ToolDefinition? Tool, string Carried)> RunCallAsync(
        AgentToolCall call, int toolRound, ISpan? llmSpan, TrajectorySnapshotBuilder? snapshotBuilder, CancellationToken ct)
    {
        await using var toolSpan = llmSpan?.StartSpan($"execute_tool {call.FunctionName}", SpanKind.ToolCall);
        toolSpan?.SetAttribute("gen_ai.operation.name", "execute_tool");
        toolSpan?.SetAttribute("gen_ai.tool.name", call.FunctionName);
        toolSpan?.SetAttribute("gen_ai.tool.call.id", call.Id);
        toolSpan?.SetAttribute("tool_round", toolRound);

        ToolResult toolResult;
        ToolDefinition? executor = null;

        if (_toolExecutors!.TryGetValue(call.FunctionName, out var registered))
        {
            toolSpan?.SetAttribute("tool.execution_mode", registered.ExecutionMode.ToString().ToLowerInvariant());

            if (TargetHost(registered.Endpoint) is { } target)
                toolSpan?.SetAttribute("tool.target", target);
        }

        var started = _timeProvider.GetTimestamp();

        if (!ToolDispatch.TryParseArguments(call.Arguments, call.FunctionName, out var args, out var parseError))
        {
            toolSpan?.SetAttribute("tool.malformed_arguments", true);
            toolResult = parseError;
            snapshotBuilder?.RecordToolCall(hallucinated: false, faulted: true, call.FunctionName, call.Arguments);
        }
        else if (registered is not null)
        {
            executor = registered;
            toolResult = await ToolDispatch.InvokeAsync(executor, args, ct, _logger).ConfigureAwait(false);
            snapshotBuilder?.RecordToolCall(
                hallucinated: false, faulted: toolResult.IsError, call.FunctionName, call.Arguments);
        }
        else
        {
            toolResult = await UnknownTools.ReportAsync(
                _hallucinationObserver, call.FunctionName, _kitName, Name,
                snapshotBuilder?.EpisodeId ?? string.Empty, _timeProvider, ct).ConfigureAwait(false);

            toolSpan?.SetAttribute("tool.hallucination", true);
            toolSpan?.SetAttribute("tool.hallucination.requested_name", call.FunctionName);
            snapshotBuilder?.RecordToolCall(hallucinated: true, faulted: false, call.FunctionName, call.Arguments);
        }

        var duration = _timeProvider.GetElapsedTime(started);

        // Built only for a sink that will read it, so a run nobody streams pays nothing for it.
        if (WorkflowEventReporting.Current is { } sink)
        {
            var trace = WorkflowTraceContext.Value;
            var content = trace?.RecordContent == true;
            var result = content ? toolResult.Recorded() : null;

            await sink.ReportAsync(new AgentToolCalled
            {
                WorkflowName = trace?.WorkflowName ?? string.Empty,
                ExecutionId = trace?.ExecutionId ?? string.Empty,
                AgentName = Name,
                ToolName = call.FunctionName,
                CallId = call.Id,
                Round = toolRound,
                Duration = duration,
                Arguments = content ? registered?.Recorded(call.Arguments) ?? call.Arguments : null,
                Result = result is null || result.Length <= AgentToolCalled.ResultCap
                    ? result
                    : result[..AgentToolCalled.ResultCap],
                ResultLength = toolResult.Value.Length,
                IsError = toolResult.IsError
            }, ct).ConfigureAwait(false);
        }

        if (toolResult.IsError)
        {
            // A tool reports its error as text, so there is no class of error to name, and the
            // convention's value for that is "_OTHER".
            toolSpan?.SetAttribute("error.type", "_OTHER");
            toolSpan?.SetAttribute("tool.error", true);
            _logger.LogWarning(
                "[{AgentName}] Tool '{Tool}' returned error: {Error}",
                Name, call.FunctionName, toolResult.Value);

            if (!toolResult.IsRetryable)
                toolSpan?.SetAttribute("tool.retryable", false);
        }

        toolSpan?.SetAttribute("output_length", toolResult.Value.Length);

        var carried = await CapToolOutputAsync(
            call.FunctionName, toolResult.Value, toolSpan, ct).ConfigureAwait(false);
        return (toolResult, executor, carried);
    }

    /// <summary>
    /// The host a remote tool is reached at, or <see langword="null"/> for a tool with no absolute
    /// endpoint. Never the path or query: a URI can carry a key in its query.
    /// </summary>
    private static string? TargetHost(ToolEndpoint? endpoint) =>
        endpoint?.Uri is { IsAbsoluteUri: true, Host: { Length: > 0 } host } ? host : null;

    /// <summary>A call's tool and its arguments with their keys in order, so the same call reads the same.</summary>
    private static string CallKey(AgentToolCall call)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return call.FunctionName + "\n" + string.Join("\n", doc.RootElement.EnumerateObject()
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => p.Name + "=" + p.Value.GetRawText()));
        }
        catch (JsonException)
        {
        }

        return call.FunctionName + "\n" + call.Arguments;
    }

    /// <summary>
    /// One last chance instead of an abrupt failure: every call the model asked for when its turn
    /// stopped is answered with <paramref name="stub"/> so the transcript stays valid — except a call
    /// to a tool that ends the turn or the job, which is run when <paramref name="runCallsThatEndTheTurn"/>
    /// — then one more call with the same tools, asked not to use them. Still asking for tools after that
    /// really is exhausted, not a fluke that one extra round would have cleared, and throws
    /// <paramref name="failure"/>, which names why the turn stopped. <see langword="null"/>, with no call
    /// made, when a call to a tool that ends the job ran.
    /// </summary>
    private async Task<AgentResponse?> FinishTurnAsync(
        List<AgentMessage> messages,
        HashSet<AgentMessage> synthetic,
        AgentResponse pending,
        AgentRequest request,
        TrajectorySnapshotBuilder? snapshotBuilder,
        ISpan? llmSpan,
        int toolRound,
        string stub,
        string nudgeText,
        string failure,
        bool runCallsThatEndTheTurn,
        CancellationToken ct)
    {
        messages.Add(AgentMessage.Assistant(pending.Text ?? string.Empty, pending.ToolCalls));

        var ended = false;
        var jobEnded = false;

        foreach (var call in pending.ToolCalls!)
        {
            if (runCallsThatEndTheTurn && !ended
                && _toolExecutors is not null
                && _toolExecutors.TryGetValue(call.FunctionName, out var tool) && (tool.EndsTurn || tool.EndsJob))
            {
                var (toolResult, _, carried) = await RunCallAsync(call, toolRound, llmSpan, snapshotBuilder, ct).ConfigureAwait(false);
                messages.Add(AgentMessage.ToolResult(call.Id, carried));
                ended = !toolResult.IsError;
                jobEnded = ended && tool.EndsJob;
                continue;
            }

            messages.Add(AgentMessage.ToolResult(call.Id, stub));
        }

        if (jobEnded)
            return null;

        var nudge = AgentMessage.User(nudgeText);
        synthetic.Add(nudge);
        messages.Add(nudge);

        IReadOnlyList<AgentMessage> requestMessages = messages;
        if (_contextStrategy is not null)
            requestMessages = (await CompactAsync(messages, ct).ConfigureAwait(false)).Messages;

        request = request with { Messages = requestMessages };
        var result = await GenerateWithRetryAsync(request, snapshotBuilder, llmSpan, ct).ConfigureAwait(false);

        if (result.RequiresAction)
            throw new InvalidOperationException(failure);

        return result;
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

        toolSpan?.SetAttribute("tool.output_capped", true);
        toolSpan?.SetAttribute("tool.output_chars_after", record.CharsAfter);
        toolSpan?.SetAttribute("tool.output_recoverable", record.Recoverable);

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
    private ContextBudget BuildContextBudget(IReadOnlyList<AgentMessage> messages) =>
        ContextBudgets.For(_model, new AgentRequest
        {
            SystemPrompt = _systemPrompt,
            Messages = messages,
            Tools = _tools,
            Metadata = BuildMetadata()
        }, _maxContextTokens, _toolSchemaTokens);

    /// <summary>
    /// Default retry predicate: <see cref="TimeoutException"/>, and an HTTP 429 that waiting can clear
    /// (see <see cref="ResilientAgentModel.IsTransientRateLimit"/>) <em>and</em> that states how long
    /// to wait (see <see cref="ProviderRetryDelay"/>). Everything else — a plain 429, 4xx
    /// auth/validation errors, <see cref="GuardrailException"/>, a 429 that says the account is out
    /// of allowance, arbitrary application exceptions — is rethrown on the first occurrence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain 429 is the provider SDK's to retry: each adapter's SDK already does, so retrying it here
    /// too multiplied every request. A delay stated in the error body is what an SDK does not read —
    /// it waits by the header — so that wait is waited here.
    /// </para>
    /// The exclusion is not a tuning choice. A depleted account answers 429 exactly as a busy one
    /// does, so without it the engine spends three round trips <em>per job</em> on a wall that will
    /// not move, and the provider's own explanation reaches the reader on the third attempt rather
    /// than the first.
    /// </remarks>
    public static bool DefaultShouldRetry(Exception ex) =>
        ex is TimeoutException
        || (ResilientAgentModel.IsTransientRateLimit(ex) && ProviderRetryDelay.From(ex) is not null);

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

                // A reply that repeats itself runs to the output limit; one the model ended itself did
                // not, whatever its last lines look like. An adapter that says nothing of why the reply
                // stopped is checked as if it had reached the limit.
                if (response.StopReason is AgentStopReason.MaxTokens or AgentStopReason.Unknown)
                    RepeatedOutput.ThrowIfRepeating(response.Text);

                return response;
            }
            catch (OperationCanceledException) { throw; }
            catch (GuardrailException) { throw; }
            catch (RepeatedOutputException) { throw; }
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
                var stated = ProviderRetryDelay.From(ex, _timeProvider);
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
