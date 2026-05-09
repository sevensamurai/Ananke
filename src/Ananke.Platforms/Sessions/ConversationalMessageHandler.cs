using Ananke.Orchestration.Workflows;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Tools;

namespace Ananke.Platforms.Sessions;

/// <summary>
/// Abstract base class for session-aware, memory-integrated platform message handlers.
/// Encapsulates the common pattern of bridging <see cref="StreamingChatWorkflow"/> to
/// a messaging platform with conversation history, streaming, and tool execution.
/// </summary>
/// <remarks>
/// <para>
/// Subclasses provide the <see cref="IStreamingAgentModel"/> and optionally override
/// <see cref="SystemPrompt"/>, <paramref name="tools"/>, <see cref="ContextStrategy"/>,
/// and <see cref="GetSessionId"/> to customize behavior.
/// </para>
/// <para>
/// When <paramref name="memory"/> is provided, the handler automatically:
/// <list type="number">
///   <item>Derives a session key from the incoming message via <see cref="GetSessionId"/></item>
///   <item>Loads conversation history from <see cref="IConversationMemory"/></item>
///   <item>Runs <see cref="StreamingChatWorkflow"/> with the loaded history</item>
///   <item>Persists new messages back to memory after completion</item>
/// </list>
/// This mirrors the web-based <c>ChatSession</c> pattern, generalized for any platform.
/// </para>
/// </remarks>
/// <param name="model">The streaming agent model to use for LLM calls.</param>
/// <param name="memory">
/// Optional conversation memory for session persistence. When <see langword="null"/>,
/// each message is handled statelessly (no history loaded or saved).
/// </param>
/// <param name="tools">Optional tool kit for tool-calling workflows.</param>
public abstract class ConversationalMessageHandler(
    IStreamingAgentModel model,
    IConversationMemory? memory = null,
    ToolKit? tools = null) : IPlatformMessageHandler
{
    /// <summary>The system prompt sent to the model on every generation round.</summary>
    protected virtual string? SystemPrompt => null;

    /// <summary>Context compaction strategy applied before each agent generation round.</summary>
    protected virtual IContextStrategy? ContextStrategy => null;

    /// <summary>Streaming bridge options (debounce interval, thinking placeholder).</summary>
    protected virtual StreamingBridgeOptions? StreamingOptions => null;

    /// <summary>Workflow name used in traces and logs. Default: <c>"platform-chat"</c>.</summary>
    protected virtual string WorkflowName => "platform-chat";

    /// <summary>
    /// Whether to send a typing indicator before starting the workflow.
    /// Default: <see langword="true"/>.
    /// </summary>
    protected virtual bool SendTypingIndicator => true;

    /// <summary>
    /// Derives the session key used for <see cref="IConversationMemory"/> scoping.
    /// Default: <c>{channelId}:{threadId ?? channelId}</c>.
    /// Override to include platform name, guild/workspace ID, or user identity.
    /// </summary>
    /// <param name="message">The incoming platform message.</param>
    /// <returns>A session key string for conversation memory.</returns>
    protected virtual string GetSessionId(PlatformMessage message)
        => SessionKeyBuilder.Build(message);

    /// <summary>
    /// Extension point for customizing the workflow builder before execution.
    /// Called after standard configuration (system prompt, tools, memory, context strategy)
    /// has been applied. Override to add additional callbacks, metadata, or configuration.
    /// </summary>
    /// <param name="builder">The pre-configured workflow builder.</param>
    /// <param name="message">The incoming platform message.</param>
    /// <returns>The (optionally modified) builder.</returns>
    protected virtual StreamingChatWorkflow.Builder ConfigureWorkflow(
        StreamingChatWorkflow.Builder builder, PlatformMessage message) => builder;

    /// <inheritdoc />
    public virtual async Task HandleAsync(
        PlatformMessage message,
        IPlatformResponseSink sink,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sink);

        if (SendTypingIndicator)
            await sink.SendTypingAsync(message.ChannelId, message.ThreadId, ct).ConfigureAwait(false);

        var bridge = new StreamingMessageBridge(sink, message.ChannelId, message.ThreadId, StreamingOptions);

        var builder = StreamingChatWorkflow.Create(WorkflowName, model)
            .OnTextDelta(async delta => await bridge.AppendAsync(delta, ct))
            .OnToolResult(async (name, result) =>
                await sink.SendMessageAsync(
                    message.ChannelId, message.ThreadId,
                    $"🔧 `{name}` → {result}", ct));

        if (SystemPrompt is not null)
            builder = builder.WithSystemPrompt(SystemPrompt);

        if (tools is not null)
            builder = builder.WithTools(tools);

        if (ContextStrategy is not null)
            builder = builder.WithContextStrategy(ContextStrategy);

        if (memory is not null)
            builder = builder.WithMemory(memory);

        builder = ConfigureWorkflow(builder, message);

        var sessionId = GetSessionId(message);
        try
        {
            await builder.RunAsync(sessionId, [message.Message], ct).ConfigureAwait(false);
        }
        finally
        {
            // 5.2: Always finalize the bridge so the platform receives the last partial
            // chunk even when the workflow throws or is cancelled.
            await bridge.FinalizeAsync(ct).ConfigureAwait(false);
        }
    }
}
