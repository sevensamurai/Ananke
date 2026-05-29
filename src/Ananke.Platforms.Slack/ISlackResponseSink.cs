using SlackNet;
using SlackNet.Blocks;
using SlackNet.WebApi;

namespace Ananke.Platforms.Slack;

/// <summary>
/// Slack-specific response operations exposed by <see cref="SlackAdapter.ResponseSink"/>.
/// </summary>
public interface ISlackResponseSink : IPlatformResponseSink
{
    /// <summary>
    /// Posts a Block Kit message to the specified Slack channel or thread.
    /// </summary>
    Task<string> SendBlocksAsync(string channelId, string? threadId, string text,
        IReadOnlyList<Block> blocks, CancellationToken ct = default);

    /// <summary>
    /// Posts a Block Kit message with an optional traceability metadata footer
    /// (<c>metadata</c> payload on <c>chat.postMessage</c>).
    /// When <paramref name="metadata"/> is <see langword="null"/> the call is identical
    /// to <see cref="SendBlocksAsync(string,string?,string,IReadOnlyList{Block},CancellationToken)"/>.
    /// </summary>
    /// <param name="channelId">Target channel or DM id.</param>
    /// <param name="threadId">Optional thread timestamp to reply into.</param>
    /// <param name="text">Fallback plain-text summary.</param>
    /// <param name="blocks">Block Kit blocks.</param>
    /// <param name="metadata">
    /// Optional key/value pairs attached as message metadata for traceability
    /// (e.g. <c>cell-id</c>, <c>generation</c>, <c>version</c>).
    /// Maps to the Slack <c>metadata.event_payload</c> field.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> SendBlocksWithMetadataAsync(string channelId, string? threadId, string text,
        IReadOnlyList<Block> blocks, IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct = default);

    /// <summary>
    /// Posts an ephemeral message visible only to a specific user.
    /// </summary>
    Task SendEphemeralAsync(string channelId, string userId, string text,
        IReadOnlyList<Block>? blocks = null, CancellationToken ct = default);

    /// <summary>
    /// Uploads a file with Slack's external upload flow and returns the Slack file identifier.
    /// The upload strategy is governed by <see cref="SlackAdapterOptions.UploadMode"/>.
    /// </summary>
    Task<string> UploadFileAsync(string channelId, string? threadId, string fileName,
        byte[] content, string? title = null, string? initialComment = null,
        CancellationToken ct = default);

    /// <summary>
    /// Schedules a message for later delivery and returns the scheduled message identifier.
    /// </summary>
    Task<string> ScheduleMessageAsync(string channelId, string? threadId, string text,
        DateTime postAt, IReadOnlyList<Block>? blocks = null, CancellationToken ct = default);

    /// <summary>
    /// Opens a Slack modal dialog for the user identified by <paramref name="triggerId"/>.
    /// Returns the view id that can be used with <see cref="UpdateViewAsync"/>.
    /// </summary>
    /// <param name="triggerId">
    /// The trigger id obtained from a block action, slash command, or shortcut payload.
    /// Valid for three seconds after the originating event.
    /// </param>
    /// <param name="view">The <see cref="ModalViewDefinition"/> to display.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> OpenViewAsync(string triggerId, ModalViewDefinition view,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing Slack modal identified by <paramref name="viewId"/>.
    /// </summary>
    /// <param name="viewId">The view id returned by <see cref="OpenViewAsync"/>.</param>
    /// <param name="view">The replacement <see cref="ModalViewDefinition"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateViewAsync(string viewId, ModalViewDefinition view,
        CancellationToken ct = default);

    /// <summary>
    /// Sets the status indicator on an Assistant pane thread
    /// (calls <c>assistant.threads.setStatus</c>).
    /// Typically called at the start of a response to show a "thinking…" label.
    /// </summary>
    /// <param name="channelId">Channel ID containing the assistant thread.</param>
    /// <param name="threadTs">Thread timestamp (<c>thread_ts</c>) of the assistant thread.</param>
    /// <param name="status">Status label, e.g. <c>"thinking…"</c>. Pass an empty string to clear.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetAssistantStatusAsync(string channelId, string threadTs, string status,
        CancellationToken ct = default);

    /// <summary>
    /// Sets suggested prompts on an Assistant pane thread
    /// (calls <c>assistant.threads.setSuggestedPrompts</c>).
    /// </summary>
    /// <param name="channelId">Channel ID containing the assistant thread.</param>
    /// <param name="threadTs">Thread timestamp (<c>thread_ts</c>) of the assistant thread.</param>
    /// <param name="prompts">
    /// Prompts to display. Each entry is a (title, message) pair where <c>title</c> is the
    /// display label and <c>message</c> is the text pre-filled into the composer.
    /// </param>
    /// <param name="title">Optional header for the prompt list.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetSuggestedPromptsAsync(string channelId, string threadTs,
        IReadOnlyList<(string Title, string Message)> prompts,
        string? title = null, CancellationToken ct = default);
}
