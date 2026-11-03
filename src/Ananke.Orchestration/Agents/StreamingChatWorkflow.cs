using Ananke.Orchestration.Workflows;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Ananke.Abstractions.Memory;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Tools;

using Ananke.Abstractions.Agents;

using Ananke.Orchestration.Usage;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Immutable state threaded through a <see cref="StreamingChatWorkflow"/>.
/// Tracks the conversation history, the latest model response, and the tool-round counter.
/// </summary>
public sealed record StreamingChatState
{
    /// <summary>The conversation message history, accumulated across job iterations.</summary>
    public required List<AgentMessage> Messages { get; init; }

    /// <summary>The most recent model response (<see langword="null"/> after tool execution).</summary>
    public AgentResponse? LastResponse { get; init; }

    /// <summary>Full accumulated text from the latest agent generation round.</summary>
    public string FullText { get; init; } = string.Empty;

    /// <summary>Number of tool-calling rounds completed so far.</summary>
    public int ToolRounds { get; init; }

    /// <summary>
    /// Optional session identifier for <see cref="IConversationMemory"/> scoping.
    /// When set, the workflow loads prior history and persists new messages automatically.
    /// Typically set to a workflow execution ID or a user/conversation identifier.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>Number of history messages loaded from memory (used to persist only new messages).</summary>
    internal int HistoryBaseline { get; init; }

    /// <summary>
    /// The workflow's own prompts in <see cref="Messages"/>, by reference: sent to the model but never
    /// something the user said, so never persisted to memory. Shared by every copy of one run's state.
    /// </summary>
    internal HashSet<AgentMessage> Synthetic { get; init; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>When <c>true</c>, the last generation was interrupted and the agent will be re-invoked.</summary>
    public bool WasInterrupted { get; init; }

    /// <summary>Partial text captured when the generation was interrupted.</summary>
    public string? PartialText { get; init; }
}

/// <summary>
/// Pre-built workflow for streaming agent chat with automatic tool-calling.
/// Encapsulates the common "agent → decide → tools → agent" loop and exposes
/// callback hooks so consumers can plug in any output transport (SSE, WebSocket, console, etc.).
/// </summary>
/// <example>
/// <code>
/// var execution = await StreamingChatWorkflow.Create("chat", model)
///     .WithSystemPrompt("You are a helpful assistant.")
///     .WithTools(toolkit)
///     .OnTextDelta(async delta =&gt; Console.Write(delta))
///     .OnToolResult(async (name, result) =&gt; Console.WriteLine($"[{name}]: {result}"))
///     .RunAsync(messages, ct);
/// </code>
/// </example>
public static class StreamingChatWorkflow
{
    /// <summary>Creates a new builder for a streaming chat workflow.</summary>
    /// <param name="name">Workflow name used in traces and logs.</param>
    /// <param name="model">The streaming agent model to use for LLM calls.</param>
    public static Builder Create(string name, IStreamingAgentModel model) => new(name, model);

    /// <summary>Fluent builder for a streaming agent chat workflow.</summary>
    public sealed class Builder
    {
        private readonly string _name;
        private readonly IStreamingAgentModel _model;
        private string? _systemPrompt;
        private ToolKit? _toolKit;
        private int _maxToolRounds = AgentJobFactory.DefaultMaxToolRounds;
        private double? _temperature;
        private Func<string, Task>? _onTextDelta;
        private Func<byte[], string, Task>? _onAudioDelta;
        private Func<string, string, Task>? _onToolCall;
        private Func<string, string, Task>? _onToolResult;
        private IConversationMemory? _memory;
        private IContextStrategy? _contextStrategy;
        private ToolOutputPolicy? _toolOutputPolicy;
        private TimeProvider _timeProvider = TimeProvider.System;

        internal Builder(string name, IStreamingAgentModel model)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(model);
            _name = name;
            _model = model;
        }

        /// <summary>
        /// Enables conversation memory. When set, the workflow loads prior conversation
        /// history before the first agent call and persists new messages after each turn.
        /// The session ID is taken from <see cref="StreamingChatState.SessionId"/>.
        /// </summary>
        public Builder WithMemory(IConversationMemory memory)
        {
            ArgumentNullException.ThrowIfNull(memory);
            _memory = memory;
            return this;
        }

        /// <summary>Sets the system prompt sent to the model on every generation round.</summary>
        public Builder WithSystemPrompt(string systemPrompt)
        {
            _systemPrompt = systemPrompt;
            return this;
        }

        /// <summary>Provides the tools the agent can call during the conversation.</summary>
        public Builder WithTools(ToolKit toolKit)
        {
            ArgumentNullException.ThrowIfNull(toolKit);
            _toolKit = toolKit;
            return this;
        }

        /// <summary>
        /// Sets the context strategy applied before each agent generation round.
        /// When set, the message history is passed through the strategy before
        /// building the <see cref="AgentRequest"/> sent to the model.
        /// </summary>
        /// <param name="strategy">The context compaction strategy.</param>
        public Builder WithContextStrategy(IContextStrategy strategy)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            _contextStrategy = strategy;
            return this;
        }

        /// <summary>
        /// Bounds tool results so one oversized return cannot fill the window.
        /// </summary>
        /// <remarks>
        /// Opt-in, as on an agent job. Each result is capped as it arrives, before it joins the
        /// transcript; <see cref="OnToolResult"/> still receives it whole.
        /// </remarks>
        public Builder WithToolOutputPolicy(ToolOutputPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            policy.Validate();
            _toolOutputPolicy = policy;
            return this;
        }

        /// <summary>
        /// Sets the clock that stamps this workflow's unknown-tool reports. Defaults to
        /// <see cref="TimeProvider.System"/>.
        /// </summary>
        public Builder WithTimeProvider(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            _timeProvider = timeProvider;
            return this;
        }

        /// <summary>Maximum number of tool-calling rounds before ending the workflow. Default is 10.</summary>
        public Builder WithMaxToolRounds(int max)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
            _maxToolRounds = max;
            return this;
        }

        /// <summary>
        /// Sets the sampling temperature for this job. Leave unset to let the provider apply its
        /// own default.
        /// </summary>
        /// <remarks>
        /// <b>Not accepted by every model.</b> Anthropic deprecated temperature for models after
        /// Claude Opus 4.6 — they accept only <c>1.0</c> and reject anything else with a 400. The
        /// value is passed through rather than clamped, so the provider's refusal is visible.
        /// </remarks>
        /// <param name="temperature">Sampling temperature; provider ranges differ.</param>
        public Builder WithTemperature(double temperature)
        {
            _temperature = temperature;
            return this;
        }

        /// <summary>Called for each text delta streamed from the model.</summary>
        public Builder OnTextDelta(Func<string, Task> handler)
        {
            _onTextDelta = handler;
            return this;
        }

        /// <summary>Called for each audio delta streamed from the model, with raw bytes and MIME type.</summary>
        public Builder OnAudioDelta(Func<byte[], string, Task> handler)
        {
            _onAudioDelta = handler;
            return this;
        }

        /// <summary>Called before each tool execution with the tool name and raw JSON arguments.</summary>
        public Builder OnToolCall(Func<string, string, Task> handler)
        {
            _onToolCall = handler;
            return this;
        }

        /// <summary>Called after each tool execution with the tool name and output.</summary>
        public Builder OnToolResult(Func<string, string, Task> handler)
        {
            _onToolResult = handler;
            return this;
        }

        /// <summary>
        /// Builds the configured <see cref="Workflow{TState}"/> with "agent" and "tools" jobs.
        /// The returned workflow can be further customized (checkpointing, tracing, metadata)
        /// before calling <see cref="Workflow{TState}.RunAsync"/>.
        /// </summary>
        public Workflow<StreamingChatState> Build()
        {
            var agentToolDefs = _toolKit?.Tools.Values
                .Select(t => new AgentTool(t.Name, t.Description, t.ParametersJsonSchema))
                .ToList();

            // Captured like the other builder state below. Left null unless the caller set one:
            // a chat that always answers identically is a worse chat, so this path does not adopt
            // the deterministic default a workflow job would want.
            var temperature = _temperature;

            var model = _model;
            var systemPrompt = _systemPrompt;
            var toolKit = _toolKit;
            var maxRounds = _maxToolRounds;
            var onDelta = _onTextDelta;
            var onAudioDelta = _onAudioDelta;
            var onToolCall = _onToolCall;
            var onTool = _onToolResult;
            var memory = _memory;
            var contextStrategy = _contextStrategy;
            var toolOutputPolicy = _toolOutputPolicy;
            var timeProvider = _timeProvider;
            var name = _name;

            // What the tool schemas take before any message, as an agent job counts it.
            var toolSchemaTokens = agentToolDefs is null
                ? 0
                : RequestTokenEstimator.Estimate(new AgentRequest { Messages = [], Tools = agentToolDefs });

            // Compacted against the window of the model that will answer, when it can say.
            ContextBudget BudgetFor(IReadOnlyList<AgentMessage> messages) =>
                ContextBudgets.For(model, new AgentRequest
                {
                    SystemPrompt = systemPrompt,
                    Messages = messages,
                    Tools = agentToolDefs
                }, maxContextTokens: null, toolSchemaTokens);

            // Shared by "agent" and "limit" — the only difference between them is what goes
            // into the outgoing message list before this runs.
            async Task<(AgentResponse? Completed, string FullText)> CallAsync(
                IReadOnlyList<AgentMessage> outbound, CancellationToken ct)
            {
                var request = new AgentRequest
                {
                    SystemPrompt = systemPrompt,
                    Messages = outbound,
                    Tools = agentToolDefs,
                    Temperature = temperature
                };

                var fullText = new StringBuilder();
                AgentResponse? completed = null;

                await foreach (var chunk in model.GenerateStreamAsync(request, ct))
                {
                    if (chunk.TextDelta is not null)
                    {
                        fullText.Append(chunk.TextDelta);
                        if (onDelta is not null)
                            await onDelta(chunk.TextDelta);
                    }
                    if (chunk.AudioDelta is not null && onAudioDelta is not null)
                        await onAudioDelta(chunk.AudioDelta, chunk.AudioMimeType ?? "audio/pcm");
                    if (chunk.CompletedResponse is not null)
                        completed = chunk.CompletedResponse;
                }

                // Capture token usage for budget tracking
                if (completed is not null)
                    await UsageRecording.ReportAsync(completed, ct);

                return (completed, fullText.ToString());
            }

            // Persists whatever the turn produced even when it ends unanswered at the tool-call
            // limit — the alternative is silently losing the user's message and every tool call
            // that did run. The assistant turn itself is only appended when there is one: a
            // response that still has a pending tool call has no result for it, and appending it
            // anyway would corrupt the next turn's transcript.
            async Task FinalizeAsync(StreamingChatState state)
            {
                if (state.LastResponse?.RequiresAction != true)
                {
                    var responseText = state.LastResponse?.Text ?? state.FullText;
                    if (!string.IsNullOrEmpty(responseText))
                        state.Messages.Add(AgentMessage.Assistant(responseText));
                }

                if (memory is not null && state.SessionId is not null
                    && state.HistoryBaseline < state.Messages.Count)
                {
                    var newMessages = state.Messages.Skip(state.HistoryBaseline)
                        .Where(m => !state.Synthetic.Contains(m))
                        .ToList();
                    await memory.AddAsync(state.SessionId, newMessages, CancellationToken.None);
                }
            }

            return new Workflow<StreamingChatState>(_name)
                .Job("agent", async (state, ct) =>
                {
                    // Load conversation history from memory on the first agent round
                    if (memory is not null && state.SessionId is not null && state.ToolRounds == 0)
                    {
                        var history = await memory.GetHistoryAsync(state.SessionId, ct);
                        if (history.Count > 0)
                        {
                            var merged = new List<AgentMessage>(history.Count + state.Messages.Count);
                            merged.AddRange(history);
                            merged.AddRange(state.Messages);
                            state = state with { Messages = merged, HistoryBaseline = history.Count };
                        }
                    }

                    var outbound = contextStrategy is not null
                        ? (await contextStrategy.ApplyAsync(
                            state.Messages, systemPrompt, BudgetFor(state.Messages), ct)).Messages
                        : state.Messages;

                    var (completed, fullText) = await CallAsync(outbound, ct);

                    return state with
                    {
                        LastResponse = completed,
                        FullText = fullText
                    };
                })
                .Job("limit", async (state, ct) =>
                {
                    // One last chance instead of silently dropping the turn: every pending call
                    // gets a stub result so the transcript stays valid, then one more call with
                    // the same tools, asked not to use them.
                    state.Messages.Add(AgentMessage.Assistant(
                        state.LastResponse!.Text ?? string.Empty, state.LastResponse.ToolCalls));

                    foreach (var call in state.LastResponse.ToolCalls!)
                        state.Messages.Add(AgentMessage.ToolResult(
                            call.Id, "Not executed: the tool-call limit for this turn was reached."));

                    AddSynthetic(state, AgentMessage.User(
                        "You have reached the tool-call limit. Answer now with what you have; do not call tools."));

                    var outbound = contextStrategy is not null
                        ? (await contextStrategy.ApplyAsync(
                            state.Messages, systemPrompt, BudgetFor(state.Messages), ct)).Messages
                        : state.Messages;

                    var (completed, fullText) = await CallAsync(outbound, ct);

                    // Streamed as well as set, since a client that renders deltas shows only those.
                    if (completed?.RequiresAction == true)
                    {
                        fullText = "I could not finish within the tool-call limit.";
                        if (onDelta is not null)
                            await onDelta(fullText);
                    }

                    return state with
                    {
                        LastResponse = completed,
                        FullText = fullText,
                        ToolRounds = state.ToolRounds + 1
                    };
                })
                .Job("tools", async (state, ct) =>
                {
                    state.Messages.Add(AgentMessage.Assistant(
                        state.LastResponse!.Text ?? string.Empty,
                        state.LastResponse.ToolCalls));

                    var hasNonRetryable = false;

                    foreach (var call in state.LastResponse.ToolCalls!)
                    {
                        if (onToolCall is not null)
                            await onToolCall(call.FunctionName, call.Arguments);

                        ToolResult toolResult;
                        if (!ToolDispatch.TryParseArguments(call.Arguments, call.FunctionName, out var args, out var parseError))
                            toolResult = parseError;
                        else if (toolKit!.Tools.TryGetValue(call.FunctionName, out var executor))
                            toolResult = await ToolDispatch.InvokeAsync(executor, args, ct);
                        else
                            toolResult = await UnknownTools.ReportAsync(
                                toolKit?.HallucinationObserver, call.FunctionName, toolKit?.Name, name,
                                episodeId: string.Empty, timeProvider, ct);

                        if (onTool is not null)
                            await onTool(call.FunctionName, toolResult.Value);

                        if (toolResult.IsError && !toolResult.IsRetryable)
                            hasNonRetryable = true;

                        var carried = toolResult.Value;
                        if (toolOutputPolicy is not null)
                        {
                            var (capped, record) = await ToolOutputHygiene.CapAsync(
                                toolOutputPolicy, call.FunctionName, toolResult.Value, ct);
                            carried = capped;

                            if (record is not null)
                                await ContextObserving.ReportAsync(record, ct);
                        }

                        state.Messages.Add(AgentMessage.ToolResult(call.Id, carried));
                    }

                    if (hasNonRetryable)
                    {
                        AddSynthetic(state, AgentMessage.User(
                            "One or more tools returned a permanent error that will not succeed on retry. " +
                            "Do not call those tools again. Proceed with your best answer using any information you already have."));
                    }

                    return state with
                    {
                        LastResponse = null,
                        FullText = string.Empty,
                        ToolRounds = state.ToolRounds + 1
                    };
                })
                .Then("agent", Workflow.Decide<StreamingChatState>(state =>
                    state.LastResponse?.RequiresAction != true
                        ? Workflow.End
                        : state.ToolRounds < maxRounds ? "tools" : "limit"))
                .Then("tools", "agent")
                .Then("limit", Workflow.End)
                // An exit hook runs after every round of its job. "agent" ends the turn only when it
                // has answered; asking for a tool, it goes on to "tools" or "limit", and "limit"
                // always ends it.
                .OnExit("agent", state => state.LastResponse?.RequiresAction == true
                    ? Task.CompletedTask
                    : FinalizeAsync(state))
                .OnExit("limit", FinalizeAsync);
        }

        /// <summary>
        /// Builds and runs the workflow in one step.
        /// Shorthand for <c>Build().RunAsync(new StreamingChatState { Messages = messages }, ct)</c>.
        /// </summary>
        public async Task<WorkflowExecution<StreamingChatState>> RunAsync(
            List<AgentMessage> messages,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            var workflow = Build();
            return await workflow.RunAsync(new StreamingChatState { Messages = messages }, ct);
        }

        /// <summary>
        /// Builds and runs the workflow with conversation memory.
        /// Prior history is loaded from <paramref name="sessionId"/> and new messages are persisted after completion.
        /// </summary>
        public async Task<WorkflowExecution<StreamingChatState>> RunAsync(
            string sessionId,
            List<AgentMessage> messages,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentNullException.ThrowIfNull(messages);
            var workflow = Build();
            return await workflow.RunAsync(
                new StreamingChatState { Messages = messages, SessionId = sessionId }, ct);
        }

        /// <summary>
        /// Builds the workflow and streams <see cref="ChatSessionEvent"/> instances as they occur.
        /// No handle, no interrupt channel — just an event stream. Interrupts are the caller's
        /// concern (e.g. via a state machine that cancels the <paramref name="ct"/>).
        /// </summary>
        /// <example>
        /// <code>
        /// await foreach (var evt in StreamingChatWorkflow.Create("chat", model)
        ///     .WithTools(tools)
        ///     .BuildStream([AgentMessage.User("hello")], ct))
        /// {
        ///     switch (evt) { case TextDeltaEvent d: Console.Write(d.Text); break; ... }
        /// }
        /// </code>
        /// </example>
        public async IAsyncEnumerable<ChatSessionEvent> BuildStream(
            List<AgentMessage> messages,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(messages);

            var channel = Channel.CreateUnbounded<ChatSessionEvent>();
            var writer = channel.Writer;

            // Wire callbacks to write events into the channel
            var origDelta = _onTextDelta;
            var origAudioDelta = _onAudioDelta;
            var origToolCall = _onToolCall;
            var origToolResult = _onToolResult;

            _onTextDelta = async delta =>
            {
                await writer.WriteAsync(new TextDeltaEvent(delta), ct);
                if (origDelta is not null) await origDelta(delta);
            };
            _onAudioDelta = async (data, mimeType) =>
            {
                await writer.WriteAsync(new AudioDeltaEvent(data, mimeType), ct);
                if (origAudioDelta is not null) await origAudioDelta(data, mimeType);
            };
            _onToolCall = async (name, args) =>
            {
                await writer.WriteAsync(new ToolCallEvent(name, args), ct);
                if (origToolCall is not null) await origToolCall(name, args);
            };
            _onToolResult = async (name, result) =>
            {
                await writer.WriteAsync(new ToolResultEvent(name, result), ct);
                if (origToolResult is not null) await origToolResult(name, result);
            };

            var workflow = Build();
            var initialState = new StreamingChatState { Messages = messages };

            // Restore original callbacks so builder is reusable
            _onTextDelta = origDelta;
            _onAudioDelta = origAudioDelta;
            _onToolCall = origToolCall;
            _onToolResult = origToolResult;

            var runTask = Task.Run(async () =>
            {
                try
                {
                    var execution = await workflow.RunAsync(initialState, ct);
                    if (execution.Result?.Success == true)
                        await writer.WriteAsync(new CompletedEvent(execution.State.FullText), CancellationToken.None);
                    else
                        await writer.WriteAsync(
                            new ErrorEvent(execution.Result?.Error ?? "Workflow failed"), CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    // Cancelled by the caller (e.g. SM interrupt) — not an error
                }
                catch (Exception ex)
                {
                    // Best-effort: if the writer itself is faulted (e.g. already completed), there is
                    // no channel left to report to, so give up quietly rather than fault this task.
                    try { await writer.WriteAsync(new ErrorEvent(ex.Message), CancellationToken.None); } catch (Exception) { }
                }
                finally
                {
                    writer.TryComplete();
                }
            }, CancellationToken.None);

            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                yield return evt;
            }

            // Observe the background task to surface exceptions
            await runTask;
        }
    }

    /// <summary>Adds one of the workflow's own prompts: sent to the model, kept out of memory.</summary>
    private static void AddSynthetic(StreamingChatState state, AgentMessage prompt)
    {
        state.Synthetic.Add(prompt);
        state.Messages.Add(prompt);
    }
}
