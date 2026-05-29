namespace Ananke.Platforms.Review;

/// <summary>
/// Transport-neutral helper that posts a "please review" notification for a work item
/// to a configured channel/thread via an <see cref="IPlatformResponseSink"/>.
/// </summary>
/// <remarks>
/// Platform-specific rendering (e.g. Slack Block Kit approval buttons) remains the
/// responsibility of the concrete <see cref="IPlatformResponseSink"/> implementation or
/// a platform-specific decorator layered on top of this notifier.
/// </remarks>
public sealed class WorkItemReviewNotifier(IPlatformResponseSink sink)
{
    /// <summary>
    /// Posts a review notification for the given work item to <paramref name="channelId"/>.
    /// </summary>
    /// <param name="workItemId">Stable identifier of the work item.</param>
    /// <param name="title">Short title shown to reviewers.</param>
    /// <param name="kind">Category of work under review (e.g. "Patch", "Document").</param>
    /// <param name="payload">Primary review payload such as diff text, markdown, or a summary.</param>
    /// <param name="channelId">
    /// Destination channel. When <see langword="null"/> or empty the method returns immediately
    /// without posting, allowing callers to opt out by leaving the channel unconfigured.
    /// </param>
    /// <param name="threadId">Optional thread to post into.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The platform-specific message identifier of the posted notification, or
    /// <see langword="null"/> when <paramref name="channelId"/> is not configured.
    /// </returns>
    public async Task<string?> NotifyAsync(
        string workItemId,
        string title,
        string kind,
        string payload,
        string? channelId,
        string? threadId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(channelId))
            return null;

        var text = FormatMessage(workItemId, title, kind, payload);
        return await sink.SendMessageAsync(channelId, threadId, text, ct).ConfigureAwait(false);
    }

    /// <summary>Formats the notification text for the given work item fields.</summary>
    public static string FormatMessage(string workItemId, string title, string kind, string payload)
    {
        var preview = payload.Length <= 200 ? payload : string.Concat(payload.AsSpan(0, 197), "...");
        return $"[Review requested] {kind}: {title} (id: {workItemId})\n{preview}";
    }
}
