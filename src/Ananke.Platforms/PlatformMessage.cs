using Ananke.Abstractions.Agents;

namespace Ananke.Platforms;

/// <summary>
/// An incoming message from a messaging platform, normalized to
/// Ananke's conversation model.
/// </summary>
public sealed record PlatformMessage
{
    /// <summary>Platform-specific channel or conversation identifier.</summary>
    public required string ChannelId { get; init; }

    /// <summary>Thread identifier for threaded replies (<see langword="null"/> for top-level messages).</summary>
    public string? ThreadId { get; init; }

    /// <summary>Platform user identifier of the sender.</summary>
    public required string UserId { get; init; }

    /// <summary>Display name of the sender (when available).</summary>
    public string? UserName { get; init; }

    /// <summary>The user's message content, mapped to Ananke's <see cref="AgentMessage"/>.</summary>
    public required AgentMessage Message { get; init; }

    /// <summary>
    /// Platform-specific message identifier (e.g. Slack <c>ts</c>, Discord message snowflake).
    /// Useful for reactions and reply targeting.
    /// </summary>
    public string? PlatformMessageId { get; init; }

    /// <summary>
    /// Platform-native message object for advanced scenarios
    /// (slash command metadata, interaction payloads, etc.).
    /// Cast to the platform-specific type in the handler when needed.
    /// </summary>
    public object? PlatformContext { get; init; }
}
