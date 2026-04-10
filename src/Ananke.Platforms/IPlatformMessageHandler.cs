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
}
