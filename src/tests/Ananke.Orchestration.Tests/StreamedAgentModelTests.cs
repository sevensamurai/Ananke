using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A model asked for its answer as a stream and handed back whole: the client's timeout applies to each
/// read rather than to the whole answer, and a reply that repeats itself is stopped as it arrives.
/// </summary>
[TestFixture]
public class StreamedAgentModelTests
{
    private static readonly AgentRequest Request = new() { Messages = [AgentMessage.User("Decide.")] };

    [Test]
    public async Task GenerateAsync_OfAStreamingModel_AsksForTheStream_AndReturnsTheCompletedResponse()
    {
        var inner = new Streams(["Re", "use"], new AgentResponse { Text = "Reuse", Usage = new TokenUsage { InputTokens = 3, OutputTokens = 2 } });

        var response = await inner.Streamed().GenerateAsync(Request).WaitAsync(TimeSpan.FromSeconds(10));

        response.Text.ShouldBe("Reuse");
        response.Usage.ShouldNotBeNull().OutputTokens.ShouldBe(2);
        inner.Streamed.ShouldBe(1);
    }

    [Test]
    public async Task GenerateAsync_AStreamWithNoCompletedResponse_IsTheTextItCarried()
    {
        var inner = new Streams(["Re", "use"], completed: null);

        var response = await inner.Streamed().GenerateAsync(Request).WaitAsync(TimeSpan.FromSeconds(10));

        response.Text.ShouldBe("Reuse");
    }

    [Test]
    public async Task GenerateAsync_AStreamWithNoCompletedResponse_WhenToolsWereOffered_Throws()
    {
        // Calls arrive only on the completed response: without one, a reply that called a tool would
        // read as a final answer.
        var inner = new Streams(["Let me look."], completed: null);
        var request = Request with { Tools = [new AgentTool("look", "Looks.", """{"type":"object"}""")] };

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => inner.Streamed().GenerateAsync(request).WaitAsync(TimeSpan.FromSeconds(10)));

        thrown.Message.ShouldContain("completed response");
    }

    [Test]
    public async Task GenerateAsync_OfAModelThatCannotStream_AsksItDirectly()
    {
        var inner = new Answers("Direct.");

        var response = await inner.Streamed().GenerateAsync(Request).WaitAsync(TimeSpan.FromSeconds(10));

        response.Text.ShouldBe("Direct.");
    }

    [Test]
    public async Task GenerateAsync_AnAnswerThatRepeatsOnePassage_IsStoppedAsItArrives_WhichIsTerminal()
    {
        var inner = new Repeats("The step is written so it leaves the codebase working on its own: existing callers and "
            + "tests keep passing because the default behavior is byte-identical to today's. ", times: 2_000);

        var thrown = await Should.ThrowAsync<RepeatedOutputException>(
            () => inner.Streamed().GenerateAsync(Request).WaitAsync(TimeSpan.FromSeconds(10)));

        inner.Sent.ShouldBeLessThan(20);
        TerminalProviderError.Is(thrown).ShouldBeTrue();
    }

    [Test]
    public void ResolveContextWindow_IsTheInnerModels()
    {
        var window = new ModelContextWindow("a model", 1_000);

        new Streams([], null, window).Streamed().ResolveContextWindow(Request).ShouldBe(window);
    }

    /// <summary>A streaming model that fails if asked for a whole answer.</summary>
    private sealed class Streams(string[] deltas, AgentResponse? completed, ModelContextWindow? window = null)
        : IStreamingAgentModel, IModelContextResolver
    {
        public int Streamed { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("asked for a whole answer");

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Streamed++;

            foreach (var delta in deltas)
            {
                await Task.Yield();
                yield return new AgentStreamChunk { TextDelta = delta };
            }

            if (completed is not null)
                yield return new AgentStreamChunk { CompletedResponse = completed };
        }

        public ModelContextWindow ResolveContextWindow(AgentRequest request) => window ?? ModelContextWindow.Unknown;
    }

    /// <summary>A streaming model that sends one passage over and over.</summary>
    private sealed class Repeats(string passage, int times) : IStreamingAgentModel
    {
        public int Sent { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("asked for a whole answer");

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (; Sent < times; Sent++)
            {
                await Task.Yield();
                yield return new AgentStreamChunk { TextDelta = passage };
            }

            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse { Text = string.Concat(Enumerable.Repeat(passage, times)) }
            };
        }
    }

    private sealed class Answers(string text) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = text });
    }
}
