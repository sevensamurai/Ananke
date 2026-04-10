namespace Ananke.Platforms;

/// <summary>
/// Callback sink for sending responses back to a messaging platform.
/// Each <see cref="IMessagePlatformAdapter"/> implementation provides a concrete sink
/// that maps Ananke responses to platform-native API calls (Slack <c>chat.postMessage</c>,
/// Discord <c>CreateMessageAsync</c>, etc.).
/// </summary>
public interface IPlatformResponseSink
{
    /// <summary>
    /// Posts a new message to the specified channel/thread and returns the
    /// platform-specific message identifier (needed for subsequent edits and reactions).
    /// </summary>
    Task<string> SendMessageAsync(string channelId, string? threadId, string text,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing message identified by <paramref name="messageId"/>.
    /// Used by <see cref="StreamingMessageBridge"/> for the post-then-edit streaming pattern.
    /// </summary>
    Task UpdateMessageAsync(string channelId, string messageId, string text,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a typing/thinking indicator to the specified channel/thread.
    /// Platforms typically auto-expire this after a few seconds.
    /// </summary>
    Task SendTypingAsync(string channelId, string? threadId,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a reaction emoji to an existing message (e.g., ✅ on tool completion, 🤔 on thinking).
    /// </summary>
    Task AddReactionAsync(string channelId, string messageId, string emoji,
        CancellationToken ct = default);
}
