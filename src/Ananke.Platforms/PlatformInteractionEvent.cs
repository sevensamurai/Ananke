namespace Ananke.Platforms;

/// <summary>
/// The kind of platform interaction event, distinguishing block actions from view submissions.
/// </summary>
public enum PlatformInteractionKind
{
    /// <summary>A user clicked a button or interacted with a block element.</summary>
    BlockAction,

    /// <summary>A user submitted a modal view.</summary>
    ViewSubmission,

    /// <summary>A user closed / dismissed a modal view.</summary>
    ViewClosed
}

/// <summary>
/// An interactivity event (block action or view submission) from a messaging platform,
/// normalized to Ananke's platform model.
/// </summary>
public sealed record PlatformInteractionEvent
{
    /// <summary>The kind of interaction that occurred.</summary>
    public required PlatformInteractionKind Kind { get; init; }

    /// <summary>
    /// The action identifier of the element that was interacted with.
    /// <see langword="null"/> for view submissions.
    /// </summary>
    public string? ActionId { get; init; }

    /// <summary>
    /// The string value carried by the block action element (button value, select option, etc.).
    /// <see langword="null"/> when not applicable.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>Platform user identifier of the acting user.</summary>
    public required string UserId { get; init; }

    /// <summary>Platform-specific channel or conversation identifier. May be <see langword="null"/> for global shortcuts.</summary>
    public string? ChannelId { get; init; }

    /// <summary>Thread identifier of the message that contained the interactive element, if any.</summary>
    public string? ThreadId { get; init; }

    /// <summary>
    /// Short-lived trigger identifier, usable to open a follow-up modal.
    /// </summary>
    public string? TriggerId { get; init; }

    /// <summary>
    /// Platform-native interaction payload for advanced scenarios.
    /// Cast to the platform-specific type in the handler when needed.
    /// </summary>
    public object? PlatformContext { get; init; }
}
