using Ananke.Platforms;

namespace Ananke.Platforms.Slack;

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
    /// Streaming bridge options controlling debounce interval and thinking placeholder.
    /// </summary>
    public StreamingBridgeOptions StreamingOptions { get; set; } = new()
    {
        DebounceInterval = TimeSpan.FromMilliseconds(300)
    };
}
