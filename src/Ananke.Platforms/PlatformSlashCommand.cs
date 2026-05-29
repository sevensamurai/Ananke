namespace Ananke.Platforms;

/// <summary>
/// A slash-command invocation from a messaging platform, normalized to Ananke's platform model.
/// </summary>
public sealed record PlatformSlashCommand
{
    /// <summary>The slash command name, including the leading slash (e.g. <c>/studio</c>).</summary>
    public required string Command { get; init; }

    /// <summary>Text following the command name, trimmed. May be empty.</summary>
    public required string Text { get; init; }

    /// <summary>Platform user identifier of the invoking user.</summary>
    public required string UserId { get; init; }

    /// <summary>Platform-specific channel or conversation identifier.</summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// Short-lived trigger identifier used to open modals in response to this invocation.
    /// </summary>
    public string? TriggerId { get; init; }

    /// <summary>
    /// Platform-native command object for advanced scenarios.
    /// Cast to the platform-specific type in the handler when needed.
    /// </summary>
    public object? PlatformContext { get; init; }
}
