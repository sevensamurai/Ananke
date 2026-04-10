using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.Tools;
using Ananke.Platforms.Sessions;
using Shouldly;

namespace Ananke.Platforms.Tests;

[TestFixture]
public sealed class ConversationalMessageHandlerTests
{
    private FakeSink _sink = null!;
    private FakeStreamingModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _sink = new FakeSink();
        _model = new FakeStreamingModel();
    }

    [Test]
    public async Task HandleAsync_SendsTypingIndicator()
    {
        var handler = new TestHandler(_model);
        var message = CreateMessage();

        await handler.HandleAsync(message, _sink);

        _sink.TypingIndicators.Count.ShouldBe(1);
        _sink.TypingIndicators[0].ChannelId.ShouldBe("ch1");
    }

    [Test]
    public async Task HandleAsync_StreamsResponseViaBridge()
    {
        _model.ResponseText = "Hello world";
        var handler = new TestHandler(_model);
        var message = CreateMessage();

        await handler.HandleAsync(message, _sink);

        // Bridge should have posted and finalized a message
        _sink.PostedMessages.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task HandleAsync_WithMemory_PersistsMessages()
    {
        var memory = new InMemoryConversationMemory();
        var handler = new TestHandler(_model, memory: memory);
        var message = CreateMessage(channelId: "C1", threadId: "T1");

        await handler.HandleAsync(message, _sink);

        var sessionId = SessionKeyBuilder.Build(message);
        var history = await memory.GetHistoryAsync(sessionId);
        history.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task HandleAsync_WithMemory_LoadsHistoryOnSecondCall()
    {
        var memory = new InMemoryConversationMemory();
        var handler = new TestHandler(_model, memory: memory);
        var message = CreateMessage(channelId: "C1", threadId: "T1");

        // First call — seeds history
        await handler.HandleAsync(message, _sink);

        var sessionId = SessionKeyBuilder.Build(message);
        var historyAfterFirst = await memory.GetHistoryAsync(sessionId);

        // Second call — should load the history from first call
        await handler.HandleAsync(message, _sink);

        var historyAfterSecond = await memory.GetHistoryAsync(sessionId);
        historyAfterSecond.Count.ShouldBeGreaterThan(historyAfterFirst.Count);
    }

    [Test]
    public async Task HandleAsync_WithoutMemory_WorksStateless()
    {
        var handler = new TestHandler(_model);
        var message = CreateMessage();

        // Should not throw — just processes without persistence
        await handler.HandleAsync(message, _sink);

        _sink.PostedMessages.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task HandleAsync_UsesCustomSystemPrompt()
    {
        var handler = new PromptHandler(_model);
        var message = CreateMessage();

        await handler.HandleAsync(message, _sink);

        // The model should have received the custom system prompt
        _model.LastSystemPrompt.ShouldBe("You are a test bot.");
    }

    [Test]
    public async Task HandleAsync_UsesCustomSessionId()
    {
        var memory = new InMemoryConversationMemory();
        var handler = new CustomSessionHandler(_model, memory);
        var message = CreateMessage(userId: "U42");

        await handler.HandleAsync(message, _sink);

        // Custom handler uses user-scoped session key
        var history = await memory.GetHistoryAsync($"custom:U42");
        history.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task HandleAsync_TypingDisabled_SkipsTyping()
    {
        var handler = new NoTypingHandler(_model);
        var message = CreateMessage();

        await handler.HandleAsync(message, _sink);

        _sink.TypingIndicators.Count.ShouldBe(0);
    }

    [Test]
    public async Task HandleAsync_NullMessage_Throws()
    {
        var handler = new TestHandler(_model);

        await Should.ThrowAsync<ArgumentNullException>(
            () => handler.HandleAsync(null!, _sink));
    }

    [Test]
    public async Task HandleAsync_NullSink_Throws()
    {
        var handler = new TestHandler(_model);
        var message = CreateMessage();

        await Should.ThrowAsync<ArgumentNullException>(
            () => handler.HandleAsync(message, null!));
    }

    // ── Test handlers ─────────────────────────────────────────────

    private sealed class TestHandler(
        IStreamingAgentModel model,
        IConversationMemory? memory = null,
        ToolKit? tools = null)
        : ConversationalMessageHandler(model, memory, tools);

    private sealed class PromptHandler(IStreamingAgentModel model)
        : ConversationalMessageHandler(model)
    {
        protected override string? SystemPrompt => "You are a test bot.";
    }

    private sealed class CustomSessionHandler(
        IStreamingAgentModel model,
        IConversationMemory memory)
        : ConversationalMessageHandler(model, memory)
    {
        protected override string GetSessionId(PlatformMessage message)
            => $"custom:{message.UserId}";
    }

    private sealed class NoTypingHandler(IStreamingAgentModel model)
        : ConversationalMessageHandler(model)
    {
        protected override bool SendTypingIndicator => false;
    }

    // ── Fakes ─────────────────────────────────────────────────────

    private static PlatformMessage CreateMessage(
        string channelId = "ch1",
        string? threadId = null,
        string userId = "user1") =>
        new()
        {
            ChannelId = channelId,
            ThreadId = threadId,
            UserId = userId,
            Message = AgentMessage.User("hello")
        };

    private sealed class FakeStreamingModel : IStreamingAgentModel
    {
        public string ResponseText { get; set; } = "OK";
        public string? LastSystemPrompt { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            LastSystemPrompt = request.SystemPrompt;
            return Task.FromResult(new AgentResponse { Text = ResponseText });
        }

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            LastSystemPrompt = request.SystemPrompt;
            yield return new AgentStreamChunk { TextDelta = ResponseText };
            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse { Text = ResponseText }
            };
            await Task.CompletedTask;
        }
    }

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
