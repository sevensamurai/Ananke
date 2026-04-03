using System.Runtime.CompilerServices;
using System.Text;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class BuildStreamTests
{
    [Test]
    public async Task BuildStream_EmitsTextDeltasAndCompletion()
    {
        var model = new StreamTestModel(["hello-", "world"]);

        var events = new List<ChatSessionEvent>();
        await foreach (var evt in StreamingChatWorkflow.Create("test", model)
            .BuildStream([AgentMessage.User("hi")]))
        {
            events.Add(evt);
        }

        events.OfType<TextDeltaEvent>().Select(e => e.Text)
            .ShouldBe(["hello-", "world"]);
        events.OfType<CompletedEvent>().Count().ShouldBe(1);
        events.OfType<CompletedEvent>().First().FullText.ShouldBe("hello-world");
    }

    [Test]
    public async Task BuildStream_EmitsToolCallAndToolResultEvents()
    {
        var toolCalls = new List<AgentToolCall>
        {
            new("c1", "greet", """{"name":"Ada"}""")
        };

        // First round: model requests tool call
        // Second round: model returns final text
        var roundCount = 0;
        var model = new ToolRoundModel(toolCalls, onRound: () => roundCount++);

        var tools = new Ananke.Orchestration.Tools.ToolKit("test-tools");
        tools.AddTool("greet", "Says hello",
            (string name) => Ananke.Orchestration.Tools.ToolResult.Ok($"Hello, {name}!"),
            "name", "The name to greet");

        var events = new List<ChatSessionEvent>();
        await foreach (var evt in StreamingChatWorkflow.Create("tool-test", model)
            .WithTools(tools)
            .BuildStream([AgentMessage.User("greet Ada")]))
        {
            events.Add(evt);
        }

        events.OfType<ToolCallEvent>().Count().ShouldBe(1);
        events.OfType<ToolCallEvent>().First().Name.ShouldBe("greet");
        events.OfType<ToolResultEvent>().Count().ShouldBe(1);
        events.OfType<ToolResultEvent>().First().Result.ShouldContain("Hello, Ada!");
        events.OfType<CompletedEvent>().Count().ShouldBe(1);
    }

    [Test]
    public async Task BuildStream_CancellationStopsStream()
    {
        var model = new SlowStreamTestModel(delayMs: 200, chunks: 20);
        using var cts = new CancellationTokenSource();

        var eventCount = 0;
        try
        {
            await foreach (var evt in StreamingChatWorkflow.Create("cancel-test", model)
                .BuildStream([AgentMessage.User("go")], cts.Token))
            {
                eventCount++;
                if (eventCount >= 3)
                    await cts.CancelAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        eventCount.ShouldBeGreaterThanOrEqualTo(3);
        eventCount.ShouldBeLessThan(20);
    }

    [Test]
    public async Task BuildStream_PreservesExistingCallbacks()
    {
        var model = new StreamTestModel(["a-", "b"]);
        var callbackDeltas = new List<string>();

        var events = new List<ChatSessionEvent>();
        await foreach (var evt in StreamingChatWorkflow.Create("callback-test", model)
            .OnTextDelta(async delta =>
            {
                callbackDeltas.Add(delta);
                await Task.CompletedTask;
            })
            .BuildStream([AgentMessage.User("hi")]))
        {
            events.Add(evt);
        }

        // Both the channel events and the original callback should fire
        events.OfType<TextDeltaEvent>().Count().ShouldBe(2);
        callbackDeltas.ShouldBe(["a-", "b"]);
    }

    // ── Test helpers ─────────────────────────────────────────────

    private sealed class StreamTestModel(string[] chunks) : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = string.Concat(chunks) });

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var fullText = new StringBuilder();
            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                fullText.Append(chunk);
                yield return new AgentStreamChunk { TextDelta = chunk };
            }

            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse { Text = fullText.ToString() }
            };
        }
    }

    private sealed class SlowStreamTestModel(int delayMs, int chunks) : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "done" });

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; i < chunks; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(delayMs, ct);
                yield return new AgentStreamChunk { TextDelta = $"chunk{i}-" };
            }

            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse { Text = "done" }
            };
        }
    }

    /// <summary>
    /// First round: returns tool calls. Second round: returns final text.
    /// </summary>
    private sealed class ToolRoundModel(
        IReadOnlyList<AgentToolCall> toolCalls,
        Action? onRound = null) : IStreamingAgentModel
    {
        private int _round;

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "done" });

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var round = Interlocked.Increment(ref _round);
            onRound?.Invoke();
            await Task.Yield();

            if (round == 1)
            {
                // First round: request tool calls
                yield return new AgentStreamChunk
                {
                    CompletedResponse = new AgentResponse
                    {
                        Text = null,
                        ToolCalls = toolCalls
                    }
                };
            }
            else
            {
                // Second round: return final text
                yield return new AgentStreamChunk { TextDelta = "done" };
                yield return new AgentStreamChunk
                {
                    CompletedResponse = new AgentResponse { Text = "done" }
                };
            }
        }
    }
}
