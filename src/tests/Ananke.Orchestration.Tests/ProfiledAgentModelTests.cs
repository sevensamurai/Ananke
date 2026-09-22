using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Telling a model which model it is, so everything that reads a context window has one to read.
/// </summary>
/// <remarks>
/// <para>
/// Found live: an OpenAI run's context baseline said <c>window known 0 (0.0%)</c> and <c>n/a</c> for
/// fill and truncation on every one of 35 calls. The instrument ran and measured nothing, because
/// nothing in the path could say how big the window it was filling was.
/// </para>
/// <para>
/// The window deliberately does <b>not</b> come from the adapter. One adapter serves OpenAI, Ollama,
/// vLLM and Azure deployments, so a name-keyed table inside it would answer for a local
/// <c>llama3.2</c> with OpenAI's numbers — and it would be a second place model metadata lives,
/// beside the catalogue that already holds it.
/// </para>
/// </remarks>
[TestFixture]
public class ProfiledAgentModelTests
{
    [Test]
    public void WithProfile_ACatalogueTemplate_ReportsThatModelsWindow()
    {
        var model = new EchoModel().WithProfile(ModelCatalog.OpenAI.Gpt4_1Mini);

        var window = model.ResolveContextWindow(Request());

        window.ModelName.ShouldBe(ModelCatalog.OpenAI.Gpt4_1Mini.Name);
        window.ContextTokens.ShouldBe(ModelCatalog.OpenAI.Gpt4_1Mini.MaxContextTokens);
        window.IsKnown.ShouldBeTrue();
    }

    [Test]
    public void WithContextWindow_ADeploymentThatNarrowedIt_ReportsWhatItActuallyHonours()
    {
        // The effective window, not the advertised one. A model served with 8K of context is an 8K
        // model to everything that has to fit inside it, whatever the published figure says.
        var model = new EchoModel().WithContextWindow("llama3.2:3b", 8_192);

        model.ResolveContextWindow(Request()).ContextTokens.ShouldBe(8_192);
    }

    [Test]
    public async Task Generate_IsTheInnerModelsAnswer_Unchanged()
    {
        var inner = new EchoModel();
        var model = inner.WithProfile(ModelCatalog.OpenAI.Gpt4_1Mini);

        var response = await model.GenerateAsync(Request());

        response.Text.ShouldBe("echo");
        inner.Calls.ShouldBe(1);
    }

    [Test]
    public async Task GenerateStream_ANonStreamingModel_IsBufferedIntoChunks()
    {
        // Everything downstream reads the streaming interface; a model that cannot stream still has
        // to be usable through a wrapper whose only job is to answer one extra question.
        var model = new NonStreamingEchoModel().WithProfile(ModelCatalog.OpenAI.Gpt4_1Mini);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in model.GenerateStreamAsync(Request()))
            chunks.Add(chunk);

        chunks.Count.ShouldBe(2);
        chunks[0].TextDelta.ShouldBe("echo");
        chunks[1].CompletedResponse!.Text.ShouldBe("echo");
    }

    [Test]
    public async Task Execute_ThroughTheEngine_TheBaselineKnowsTheWindow()
    {
        // The whole point: the figures the context tier reports are computed from a window somebody
        // could state, and without one the report is a row of `n/a`.
        var collector = new ContextBaselineCollector();

        var agent = AgentJobFactory.Create<string>(
                "profiled", new EchoModel().WithProfile(ModelCatalog.OpenAI.Gpt4_1Mini))
            .WithPrompt(s => s)
            .MapResult((_, text) => text)
            .Build();

        using (ContextObserving.BeginScope(collector))
            await agent.ExecuteAsync("go");

        var baseline = collector.Snapshot();

        baseline.Calls.ShouldBe(1);
        baseline.CallsWithKnownWindow.ShouldBe(1);
        baseline.ToReport().ShouldNotContain("no call knew its window");
    }

    [Test]
    public async Task Execute_ThroughTheEngine_AnUnboundModelStillSaysItDoesNotKnow()
    {
        // The other half, and it must stay true: not knowing is reported as not knowing, never as a
        // zero that reads like a measurement.
        var collector = new ContextBaselineCollector();

        var agent = AgentJobFactory.Create<string>("bare", new EchoModel())
            .WithPrompt(s => s)
            .MapResult((_, text) => text)
            .Build();

        using (ContextObserving.BeginScope(collector))
            await agent.ExecuteAsync("go");

        collector.Snapshot().CallsWithKnownWindow.ShouldBe(0);
    }

    [Test]
    public void WithContextWindow_ANegativeWindow_IsRefused() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => new EchoModel().WithContextWindow("whatever", -1));

    private static AgentRequest Request() => new() { Messages = [AgentMessage.User("go")] };

    private sealed class EchoModel : IStreamingAgentModel
    {
        public int Calls { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AgentResponse { Text = "echo" });
        }

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new AgentStreamChunk { TextDelta = "echo" };
            yield return new AgentStreamChunk { CompletedResponse = new AgentResponse { Text = "echo" } };
        }
    }

    private sealed class NonStreamingEchoModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "echo" });
    }
}
