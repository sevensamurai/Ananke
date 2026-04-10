namespace Ananke.Platforms.Sessions;

/// <summary>
/// Derives collision-free session keys from <see cref="PlatformMessage"/> properties.
/// The key incorporates platform, channel, and thread identifiers so that
/// conversations on different platforms or in different channels never share
/// an <see cref="Ananke.Abstractions.Memory.IConversationMemory"/> session.
/// </summary>
/// <remarks>
/// Default format: <c>{platformName}:{channelId}:{threadId}</c>.
/// When <c>ThreadId</c> is <see langword="null"/>, <c>ChannelId</c> is used as
/// the conversation scope (top-level channel messages share history).
/// </remarks>
public static class SessionKeyBuilder
{
    /// <summary>
    /// Builds a session key from the given platform message.
    /// </summary>
    /// <param name="message">The incoming platform message.</param>
    /// <param name="platformName">
    /// Optional platform identifier (e.g. <c>"slack"</c>, <c>"discord"</c>).
    /// When provided, prevents key collisions across platforms that happen to
    /// share channel identifiers.
    /// </param>
    /// <returns>A colon-separated session key suitable for <c>IConversationMemory</c>.</returns>
    public static string Build(PlatformMessage message, string? platformName = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        var conversation = message.ThreadId ?? message.ChannelId;

        return platformName is not null
            ? $"{platformName}:{message.ChannelId}:{conversation}"
            : $"{message.ChannelId}:{conversation}";
    }

    /// <summary>
    /// Builds a per-user session key from the given platform message.
    /// Unlike <see cref="Build"/>, this scopes the session to the individual user
    /// rather than the thread, so each user has isolated conversation history
    /// even in the same channel.
    /// </summary>
    /// <param name="message">The incoming platform message.</param>
    /// <param name="platformName">Optional platform identifier.</param>
    /// <returns>A colon-separated user-scoped session key.</returns>
    public static string BuildPerUser(PlatformMessage message, string? platformName = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        return platformName is not null
            ? $"{platformName}:{message.ChannelId}:{message.UserId}"
            : $"{message.ChannelId}:{message.UserId}";
    }
}
