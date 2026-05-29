namespace Ananke.Platforms;

/// <summary>
/// Indicates whether an Assistant thread has just started or had its context updated.
/// </summary>
public enum AssistantThreadEventKind
{
    /// <summary>The user opened a new Assistant thread.</summary>
    Started,

    /// <summary>The context of an existing Assistant thread changed (e.g. a channel was shared).</summary>
    ContextChanged
}

/// <summary>
/// Normalized representation of a Slack Agents &amp; AI Apps Assistant pane thread event
/// (<c>assistant_thread_started</c> or <c>assistant_thread_context_changed</c>).
/// </summary>
public sealed record PlatformAssistantThreadEvent
{
    /// <summary>Whether the thread started or its context changed.</summary>
    public required AssistantThreadEventKind Kind { get; init; }

    /// <summary>Slack user ID of the person who opened or is interacting with the thread.</summary>
    public required string UserId { get; init; }

    /// <summary>Slack channel ID of the Assistant thread.</summary>
    public required string ChannelId { get; init; }

    /// <summary>Timestamp of the Assistant thread message (<c>thread_ts</c>).</summary>
    public required string ThreadId { get; init; }

    /// <summary>
    /// Optional source context provided when the context changed.
    /// Keys are Slack context field names (e.g. <c>channel_id</c>, <c>team_id</c>).
    /// <see langword="null"/> for <see cref="AssistantThreadEventKind.Started"/> events.
    /// </summary>
    public IReadOnlyDictionary<string, string>? SourceContext { get; init; }

    /// <summary>The original platform-specific payload for advanced scenarios.</summary>
    public required object PlatformContext { get; init; }
}
