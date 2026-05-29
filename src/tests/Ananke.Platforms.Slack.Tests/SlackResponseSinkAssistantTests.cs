using Ananke.Platforms.Slack;
using Shouldly;
using SlackNet.WebApi;

namespace Ananke.Platforms.Slack.Tests;

/// <summary>
/// Verifies that <see cref="SlackResponseSink.SendTypingAsync"/> routes to
/// <c>assistant.threads.setStatus</c> when <see cref="SlackAdapterOptions.EnableAssistant"/>
/// is <see langword="true"/>, and is a no-op otherwise.
/// </summary>
[TestFixture]
public sealed class SlackResponseSinkAssistantTests
{
    [Test]
    public async Task SendTypingAsync_AssistantEnabled_WithThreadTs_CallsSetStatus()
    {
        var fake = new FakeAssistantThreadsApi();
        var options = new SlackAdapterOptions
        {
            BotToken = "xoxb-test",
            EnableAssistant = true,
            AssistantStatusLabel = "working on it\u2026"
        };
        var sink = new SlackResponseSink(fake, options);

        await sink.SendTypingAsync("C001", "12345.00001");

        fake.LastChannelId.ShouldBe("C001");
        fake.LastThreadTs.ShouldBe("12345.00001");
        fake.LastStatus.ShouldBe("working on it\u2026");
    }

    [Test]
    public async Task SendTypingAsync_AssistantEnabled_NullThreadTs_DoesNotCallSetStatus()
    {
        var fake = new FakeAssistantThreadsApi();
        var options = new SlackAdapterOptions { BotToken = "xoxb-test", EnableAssistant = true };
        var sink = new SlackResponseSink(fake, options);

        await sink.SendTypingAsync("C001", null);

        fake.LastChannelId.ShouldBeNull();
    }

    [Test]
    public async Task SendTypingAsync_AssistantDisabled_DoesNotCallSetStatus()
    {
        var fake = new FakeAssistantThreadsApi();
        // EnableAssistant defaults to false
        var sink = new SlackResponseSink(fake);

        await sink.SendTypingAsync("C001", "12345.00001");

        fake.LastChannelId.ShouldBeNull();
    }

    [Test]
    public async Task SetAssistantStatusAsync_DelegatesToSetStatus()
    {
        var fake = new FakeAssistantThreadsApi();
        var sink = new SlackResponseSink(fake);

        await sink.SetAssistantStatusAsync("C010", "99.001", "reviewing\u2026");

        fake.LastChannelId.ShouldBe("C010");
        fake.LastThreadTs.ShouldBe("99.001");
        fake.LastStatus.ShouldBe("reviewing\u2026");
    }

    [Test]
    public async Task SetSuggestedPromptsAsync_DelegatesToSetSuggestedPrompts()
    {
        var fake = new FakeAssistantThreadsApi();
        var sink = new SlackResponseSink(fake);
        var prompts = new[] { ("What can you do?", "What can you do?"), ("Help me write", "Help me write an email") };

        await sink.SetSuggestedPromptsAsync("C011", "88.001",
            prompts.Select(p => (p.Item1, p.Item2)).ToList(),
            title: "Suggested");

        fake.LastSuggestedPromptsCount.ShouldBe(2);
        fake.LastSuggestedPromptsTitle.ShouldBe("Suggested");
    }

    // ── Stub ─────────────────────────────────────────────────────────────────

    private sealed class FakeAssistantThreadsApi : IAssistantThreadsApi
    {
        public string? LastChannelId { get; private set; }
        public string? LastThreadTs { get; private set; }
        public string? LastStatus { get; private set; }
        public int LastSuggestedPromptsCount { get; private set; }
        public string? LastSuggestedPromptsTitle { get; private set; }

        public Task SetStatus(string channelId, string threadTs, string status,
            IEnumerable<string>? loadingMessages = null,
            CancellationToken cancellationToken = default)
        {
            LastChannelId = channelId;
            LastThreadTs = threadTs;
            LastStatus = status;
            return Task.CompletedTask;
        }

        public Task SetSuggestedPrompts(string channelId, string threadTs,
            IEnumerable<AssistantPrompt> prompts, string? title = null,
            CancellationToken cancellationToken = default)
        {
            LastSuggestedPromptsCount = prompts.Count();
            LastSuggestedPromptsTitle = title;
            return Task.CompletedTask;
        }

        public Task SetTitle(string channelId, string threadTs, string title,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
