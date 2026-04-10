using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class StreamingAgentModelTests
{
    [Test]
    public async Task GenerateStreamAsync_YieldsTextDeltasAndCompletedResponse()
    {
        var model = new FakeStreamingModel(["Hello", ", ", "world!"]);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in model.GenerateStreamAsync(MakeRequest()))
            chunks.Add(chunk);

        chunks.Count.ShouldBe(4); // 3 text deltas + 1 completed
        chunks[0].TextDelta.ShouldBe("Hello");
        chunks[1].TextDelta.ShouldBe(", ");
        chunks[2].TextDelta.ShouldBe("world!");
        chunks[3].CompletedResponse.ShouldNotBeNull();
        chunks[3].CompletedResponse!.Text.ShouldBe("Hello, world!");
    }

    [Test]
    public async Task GenerateStreamAsync_WithToolCalls_IncludesToolCallsInCompletedResponse()
    {
        var toolCall = new AgentToolCall("tc1", "get_weather", """{"city":"London"}""");
        var model = new FakeStreamingModel([], [toolCall]);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in model.GenerateStreamAsync(MakeRequest()))
            chunks.Add(chunk);

        chunks.Count.ShouldBe(1); // only the completed chunk
        chunks[0].CompletedResponse.ShouldNotBeNull();
        chunks[0].CompletedResponse!.ToolCalls.ShouldNotBeNull();
        chunks[0].CompletedResponse!.ToolCalls!.Count.ShouldBe(1);
        chunks[0].CompletedResponse!.ToolCalls![0].FunctionName.ShouldBe("get_weather");
    }

    [Test]
    public async Task RoutedAgentModel_StreamsFallbackForNonStreamingModel()
    {
        var nonStreamingModel = new FakeNonStreamingModel("buffered response");
        var router = new ModelRouter().Otherwise(nonStreamingModel);

        var routed = router.ToAgentModel();
        routed.ShouldBeAssignableTo<IStreamingAgentModel>();

        var streaming = (IStreamingAgentModel)routed;
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in streaming.GenerateStreamAsync(MakeRequest()))
            chunks.Add(chunk);

        chunks.Count.ShouldBe(2); // 1 text delta (full text) + 1 completed
        chunks[0].TextDelta.ShouldBe("buffered response");
        chunks[1].CompletedResponse.ShouldNotBeNull();
        chunks[1].CompletedResponse!.Text.ShouldBe("buffered response");
    }

    [Test]
    public async Task RoutedAgentModel_DelegatesToStreamingModel()
    {
        var streamingModel = new FakeStreamingModel(["a", "b"]);
        var router = new ModelRouter().Otherwise(streamingModel);

        var routed = (IStreamingAgentModel)router.ToAgentModel();
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in routed.GenerateStreamAsync(MakeRequest()))
            chunks.Add(chunk);

        chunks.Count.ShouldBe(3); // 2 text deltas + 1 completed
        chunks[0].TextDelta.ShouldBe("a");
        chunks[1].TextDelta.ShouldBe("b");
        chunks[2].CompletedResponse!.Text.ShouldBe("ab");
    }

    [Test]
    public async Task GenerateStreamAsync_EmptyResponse_YieldsOnlyCompletedChunk()
    {
        var model = new FakeStreamingModel([]);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in model.GenerateStreamAsync(MakeRequest()))
            chunks.Add(chunk);

        chunks.Count.ShouldBe(1);
        chunks[0].CompletedResponse.ShouldNotBeNull();
        chunks[0].CompletedResponse!.Text.ShouldBeNull();
    }

    [Test]
    public async Task GenerateStreamAsync_SupportsCancellation()
    {
        var model = new FakeStreamingModel(["a", "b", "c", "d"]);
        var cts = new CancellationTokenSource();

        var chunks = new List<AgentStreamChunk>();
        try
        {
            await foreach (var chunk in model.GenerateStreamAsync(MakeRequest(), cts.Token))
            {
                chunks.Add(chunk);
                if (chunks.Count == 2)
                    await cts.CancelAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected — cancellation fires on the next iteration
        }

        chunks.Count.ShouldBe(2);
    }

    private static AgentRequest MakeRequest() => new()
    {
        Messages = [AgentMessage.User("test")]
    };

    private sealed class FakeStreamingModel(
        string[] textDeltas,
        IReadOnlyList<AgentToolCall>? toolCalls = null) : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new AgentResponse
            {
                Text = string.Concat(textDeltas),
                ToolCalls = toolCalls
            });
        }

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var fullText = new System.Text.StringBuilder();

            foreach (var delta in textDeltas)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                fullText.Append(delta);
                yield return new AgentStreamChunk { TextDelta = delta };
            }

            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse
                {
                    Text = fullText.Length > 0 ? fullText.ToString() : null,
                    ToolCalls = toolCalls
                }
            };
        }
    }

    private sealed class FakeNonStreamingModel(string text) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new AgentResponse { Text = text });
        }
    }
}
