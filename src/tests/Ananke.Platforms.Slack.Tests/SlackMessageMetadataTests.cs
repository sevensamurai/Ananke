using Ananke.Platforms.Slack;
using Newtonsoft.Json.Linq;
using Shouldly;
using SlackNet;
using SlackNet.Blocks;
using SlackNet.Events;
using SlackNet.WebApi;

namespace Ananke.Platforms.Slack.Tests;

/// <summary>
/// Verifies that <see cref="SlackResponseSink.SendBlocksWithMetadataAsync"/> attaches
/// a <c>MessageMetadata</c> payload with the correct event type and key/value pairs.
/// </summary>
[TestFixture]
public sealed class SlackMessageMetadataTests
{
    [Test]
    public async Task SendBlocksWithMetadataAsync_AttachesMetadataWithAnankeEventType()
    {
        var fake = new FakeChatApi();
        var sink = new SlackResponseSink(fake, new SlackAdapterOptions { BotToken = "xoxb-test" });

        await sink.SendBlocksWithMetadataAsync(
            "C001", null, "fallback text", [],
            new Dictionary<string, string>
            {
                ["cell-id"] = "cell-42",
                ["generation"] = "7"
            });

        fake.LastPostedMessage.ShouldNotBeNull();
        var meta = fake.LastPostedMessage!.MetadataJson;
        meta.ShouldNotBeNull();
        meta!.EventType.ShouldBe("ananke_message");
        var payload = (JObject)meta.EventPayload!;
        payload.ShouldNotBeNull();
        payload["cell-id"]!.ToString().ShouldBe("cell-42");
        payload["generation"]!.ToString().ShouldBe("7");
    }

    [Test]
    public async Task SendBlocksWithMetadataAsync_PassesBlocksAndTextThrough()
    {
        var fake = new FakeChatApi();
        var sink = new SlackResponseSink(fake, new SlackAdapterOptions { BotToken = "xoxb-test" });
        var blocks = new List<Block> { new SectionBlock { Text = new Markdown("hello") } };

        await sink.SendBlocksWithMetadataAsync("C002", "ts.001", "hello", blocks,
            new Dictionary<string, string> { ["k"] = "v" });

        var msg = fake.LastPostedMessage!;
        msg.Channel.ShouldBe("C002");
        msg.ThreadTs.ShouldBe("ts.001");
        msg.Text.ShouldBe("hello");
        msg.Blocks.ShouldNotBeNull();
        msg.Blocks!.Count.ShouldBe(1);
    }

    [Test]
    public async Task SendBlocksAsync_DoesNotAttachMetadata()
    {
        var fake = new FakeChatApi();
        var sink = new SlackResponseSink(fake, new SlackAdapterOptions { BotToken = "xoxb-test" });

        await sink.SendBlocksAsync("C003", null, "plain", []);

        fake.LastPostedMessage.ShouldNotBeNull();
        fake.LastPostedMessage!.MetadataJson.ShouldBeNull();
    }

    // ── Fake ────────────────────────────────────────────────────────────────

    private sealed class FakeChatApi : IChatApi
    {
        public Message? LastPostedMessage { get; private set; }

        public Task<PostMessageResponse> PostMessage(Message message,
            CancellationToken cancellationToken = default)
        {
            LastPostedMessage = message;
            return Task.FromResult(new PostMessageResponse { Ts = "ts.fake" });
        }

        public Task<MessageTsResponse> Delete(string ts, string channelId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MessageTsResponse());

        public Task<MessageTsResponse> MeMessage(string channel, string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MessageTsResponse());

        public Task<ScheduleMessageResponse> ScheduleMessage(Message message, DateTime postAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScheduleMessageResponse());

        public Task DeleteScheduledMessage(string messageId, string channelId,
            bool? asUser = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PostEphemeralResponse> PostEphemeral(string userId, Message message,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PostEphemeralResponse());

        public Task Unfurl(string channelId, string ts,
            IDictionary<string, Attachment> unfurls = null!,
            bool userAuthRequired = false,
            IEnumerable<Block> userAuthBlocks = null!,
            string userAuthMessage = null!,
            string userAuthUrl = null!,
            UnfurlMetadata metadata = null!,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Unfurl(LinkSource source, string unfurlId,
            IDictionary<string, Attachment> unfurls = null!,
            bool userAuthRequired = false,
            IEnumerable<Block> userAuthBlocks = null!,
            string userAuthMessage = null!,
            string userAuthUrl = null!,
            UnfurlMetadata metadata = null!,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<MessageUpdateResponse> Update(MessageUpdate messageUpdate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MessageUpdateResponse());

        public Task<PermalinkResponse> GetPermalink(string channelId, string messageTs,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermalinkResponse());

        public Task<MessageTsResponse> StartStream(string channel, string threadTs,
            string markdownText = null!, string recipientUserId = null!,
            string recipientTeamId = null!,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MessageTsResponse());

        public Task<MessageTsResponse> AppendStream(string channel, string ts,
            string markdownText,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MessageTsResponse());

        public Task<PostMessageResponse> StopStream(string channel, string ts,
            string markdownText = null!, IEnumerable<Block> blocks = null!,
            object metadataObject = null!, MessageMetadata metadataJson = null!,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PostMessageResponse());
    }
}
