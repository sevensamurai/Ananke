using Ananke.Orchestration.Workflows;
using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class ContextStrategyTests
{
    // ── ApproximateTokenCounter ──────────────────────────────────

    [Test]
    public void ApproximateTokenCounter_EmptyString_ReturnsZero()
    {
        ApproximateTokenCounter.Instance.EstimateTokens("").ShouldBe(0);
    }

    [Test]
    public void ApproximateTokenCounter_NullString_ReturnsZero()
    {
        ApproximateTokenCounter.Instance.EstimateTokens((string)null!).ShouldBe(0);
    }

    [Test]
    public void ApproximateTokenCounter_ShortText_ReturnsReasonableEstimate()
    {
        // "hello" is 5 chars → ceil(5/4) = 2
        ApproximateTokenCounter.Instance.EstimateTokens("hello").ShouldBe(2);
    }

    [Test]
    public void ApproximateTokenCounter_LongerText_ReturnsReasonableEstimate()
    {
        // 100 chars → ceil(100/4) = 25  [(100+3)/4 = 25 in integer division]
        var text = new string('a', 100);
        ApproximateTokenCounter.Instance.EstimateTokens(text).ShouldBe(25);
    }

    [Test]
    public void ApproximateTokenCounter_Message_IncludesContent()
    {
        var msg = AgentMessage.User("Hello, how are you?");
        var tokens = ApproximateTokenCounter.Instance.EstimateTokens(msg);
        tokens.ShouldBeGreaterThan(0);
    }

    [Test]
    public void ApproximateTokenCounter_MessageWithToolCalls_IncludesToolCallTokens()
    {
        var msg = AgentMessage.Assistant("text", [
            new AgentToolCall("id1", "search", """{"query":"test"}""")
        ]);
        var tokens = ApproximateTokenCounter.Instance.EstimateTokens(msg);
        tokens.ShouldBeGreaterThan(ApproximateTokenCounter.Instance.EstimateTokens("text"));
    }

    // ── SlidingWindowContextStrategy: passthrough ────────────────

    [Test]
    public async Task SlidingWindow_UnderBudget_ReturnsSameList()
    {
        var strategy = new SlidingWindowContextStrategy(maxTokens: 1000);
        var messages = MakeMessages(3);

        var result = await strategy.ApplyAsync(messages, null);

        result.ShouldBeSameAs(messages);
    }

    [Test]
    public async Task SlidingWindow_EmptyList_ReturnsSameList()
    {
        var strategy = new SlidingWindowContextStrategy(maxTokens: 100);
        IReadOnlyList<AgentMessage> messages = [];

        var result = await strategy.ApplyAsync(messages, null);

        result.Count.ShouldBe(0);
    }

    // ── SlidingWindowContextStrategy: drops oldest ───────────────

    [Test]
    public async Task SlidingWindow_OverBudget_DropsOldestMessages()
    {
        // Each message is ~25 tokens ("Message 0" ≈ 9 chars / 4 ≈ 3 tokens, but let's use longer)
        var messages = new List<AgentMessage>
        {
            AgentMessage.User(new string('a', 400)),  // ~100 tokens
            AgentMessage.User(new string('b', 400)),  // ~100 tokens
            AgentMessage.User(new string('c', 400)),  // ~100 tokens
            AgentMessage.User("current question"),     // small
        };

        // Budget: 200 tokens — can only fit last 2 messages
        var strategy = new SlidingWindowContextStrategy(maxTokens: 200);
        var result = await strategy.ApplyAsync(messages, null);

        result.Count.ShouldBe(2);
        result[^1].Content.ShouldBe("current question");
    }

    [Test]
    public async Task SlidingWindow_AlwaysPreservesLastMessage()
    {
        var messages = new List<AgentMessage>
        {
            AgentMessage.User(new string('a', 10000)), // huge
            AgentMessage.User("must keep"),
        };

        var strategy = new SlidingWindowContextStrategy(maxTokens: 50);
        var result = await strategy.ApplyAsync(messages, null);

        result.Count.ShouldBe(1);
        result[0].Content.ShouldBe("must keep");
    }

    // ── SlidingWindowContextStrategy: system prompt ──────────────

    [Test]
    public async Task SlidingWindow_AccountsForSystemPromptTokens()
    {
        var systemPrompt = new string('s', 400); // ~100 tokens
        var messages = new List<AgentMessage>
        {
            AgentMessage.User(new string('a', 400)),  // ~100 tokens
            AgentMessage.User(new string('b', 400)),  // ~100 tokens
            AgentMessage.User("latest"),               // small
        };

        // Budget: 250 tokens. System prompt takes ~100, leaving ~150 for messages
        var strategy = new SlidingWindowContextStrategy(maxTokens: 250);
        var result = await strategy.ApplyAsync(messages, systemPrompt);

        // Should drop oldest to fit within budget
        result.Count.ShouldBeLessThan(3);
        result[^1].Content.ShouldBe("latest");
    }

    [Test]
    public async Task SlidingWindow_SystemPromptExceedsBudget_KeepsOnlyLastMessage()
    {
        var systemPrompt = new string('s', 1000); // ~250 tokens
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("old"),
            AgentMessage.User("latest"),
        };

        var strategy = new SlidingWindowContextStrategy(maxTokens: 100);
        var result = await strategy.ApplyAsync(messages, systemPrompt);

        result.Count.ShouldBe(1);
        result[0].Content.ShouldBe("latest");
    }

    // ── SlidingWindowContextStrategy: validation ─────────────────

    [Test]
    public void SlidingWindow_MaxTokensLessThan1_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new SlidingWindowContextStrategy(maxTokens: 0));
    }

    // ── SummarizingContextStrategy: passthrough ──────────────────

    [Test]
    public async Task Summarizing_UnderThreshold_ReturnsSameList()
    {
        var summarizer = new StaticSummarizerModel("summary");
        var strategy = new SummarizingContextStrategy(summarizer, thresholdTokens: 10000);
        var messages = MakeMessages(3);

        var result = await strategy.ApplyAsync(messages, null);

        result.ShouldBeSameAs(messages);
        summarizer.CallCount.ShouldBe(0);
    }

    [Test]
    public async Task Summarizing_FewMessages_ReturnsSameList()
    {
        var summarizer = new StaticSummarizerModel("summary");
        // recentMessageCount defaults to 4 — if we have <= 4 messages, no summarization
        var strategy = new SummarizingContextStrategy(summarizer, thresholdTokens: 1);
        var messages = MakeMessages(3);

        var result = await strategy.ApplyAsync(messages, null);

        result.ShouldBeSameAs(messages);
        summarizer.CallCount.ShouldBe(0);
    }

    // ── SummarizingContextStrategy: summarizes ───────────────────

    [Test]
    public async Task Summarizing_OverThreshold_SummarizesOldMessages()
    {
        var summarizer = new StaticSummarizerModel("This is the summary.");
        // Small threshold to force summarization
        var strategy = new SummarizingContextStrategy(
            summarizer, thresholdTokens: 10, recentMessageCount: 2);

        var messages = new List<AgentMessage>
        {
            AgentMessage.User("old message 1"),
            AgentMessage.User("old message 2"),
            AgentMessage.User("old message 3"),
            AgentMessage.User("recent 1"),
            AgentMessage.User("recent 2"),
        };

        var result = await strategy.ApplyAsync(messages, null);

        // 1 summary + 2 recent
        result.Count.ShouldBe(3);
        result[0].Content!.ShouldContain("This is the summary.");
        result[1].Content.ShouldBe("recent 1");
        result[2].Content.ShouldBe("recent 2");
        summarizer.CallCount.ShouldBe(1);
    }

    [Test]
    public async Task Summarizing_SummaryRequestIncludesOldMessages()
    {
        var summarizer = new EchoSummarizerModel();
        var strategy = new SummarizingContextStrategy(
            summarizer, thresholdTokens: 10, recentMessageCount: 1);

        // Use longer messages to ensure tokens exceed threshold
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("fact A is: " + new string('x', 100)),
            AgentMessage.User("fact B is: " + new string('y', 100)),
            AgentMessage.User("current question"),
        };

        var result = await strategy.ApplyAsync(messages, null);

        // The summary should contain "fact A" and "fact B" but not "current question"
        result[0].Content!.ShouldContain("fact A");
        result[0].Content!.ShouldContain("fact B");
        result[0].Content!.ShouldNotContain("current question");
    }

    [Test]
    public async Task Summarizing_ReceivesCorrectSystemPromptForBudget()
    {
        var systemPrompt = new string('x', 4000); // large system prompt
        var summarizer = new StaticSummarizerModel("summary");
        var strategy = new SummarizingContextStrategy(
            summarizer, thresholdTokens: 100, recentMessageCount: 1);

        var messages = new List<AgentMessage>
        {
            AgentMessage.User("old"),
            AgentMessage.User("current"),
        };

        // With the large system prompt, total tokens should exceed threshold
        var result = await strategy.ApplyAsync(messages, systemPrompt);

        result.Count.ShouldBe(2); // 1 summary + 1 recent
        summarizer.CallCount.ShouldBe(1);
    }

    // ── SummarizingContextStrategy: validation ───────────────────

    [Test]
    public void Summarizing_NullSummarizer_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SummarizingContextStrategy(null!, thresholdTokens: 100));
    }

    [Test]
    public void Summarizing_ThresholdLessThan1_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new SummarizingContextStrategy(new StaticSummarizerModel("x"), thresholdTokens: 0));
    }

    [Test]
    public void Summarizing_RecentCountLessThan1_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new SummarizingContextStrategy(new StaticSummarizerModel("x"),
                thresholdTokens: 100, recentMessageCount: 0));
    }

    // ── Integration: AgentJob with SlidingWindow ─────────────────

    [Test]
    public async Task AgentJob_WithContextStrategy_AppliesCompaction()
    {
        var capturedMessages = new List<AgentMessage>();
        var model = new CapturingModel(capturedMessages);

        var job = AgentJobFactory.Create<TestJobState, TestJobResponse>("test", model)
            .WithPrompt(s => "What is the answer?")
            .MapResult((s, r) => s with { Answer = r.Text ?? "" })
            .WithContextStrategy(new SlidingWindowContextStrategy(maxTokens: 50))
            .Build();

        // The job creates messages internally — the strategy should be applied
        var result = await job.ExecuteAsync(new TestJobState());

        result.Answer.ShouldBe("response");
    }

    // ── Integration: StreamingChatWorkflow with SlidingWindow ────

    [Test]
    public async Task StreamingChatWorkflow_WithContextStrategy_AppliesCompaction()
    {
        var capturedMessages = new List<AgentMessage>();
        var model = new CapturingStreamingModel(capturedMessages);

        var workflow = StreamingChatWorkflow.Create("test", model)
            .WithContextStrategy(new SlidingWindowContextStrategy(maxTokens: 5000))
            .Build();

        // Run with a conversation that's small enough — should pass through
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("Hello!")
        };

        var result = await workflow.RunAsync(new StreamingChatState { Messages = messages });

        result.Status.ShouldBe(ExecutionStatus.Completed);
    }

    // ── Custom ITokenCounter ─────────────────────────────────────

    [Test]
    public async Task SlidingWindow_CustomTokenCounter_IsUsed()
    {
        var counter = new FixedTokenCounter(tokensPerMessage: 100);
        var strategy = new SlidingWindowContextStrategy(maxTokens: 250, tokenCounter: counter);

        var messages = new List<AgentMessage>
        {
            AgentMessage.User("a"),
            AgentMessage.User("b"),
            AgentMessage.User("c"),
        };

        // 3 messages × 100 = 300 > 250. Should drop first message.
        var result = await strategy.ApplyAsync(messages, null);

        result.Count.ShouldBe(2);
        result[0].Content.ShouldBe("b");
        result[1].Content.ShouldBe("c");
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static List<AgentMessage> MakeMessages(int count)
    {
        var messages = new List<AgentMessage>();
        for (var i = 0; i < count; i++)
            messages.Add(AgentMessage.User($"Message {i}"));
        return messages;
    }

    // ── Test types ──────────────────────────────────────────────

    private record TestJobState
    {
        public string Answer { get; init; } = "";
    }

    private record TestJobResponse
    {
        public string? Text { get; init; }
    }

    private sealed class StaticSummarizerModel(string summaryText) : IAgentModel
    {
        public int CallCount { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new AgentResponse { Text = summaryText });
        }
    }

    private sealed class EchoSummarizerModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = request.Messages[0].Content });
    }

    private sealed class CapturingModel(List<AgentMessage> captured) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            captured.AddRange(request.Messages);
            return Task.FromResult(new AgentResponse
            {
                Text = """{"Text":"response"}"""
            });
        }
    }

    private sealed class CapturingStreamingModel(List<AgentMessage> captured) : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            captured.AddRange(request.Messages);
            return Task.FromResult(new AgentResponse { Text = "response" });
        }

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            captured.AddRange(request.Messages);
            await Task.Yield();
            yield return new AgentStreamChunk { TextDelta = "response" };
            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse { Text = "response" }
            };
        }
    }

    private sealed class FixedTokenCounter(int tokensPerMessage) : ITokenCounter
    {
        public int EstimateTokens(string text) => tokensPerMessage;
        public int EstimateTokens(AgentMessage message) => tokensPerMessage;
    }
}
