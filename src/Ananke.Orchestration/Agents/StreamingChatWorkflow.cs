using Ananke.Orchestration.Workflows;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Ananke.Abstractions.Memory;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Tools;

using Ananke.Abstractions.Agents;

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
        private int _maxToolRounds = 10;
        private Func<string, Task>? _onTextDelta;
        private Func<byte[], string, Task>? _onAudioDelta;
        private Func<string, string, Task>? _onToolCall;
        private Func<string, string, Task>? _onToolResult;
        private IConversationMemory? _memory;
        private IContextStrategy? _contextStrategy;

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

        /// <summary>Maximum number of tool-calling rounds before ending the workflow. Default is 10.</summary>
        public Builder WithMaxToolRounds(int max)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
            _maxToolRounds = max;
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

                    var request = new AgentRequest
                    {
                        SystemPrompt = systemPrompt,
                        Messages = contextStrategy is not null
                            ? await contextStrategy.ApplyAsync(state.Messages, systemPrompt, ct)
                            : state.Messages,
                        Tools = agentToolDefs
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
                        TokenUsageCapture.Accumulate(completed);

                    return state with
                    {
                        LastResponse = completed,
                        FullText = fullText.ToString()
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

                        var args = ParseToolArgs(call.Arguments);
                        var toolResult = toolKit!.Tools.TryGetValue(call.FunctionName, out var executor)
                            ? await executor.ExecuteAsync(args, ct)
                            : ToolResult.Error($"Unknown tool: {call.FunctionName}");

                        if (onTool is not null)
                            await onTool(call.FunctionName, toolResult.Value);

                        if (toolResult.IsError && !toolResult.IsRetryable)
                            hasNonRetryable = true;

                        state.Messages.Add(AgentMessage.ToolResult(call.Id, toolResult.Value));
                    }

                    if (hasNonRetryable)
                    {
                        state.Messages.Add(AgentMessage.User(
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
                    state.LastResponse?.RequiresAction == true && state.ToolRounds < maxRounds
                        ? "tools"
                        : Workflow.End))
                .Then("tools", "agent")
                .OnExit("agent", async state =>
                {
                    // Append the final assistant response to the message list so the
                    // full turn (including tool calls and the closing reply) is preserved
                    // for subsequent turns, regardless of memory configuration.
                    if (state.LastResponse?.RequiresAction != true)
                    {
                        var responseText = state.LastResponse?.Text ?? state.FullText;
                        if (!string.IsNullOrEmpty(responseText))
                            state.Messages.Add(AgentMessage.Assistant(responseText));
                    }

                    // Persist only new messages (skip the loaded history) when the workflow completes
                    if (memory is not null && state.SessionId is not null
                        && state.LastResponse?.RequiresAction != true)
                    {
                        if (state.HistoryBaseline < state.Messages.Count)
                        {
                            var newMessages = state.Messages.Skip(state.HistoryBaseline).ToList();
                            await memory.AddAsync(state.SessionId, newMessages, CancellationToken.None);
                        }
                    }
                });
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
                    try { await writer.WriteAsync(new ErrorEvent(ex.Message), CancellationToken.None); } catch { }
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

    private static IReadOnlyDictionary<string, object?> ParseToolArgs(string arguments)
    {
        var dict = new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(arguments);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }
}
