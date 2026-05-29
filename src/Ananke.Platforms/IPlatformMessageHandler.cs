namespace Ananke.Platforms;

/// <summary>
/// Processes incoming platform messages by routing them to the appropriate
/// Ananke workflow. Implementations typically:
/// <list type="number">
///   <item>Derive a session key from the message's channel/thread/user identifiers</item>
///   <item>Load conversation history from <c>IConversationMemory</c></item>
///   <item>Run a <c>StreamingChatWorkflow</c> (or other orchestration pattern)</item>
///   <item>Stream responses back via the <see cref="IPlatformResponseSink"/></item>
/// </list>
/// </summary>
/// <remarks>
/// Register a concrete handler via DI:
/// <code>services.AddSingleton&lt;IPlatformMessageHandler, MyAgentHandler&gt;();</code>
/// The platform adapter (<see cref="IMessagePlatformAdapter"/>) invokes the handler
/// for each incoming user message.
/// </remarks>
public interface IPlatformMessageHandler
{
    /// <summary>Handles a single incoming message from a messaging platform.</summary>
    /// <param name="message">The normalized incoming message.</param>
    /// <param name="sink">The response sink for replying to the originating platform.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleAsync(PlatformMessage message, IPlatformResponseSink sink,
        CancellationToken ct = default);

    /// <summary>
    /// Handles a platform-native reaction event.
    /// </summary>
    /// <param name="reaction">The normalized incoming reaction event.</param>
    /// <param name="sink">The response sink for replying to the originating platform.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnReactionAsync(PlatformReactionEvent reaction, IPlatformResponseSink sink,
        CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Handles a slash-command invocation from a messaging platform.
    /// The default implementation is a no-op; override to process commands.
    /// </summary>
    /// <param name="command">The normalized slash-command payload.</param>
    /// <param name="sink">The response sink for replying to the originating platform.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnSlashCommandAsync(PlatformSlashCommand command, IPlatformResponseSink sink,
        CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Handles a block action or view submission interaction from a messaging platform.
    /// The default implementation is a no-op; override to process interactions.
    /// </summary>
    /// <param name="interaction">The normalized interaction payload.</param>
    /// <param name="sink">The response sink for replying to the originating platform.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnInteractionAsync(PlatformInteractionEvent interaction, IPlatformResponseSink sink,
        CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Handles an Assistant pane thread event (<c>assistant_thread_started</c> or
    /// <c>assistant_thread_context_changed</c>).
    /// The default implementation is a no-op; override to preload context and set
    /// suggested prompts via the platform response sink.
    /// </summary>
    /// <param name="threadEvent">The normalized Assistant thread event.</param>
    /// <param name="sink">The response sink for replying to the originating platform.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnAssistantThreadAsync(PlatformAssistantThreadEvent threadEvent,
        IPlatformResponseSink sink, CancellationToken ct = default) => Task.CompletedTask;
}
