using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Platforms;

/// <summary>
/// Configuration for <see cref="StreamingMessageBridge"/> debounce and rate-limit behavior.
/// </summary>
public sealed record StreamingBridgeOptions
{
    /// <summary>
    /// Minimum interval between message edits. Platforms rate-limit edit calls,
    /// so this prevents 429 responses. Default: 300 ms (safe for Slack and Discord).
    /// </summary>
    public TimeSpan DebounceInterval { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Initial message text posted before the first delta arrives.
    /// Set to <see langword="null"/> to skip the initial placeholder.
    /// </summary>
    public string? ThinkingPlaceholder { get; init; } = "…";
}

/// <summary>
/// Bridges <c>StreamingChatWorkflow.OnTextDelta</c> callbacks to a messaging platform
/// using the post-then-edit pattern with configurable debouncing.
/// <para>
/// Messaging platforms (Slack, Discord) do not support true server-push streaming into
/// a message. The standard UX pattern is: post an initial "thinking…" message, edit it
/// periodically with accumulated text, and finalize on stream completion.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var bridge = new StreamingMessageBridge(sink, channelId, threadId);
///
/// await StreamingChatWorkflow.Create("chat", model)
///     .OnTextDelta(async delta =&gt; await bridge.AppendAsync(delta))
///     .RunAsync(messages, ct);
///
/// await bridge.FinalizeAsync();
/// </code>
/// </example>
public sealed class StreamingMessageBridge(
    IPlatformResponseSink sink,
    string channelId,
    string? threadId,
    StreamingBridgeOptions? options = null,
    ILogger? logger = null)
{
    private readonly IPlatformResponseSink _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    private readonly string _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
    private readonly StreamingBridgeOptions _options = options ?? new StreamingBridgeOptions();
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly StringBuilder _buffer = new();
    private readonly object _lock = new();

    private string? _messageId;
    private DateTime _lastFlush = DateTime.MinValue;
    private bool _finalized;

    /// <summary>The accumulated text so far.</summary>
    public string CurrentText
    {
        get { lock (_lock) return _buffer.ToString(); }
    }

    /// <summary>
    /// Whether an initial message has been posted to the platform.
    /// </summary>
    public bool IsStarted => _messageId is not null;

    /// <summary>
    /// Appends a text delta from the streaming workflow.
    /// Posts the initial message on the first call, then debounces subsequent
    /// edits to respect platform rate limits.
    /// </summary>
    public async Task AppendAsync(string delta, CancellationToken ct = default)
    {
        if (_finalized)
            return;

        string currentText;
        lock (_lock)
        {
            _buffer.Append(delta);
            currentText = _buffer.ToString();
        }

        if (_messageId is null)
        {
            var initialText = _options.ThinkingPlaceholder ?? currentText;
            _messageId = await _sink.SendMessageAsync(_channelId, threadId, initialText, ct)
                .ConfigureAwait(false);
            _lastFlush = DateTime.UtcNow;
            _logger.LogDebug("StreamingBridge: posted initial message {MessageId}", _messageId);

            if (_options.ThinkingPlaceholder is not null && currentText != _options.ThinkingPlaceholder)
            {
                await FlushEditAsync(currentText, ct).ConfigureAwait(false);
            }

            return;
        }

        var elapsed = DateTime.UtcNow - _lastFlush;
        if (elapsed >= _options.DebounceInterval)
        {
            await FlushEditAsync(currentText, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Finalizes the streamed message with the complete accumulated text.
    /// Must be called after the streaming workflow completes to ensure
    /// the final text is flushed to the platform.
    /// </summary>
    public async Task FinalizeAsync(CancellationToken ct = default)
    {
        if (_finalized)
            return;

        _finalized = true;

        string finalText;
        lock (_lock)
        {
            finalText = _buffer.ToString();
        }

        if (_messageId is null)
        {
            if (finalText.Length > 0)
            {
                _messageId = await _sink.SendMessageAsync(_channelId, threadId, finalText, ct)
                    .ConfigureAwait(false);
            }

            return;
        }

        await _sink.UpdateMessageAsync(_channelId, _messageId, finalText, ct)
            .ConfigureAwait(false);
        _logger.LogDebug("StreamingBridge: finalized message {MessageId} ({Length} chars)",
            _messageId, finalText.Length);
    }

    private async Task FlushEditAsync(string text, CancellationToken ct)
    {
        await _sink.UpdateMessageAsync(_channelId, _messageId!, text, ct)
            .ConfigureAwait(false);
        _lastFlush = DateTime.UtcNow;
    }
}
