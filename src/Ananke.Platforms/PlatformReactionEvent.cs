namespace Ananke.Platforms;

/// <summary>
/// A reaction event from a messaging platform, normalized to Ananke's platform model.
/// </summary>
public sealed record PlatformReactionEvent
{
    /// <summary>Platform user identifier of the actor who added or removed the reaction.</summary>
    public required string UserId { get; init; }

    /// <summary>Platform-specific channel or conversation identifier.</summary>
    public required string ChannelId { get; init; }

    /// <summary>Platform-specific identifier for the message that received the reaction.</summary>
    public required string MessageTs { get; init; }

    /// <summary>The platform-native reaction name or emoji identifier.</summary>
    public required string Reaction { get; init; }

    /// <summary><see langword="true"/> when the reaction was added; otherwise removed.</summary>
    public required bool Added { get; init; }
}
