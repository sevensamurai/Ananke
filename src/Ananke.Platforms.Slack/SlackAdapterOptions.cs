namespace Ananke.Platforms.Slack;

/// <summary>
/// Controls which Slack file-upload API is used by <see cref="ISlackResponseSink.UploadFileAsync"/>.
/// </summary>
public enum SlackUploadMode
{
    /// <summary>
    /// Uses the modern <c>files.getUploadURLExternal</c> + <c>files.completeUploadExternal</c>
    /// flow (v2). Automatically retries once if the upload URL has expired (<c>expired_url</c>).
    /// This is the default.
    /// </summary>
    ExternalUrlV2,

    /// <summary>
    /// Uses the legacy <c>files.upload</c> multipart form endpoint.
    /// Fall back to this mode when <see cref="ExternalUrlV2"/> is not available on your Slack plan.
    /// </summary>
    LegacyFilesUpload
}

/// <summary>
/// Configuration options for the Slack adapter.
/// </summary>
public sealed class SlackAdapterOptions
{
    /// <summary>
    /// Bot User OAuth Token (<c>xoxb-…</c>). Required for all operations.
    /// </summary>
    public required string BotToken { get; set; }

    /// <summary>
    /// App-Level Token (<c>xapp-…</c>). Required when <see cref="UseSocketMode"/> is <see langword="true"/>.
    /// Obtain from Slack App settings → Basic Information → App-Level Tokens.
    /// </summary>
    public string? AppToken { get; set; }

    /// <summary>
    /// When <see langword="true"/>, connects via Socket Mode (WebSocket) — no public URL needed.
    /// When <see langword="false"/>, expects Events API HTTP webhooks (requires a public endpoint).
    /// Default: <see langword="true"/>.
    /// </summary>
    public bool UseSocketMode { get; set; } = true;

    /// <summary>
    /// Signing secret for verifying Events API HTTP requests.
    /// Required when <see cref="UseSocketMode"/> is <see langword="false"/>.
    /// </summary>
    public string? SigningSecret { get; set; }

    /// <summary>
    /// When <see langword="true"/>, register Slack <c>app_mention</c> events and route them
    /// through the standard message dispatch pipeline.
    /// </summary>
    public bool EnableAppMentions { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, register Slack reaction events and route them through
    /// <see cref="IPlatformMessageHandler.OnReactionAsync(PlatformReactionEvent, IPlatformResponseSink, CancellationToken)"/>.
    /// </summary>
    public bool EnableReactions { get; set; }

    /// <summary>
    /// When <see langword="true"/>, register a Slack slash-command handler and route invocations
    /// through <see cref="IPlatformMessageHandler.OnSlashCommandAsync(PlatformSlashCommand, IPlatformResponseSink, CancellationToken)"/>.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool EnableSlashCommands { get; set; }

    /// <summary>
    /// When <see langword="true"/>, register handlers for Slack block actions and view
    /// submissions, routing them through
    /// <see cref="IPlatformMessageHandler.OnInteractionAsync(PlatformInteractionEvent, IPlatformResponseSink, CancellationToken)"/>.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool EnableInteractivity { get; set; }

    /// <summary>
    /// When <see langword="true"/>, register event handlers for Slack's Agents &amp; AI Apps
    /// Assistant pane (<c>assistant_thread_started</c> and
    /// <c>assistant_thread_context_changed</c>) and re-route
    /// <see cref="IPlatformResponseSink.SendTypingAsync"/> to
    /// <c>assistant.threads.setStatus</c> when a <c>thread_ts</c> is present.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool EnableAssistant { get; set; }

    /// <summary>
    /// Status label sent to <c>assistant.threads.setStatus</c> when
    /// <see cref="IPlatformResponseSink.SendTypingAsync"/> is called in Assistant mode.
    /// Default: <c>"thinking…"</c>.
    /// </summary>
    public string AssistantStatusLabel { get; set; } = "thinking\u2026";

    /// <summary>
    /// Controls which Slack file-upload strategy <see cref="ISlackResponseSink.UploadFileAsync"/> uses.
    /// Default: <see cref="SlackUploadMode.ExternalUrlV2"/>.
    /// </summary>
    public SlackUploadMode UploadMode { get; set; } = SlackUploadMode.ExternalUrlV2;

    /// <summary>
    /// Streaming bridge options controlling debounce interval and thinking placeholder.
    /// </summary>
    public StreamingBridgeOptions StreamingOptions { get; set; } = new()
    {
        DebounceInterval = TimeSpan.FromMilliseconds(300)
    };
}
