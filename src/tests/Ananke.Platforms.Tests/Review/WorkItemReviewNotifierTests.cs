using Shouldly;
using Ananke.Platforms.Review;

namespace Ananke.Platforms.Tests.Review;

[TestFixture]
public sealed class WorkItemReviewNotifierTests
{
    private FakeSink _sink = null!;
    private WorkItemReviewNotifier _notifier = null!;

    [SetUp]
    public void SetUp()
    {
        _sink = new FakeSink();
        _notifier = new WorkItemReviewNotifier(_sink);
    }

    [Test]
    public async Task NotifyAsync_PostsMessageToSink()
    {
        var msgId = await _notifier.NotifyAsync(
            workItemId: "wi-1",
            title: "Add login endpoint",
            kind: "Patch",
            payload: "diff --git a/...",
            channelId: "C123",
            threadId: "T456");

        msgId.ShouldNotBeNullOrEmpty();
        _sink.PostedMessages.Count.ShouldBe(1);
        var posted = _sink.PostedMessages[0];
        posted.ChannelId.ShouldBe("C123");
        posted.ThreadId.ShouldBe("T456");
        posted.Text.ShouldContain("wi-1");
        posted.Text.ShouldContain("Add login endpoint");
        posted.Text.ShouldContain("Patch");
    }

    [Test]
    public async Task NotifyAsync_NullChannel_DoesNotPost()
    {
        var msgId = await _notifier.NotifyAsync(
            workItemId: "wi-2",
            title: "Fix typo",
            kind: "Document",
            payload: "some payload",
            channelId: null);

        msgId.ShouldBeNull();
        _sink.PostedMessages.ShouldBeEmpty();
    }

    [Test]
    public async Task NotifyAsync_EmptyChannel_DoesNotPost()
    {
        var msgId = await _notifier.NotifyAsync(
            workItemId: "wi-3",
            title: "Fix typo",
            kind: "Document",
            payload: "some payload",
            channelId: "");

        msgId.ShouldBeNull();
        _sink.PostedMessages.ShouldBeEmpty();
    }

    [Test]
    public void FormatMessage_LongPayload_IsTruncated()
    {
        var longPayload = new string('x', 300);
        var msg = WorkItemReviewNotifier.FormatMessage("wi-4", "Title", "Patch", longPayload);

        msg.ShouldContain("...");
        // preview portion should be 200 chars (197 + "...")
        var previewLine = msg.Split('\n')[1];
        previewLine.Length.ShouldBe(200);
    }

    [Test]
    public void FormatMessage_ShortPayload_IsNotTruncated()
    {
        var msg = WorkItemReviewNotifier.FormatMessage("wi-5", "Title", "Patch", "short payload");
        msg.ShouldContain("short payload");
        msg.ShouldNotContain("...");
    }

    private sealed class FakeSink : IPlatformResponseSink
    {
        private int _counter;

        public List<(string ChannelId, string? ThreadId, string Text)> PostedMessages { get; } = [];

        public Task<string> SendMessageAsync(string channelId, string? threadId, string text, CancellationToken ct = default)
        {
            PostedMessages.Add((channelId, threadId, text));
            return Task.FromResult($"msg-{++_counter}");
        }

        public Task UpdateMessageAsync(string channelId, string messageId, string text, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendTypingAsync(string channelId, string? threadId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task AddReactionAsync(string channelId, string messageId, string emoji, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
