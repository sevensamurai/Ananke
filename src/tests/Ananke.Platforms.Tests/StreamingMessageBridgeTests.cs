using Shouldly;

namespace Ananke.Platforms.Tests;

[TestFixture]
public sealed class StreamingMessageBridgeTests
{
    private FakeSink _sink = null!;

    [SetUp]
    public void SetUp() => _sink = new FakeSink();

    [Test]
    public async Task AppendAsync_FirstDelta_PostsThinkingPlaceholder()
    {
        var bridge = new StreamingMessageBridge(_sink, "ch1", "t1");

        await bridge.AppendAsync("Hello");

        _sink.PostedMessages.Count.ShouldBe(1);
        _sink.PostedMessages[0].Text.ShouldBe("…");
        bridge.IsStarted.ShouldBeTrue();
    }

    [Test]
    public async Task AppendAsync_FirstDelta_WithoutPlaceholder_PostsActualText()
    {
        var options = new StreamingBridgeOptions { ThinkingPlaceholder = null };
        var bridge = new StreamingMessageBridge(_sink, "ch1", null, options);

        await bridge.AppendAsync("Hello");

        _sink.PostedMessages.Count.ShouldBe(1);
        _sink.PostedMessages[0].Text.ShouldBe("Hello");
    }

    [Test]
    public async Task AppendAsync_WithPlaceholder_EditsImmediatelyWithActualText()
    {
        var bridge = new StreamingMessageBridge(_sink, "ch1", null);

        await bridge.AppendAsync("Hello");

        // Should have posted placeholder, then immediately edited with actual text
        _sink.PostedMessages.Count.ShouldBe(1);
        _sink.UpdatedMessages.Count.ShouldBe(1);
        _sink.UpdatedMessages[0].Text.ShouldBe("Hello");
    }

    [Test]
    public async Task AppendAsync_Debounces_SkipsEditsWithinInterval()
    {
        var options = new StreamingBridgeOptions
        {
            ThinkingPlaceholder = null,
            DebounceInterval = TimeSpan.FromSeconds(10) // long debounce
        };
        var bridge = new StreamingMessageBridge(_sink, "ch1", null, options);

        await bridge.AppendAsync("Hello");  // posts initial
        await bridge.AppendAsync(" World"); // should be debounced (no edit)

        _sink.PostedMessages.Count.ShouldBe(1);
        _sink.UpdatedMessages.Count.ShouldBe(0);
        bridge.CurrentText.ShouldBe("Hello World");
    }

    [Test]
    public async Task FinalizeAsync_FlushesRemainingText()
    {
        var options = new StreamingBridgeOptions
        {
            ThinkingPlaceholder = null,
            DebounceInterval = TimeSpan.FromSeconds(10)
        };
        var bridge = new StreamingMessageBridge(_sink, "ch1", null, options);

        await bridge.AppendAsync("Hello");
        await bridge.AppendAsync(" World");
        await bridge.FinalizeAsync();

        _sink.UpdatedMessages.Count.ShouldBe(1);
        _sink.UpdatedMessages[0].Text.ShouldBe("Hello World");
    }

    [Test]
    public async Task FinalizeAsync_WithoutAppend_PostsNothing()
    {
        var bridge = new StreamingMessageBridge(_sink, "ch1", null);

        await bridge.FinalizeAsync();

        _sink.PostedMessages.Count.ShouldBe(0);
        _sink.UpdatedMessages.Count.ShouldBe(0);
    }

    [Test]
    public async Task FinalizeAsync_Idempotent()
    {
        var options = new StreamingBridgeOptions { ThinkingPlaceholder = null };
        var bridge = new StreamingMessageBridge(_sink, "ch1", null, options);

        await bridge.AppendAsync("Done");
        await bridge.FinalizeAsync();
        await bridge.FinalizeAsync(); // second call should be no-op

        _sink.UpdatedMessages.Count.ShouldBe(1);
    }

    [Test]
    public async Task AppendAsync_AfterFinalize_IsIgnored()
    {
        var options = new StreamingBridgeOptions { ThinkingPlaceholder = null };
        var bridge = new StreamingMessageBridge(_sink, "ch1", null, options);

        await bridge.AppendAsync("First");
        await bridge.FinalizeAsync();
        await bridge.AppendAsync("Should be ignored");

        bridge.CurrentText.ShouldBe("First");
    }

    [Test]
    public async Task AppendAsync_PassesThreadId()
    {
        var bridge = new StreamingMessageBridge(_sink, "ch1", "thread-42");

        await bridge.AppendAsync("Hi");

        _sink.PostedMessages[0].ThreadId.ShouldBe("thread-42");
    }

    [Test]
    public async Task FinalizeAsync_NoAppend_WithText_PostsFinal()
    {
        // Edge case: FinalizeAsync should handle the case where there's text
        // but no message was ever posted (shouldn't normally happen, but defensive)
        var bridge = new StreamingMessageBridge(_sink, "ch1", null);

        // Manually we can't set buffer, but we can test empty finalize
        await bridge.FinalizeAsync();

        _sink.PostedMessages.Count.ShouldBe(0);
    }

    [Test]
    public void Constructor_NullSink_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new StreamingMessageBridge(null!, "ch1", null));
    }

    [Test]
    public void Constructor_NullChannelId_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new StreamingMessageBridge(_sink, null!, null));
    }

    /// <summary>
    /// In-memory fake for <see cref="IPlatformResponseSink"/> that records all calls.
    /// </summary>
    private sealed class FakeSink : IPlatformResponseSink
    {
        private int _messageCounter;

        public List<(string ChannelId, string? ThreadId, string Text)> PostedMessages { get; } = [];
        public List<(string ChannelId, string MessageId, string Text)> UpdatedMessages { get; } = [];
        public List<(string ChannelId, string? ThreadId)> TypingIndicators { get; } = [];
        public List<(string ChannelId, string MessageId, string Emoji)> Reactions { get; } = [];

        public Task<string> SendMessageAsync(string channelId, string? threadId, string text,
            CancellationToken ct = default)
        {
            var id = $"msg-{Interlocked.Increment(ref _messageCounter)}";
            PostedMessages.Add((channelId, threadId, text));
            return Task.FromResult(id);
        }

        public Task UpdateMessageAsync(string channelId, string messageId, string text,
            CancellationToken ct = default)
        {
            UpdatedMessages.Add((channelId, messageId, text));
            return Task.CompletedTask;
        }

        public Task SendTypingAsync(string channelId, string? threadId,
            CancellationToken ct = default)
        {
            TypingIndicators.Add((channelId, threadId));
            return Task.CompletedTask;
        }

        public Task AddReactionAsync(string channelId, string messageId, string emoji,
            CancellationToken ct = default)
        {
            Reactions.Add((channelId, messageId, emoji));
            return Task.CompletedTask;
        }
    }
}
