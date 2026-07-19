using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class AgentModelMiddlewareTests
{
    // ── Single middleware: request transformation ─────────────────

    [Test]
    public async Task OnBefore_TransformsRequest()
    {
        var inner = new EchoModel();
        var middleware = new PrependSystemPromptMiddleware("You are a helpful assistant.");
        var model = MiddlewareAgentModel.Wrap(inner, middleware);

        var response = await model.GenerateAsync(MakeRequest("hello"));

        // The echo model returns the system prompt as text
        response.Text.ShouldBe("You are a helpful assistant.");
    }

    [Test]
    public async Task OnAfter_TransformsResponse()
    {
        var inner = new StaticModel("raw output");
        var middleware = new UpperCaseResponseMiddleware();
        var model = MiddlewareAgentModel.Wrap(inner, middleware);

        var response = await model.GenerateAsync(MakeRequest("test"));

        response.Text.ShouldBe("RAW OUTPUT");
    }

    // ── Pipeline ordering ────────────────────────────────────────

    [Test]
    public async Task Pipeline_OnBefore_RunsFirstToLast()
    {
        var trail = new List<string>();
        var inner = new StaticModel("ok");
        var model = MiddlewareAgentModel.Wrap(inner,
            new TrailMiddleware("A", trail),
            new TrailMiddleware("B", trail),
            new TrailMiddleware("C", trail));

        await model.GenerateAsync(MakeRequest("test"));

        // OnBefore runs first-to-last
        trail.Where(t => t.StartsWith("before:")).ShouldBe(["before:A", "before:B", "before:C"]);
    }

    [Test]
    public async Task Pipeline_OnAfter_RunsLastToFirst()
    {
        var trail = new List<string>();
        var inner = new StaticModel("ok");
        var model = MiddlewareAgentModel.Wrap(inner,
            new TrailMiddleware("A", trail),
            new TrailMiddleware("B", trail),
            new TrailMiddleware("C", trail));

        await model.GenerateAsync(MakeRequest("test"));

        // OnAfter runs last-to-first (onion model)
        trail.Where(t => t.StartsWith("after:")).ShouldBe(["after:C", "after:B", "after:A"]);
    }

    [Test]
    public async Task Pipeline_ThreeMiddlewares_FullOnionOrder()
    {
        var trail = new List<string>();
        var inner = new StaticModel("ok");
        var model = MiddlewareAgentModel.Wrap(inner,
            new TrailMiddleware("1", trail),
            new TrailMiddleware("2", trail),
            new TrailMiddleware("3", trail));

        await model.GenerateAsync(MakeRequest("test"));

        trail.ShouldBe([
            "before:1", "before:2", "before:3",
            "after:3", "after:2", "after:1"
        ]);
    }

    // ── Exception propagation ────────────────────────────────────

    [Test]
    public async Task OnBefore_MiddlewareThrows_PropagatesException()
    {
        var inner = new StaticModel("ok");
        var model = MiddlewareAgentModel.Wrap(inner, new ThrowingMiddleware(throwOnBefore: true));

        await Should.ThrowAsync<InvalidOperationException>(
            () => model.GenerateAsync(MakeRequest("test")));
    }

    [Test]
    public async Task OnAfter_MiddlewareThrows_PropagatesException()
    {
        var inner = new StaticModel("ok");
        var model = MiddlewareAgentModel.Wrap(inner, new ThrowingMiddleware(throwOnAfter: true));

        await Should.ThrowAsync<InvalidOperationException>(
            () => model.GenerateAsync(MakeRequest("test")));
    }

    // ── Streaming: request transformed, response transformed ─────

    [Test]
    public async Task Streaming_OnBefore_TransformsRequest()
    {
        var inner = new EchoModel();
        var middleware = new PrependSystemPromptMiddleware("stream-prompt");
        var model = MiddlewareAgentModel.Wrap(inner, middleware);

        AgentResponse? completed = null;
        await foreach (var chunk in model.GenerateStreamAsync(MakeRequest("hello")))
        {
            if (chunk.CompletedResponse is not null)
                completed = chunk.CompletedResponse;
        }

        completed.ShouldNotBeNull();
        completed.Text.ShouldBe("stream-prompt");
    }

    [Test]
    public async Task Streaming_OnAfter_TransformsFinalResponse()
    {
        var inner = new StaticModel("streaming output");
        var middleware = new UpperCaseResponseMiddleware();
        var model = MiddlewareAgentModel.Wrap(inner, middleware);

        AgentResponse? completed = null;
        var deltas = new List<string>();
        await foreach (var chunk in model.GenerateStreamAsync(MakeRequest("test")))
        {
            if (chunk.TextDelta is not null)
                deltas.Add(chunk.TextDelta);
            if (chunk.CompletedResponse is not null)
                completed = chunk.CompletedResponse;
        }

        // Deltas pass through untransformed (streaming latency preserved)
        deltas.ShouldContain("streaming output");
        // Final response is transformed
        completed.ShouldNotBeNull();
        completed.Text.ShouldBe("STREAMING OUTPUT");
    }

    [Test]
    public async Task Streaming_ChunksPassThrough_Untransformed()
    {
        var inner = new MultiChunkModel(["alpha", "beta", "gamma"]);
        var trail = new List<string>();
        var model = MiddlewareAgentModel.Wrap(inner, new TrailMiddleware("M", trail));

        var deltas = new List<string>();
        await foreach (var chunk in model.GenerateStreamAsync(MakeRequest("test")))
        {
            if (chunk.TextDelta is not null)
                deltas.Add(chunk.TextDelta);
        }

        deltas.ShouldBe(["alpha", "beta", "gamma"]);
    }

    // ── GuardrailAgentModelMiddleware ─────────────────────────────

    [Test]
    public async Task Guardrail_RegexMatch_ThrowsGuardrailException()
    {
        var guardrail = new GuardrailAgentModelMiddleware.Builder()
            .DenyPattern("ssn", @"\b\d{3}-\d{2}-\d{4}\b")
            .Build();

        var inner = new StaticModel("Your SSN is 123-45-6789.");
        var model = MiddlewareAgentModel.Wrap(inner, guardrail);

        var ex = await Should.ThrowAsync<GuardrailException>(
            () => model.GenerateAsync(MakeRequest("test")));
        ex.RuleName.ShouldBe("ssn");
        ex.BlockedResponse.Text!.ShouldContain("123-45-6789");
    }

    [Test]
    public async Task Guardrail_RegexNoMatch_PassesThrough()
    {
        var guardrail = new GuardrailAgentModelMiddleware.Builder()
            .DenyPattern("ssn", @"\b\d{3}-\d{2}-\d{4}\b")
            .Build();

        var inner = new StaticModel("No sensitive data here.");
        var model = MiddlewareAgentModel.Wrap(inner, guardrail);

        var response = await model.GenerateAsync(MakeRequest("test"));
        response.Text.ShouldBe("No sensitive data here.");
    }

    [Test]
    public async Task Guardrail_DelegateMatch_ThrowsGuardrailException()
    {
        var guardrail = new GuardrailAgentModelMiddleware.Builder()
            .DenyWhen("empty", (r, _) => string.IsNullOrWhiteSpace(r.Text))
            .Build();

        var inner = new StaticModel("   ");
        var model = MiddlewareAgentModel.Wrap(inner, guardrail);

        var ex = await Should.ThrowAsync<GuardrailException>(
            () => model.GenerateAsync(MakeRequest("test")));
        ex.RuleName.ShouldBe("empty");
    }

    [Test]
    public void Guardrail_NoRules_ThrowsOnBuild()
    {
        Should.Throw<InvalidOperationException>(() =>
            new GuardrailAgentModelMiddleware.Builder().Build());
    }

    [Test]
    public async Task Guardrail_MultipleRules_FirstMatchWins()
    {
        var guardrail = new GuardrailAgentModelMiddleware.Builder()
            .DenyPattern("email", @"\b\S+@\S+\.\S+\b")
            .DenyPattern("ssn", @"\b\d{3}-\d{2}-\d{4}\b")
            .Build();

        var inner = new StaticModel("Contact user@test.com or use 123-45-6789.");
        var model = MiddlewareAgentModel.Wrap(inner, guardrail);

        var ex = await Should.ThrowAsync<GuardrailException>(
            () => model.GenerateAsync(MakeRequest("test")));
        ex.RuleName.ShouldBe("email"); // first rule wins
    }

    [Test]
    public async Task Guardrail_Streaming_BlocksFinalResponse()
    {
        var guardrail = new GuardrailAgentModelMiddleware.Builder()
            .DenyPattern("forbidden", "FORBIDDEN")
            .Build();

        var inner = new StaticModel("Contains FORBIDDEN content.");
        var model = MiddlewareAgentModel.Wrap(inner, guardrail);

        // Streaming should throw when the final response is evaluated
        await Should.ThrowAsync<GuardrailException>(async () =>
        {
            await foreach (var _ in model.GenerateStreamAsync(MakeRequest("test")))
            {
                // consume
            }
        });
    }

    // ── StreamingMode.Buffered (F-2) ───────────────────────────────

    [Test]
    public async Task Guardrail_Buffered_ViolatingResponse_ThrowsBeforeAnyChunkYielded()
    {
        var guardrail = new GuardrailAgentModelMiddleware.Builder()
            .DenyPattern("forbidden", "FORBIDDEN")
            .Build();

        var inner = new MultiChunkModel(["Contains ", "FORBIDDEN", " content."]);
        var model = MiddlewareAgentModel.Wrap(inner, StreamingMode.Buffered, guardrail);

        var observedChunks = 0;

        var ex = await Should.ThrowAsync<GuardrailException>(async () =>
        {
            await foreach (var _ in model.GenerateStreamAsync(MakeRequest("test")))
                observedChunks++;
        });

        ex.RuleName.ShouldBe("forbidden");
        observedChunks.ShouldBe(0); // buffered — nothing leaked before the guardrail ran
    }

    [Test]
    public async Task Guardrail_Buffered_CleanResponse_ReplaysAllChunks()
    {
        var guardrail = new GuardrailAgentModelMiddleware.Builder()
            .DenyPattern("forbidden", "FORBIDDEN")
            .Build();

        var inner = new MultiChunkModel(["Hello", " world"]);
        var model = MiddlewareAgentModel.Wrap(inner, StreamingMode.Buffered, guardrail);

        var deltas = new List<string>();
        AgentResponse? completed = null;

        await foreach (var chunk in model.GenerateStreamAsync(MakeRequest("test")))
        {
            if (chunk.TextDelta is not null)
                deltas.Add(chunk.TextDelta);
            if (chunk.CompletedResponse is not null)
                completed = chunk.CompletedResponse;
        }

        deltas.ShouldBe(["Hello", " world"]);
        completed!.Text.ShouldBe("Hello world");
    }

    // ── LoggingAgentModelMiddleware ───────────────────────────────

    [Test]
    public async Task Logging_LogsRequestAndResponse()
    {
        var logSink = new CollectingLogger();
        var middleware = new LoggingAgentModelMiddleware(logSink);
        var inner = new StaticModel("hello");
        var model = MiddlewareAgentModel.Wrap(inner, middleware);

        await model.GenerateAsync(MakeRequest("test"));

        logSink.Messages.Count.ShouldBeGreaterThanOrEqualTo(2);
        logSink.Messages.ShouldContain(m => m.Contains("LLM request"));
        logSink.Messages.ShouldContain(m => m.Contains("LLM response"));
    }

    // ── Composition with ResilientAgentModel ─────────────────────

    [Test]
    public async Task ComposedWithResilient_MiddlewareRunsForEachRetryAttempt()
    {
        var failOnce = new FailNTimesStreamingModel(failCount: 1);
        var resilient = ResilientAgentModel.Create(failOnce, maxRetryAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        var countingMiddleware = new CountingMiddleware();
        var model = MiddlewareAgentModel.Wrap(resilient, countingMiddleware);

        var response = await model.GenerateAsync(MakeRequest("test"));

        response.Text.ShouldBe("success");
        // Middleware wraps resilient — OnBefore/OnAfter run once (resilient handles retries internally)
        countingMiddleware.BeforeCount.ShouldBe(1);
        countingMiddleware.AfterCount.ShouldBe(1);
    }

    // ── Wrap non-streaming IAgentModel ───────────────────────────

    [Test]
    public async Task Wrap_NonStreamingModel_WorksForGenerate()
    {
        var inner = new NonStreamingOnlyModel("non-streaming");
        var middleware = new UpperCaseResponseMiddleware();
        var model = MiddlewareAgentModel.Wrap((IAgentModel)inner, middleware);

        var response = await model.GenerateAsync(MakeRequest("test"));

        response.Text.ShouldBe("NON-STREAMING");
    }

    [Test]
    public async Task Wrap_NonStreamingModel_AdaptsForStreaming()
    {
        var inner = new NonStreamingOnlyModel("adapted");
        var middleware = new UpperCaseResponseMiddleware();
        var model = MiddlewareAgentModel.Wrap((IAgentModel)inner, middleware);

        AgentResponse? completed = null;
        await foreach (var chunk in model.GenerateStreamAsync(MakeRequest("test")))
        {
            if (chunk.CompletedResponse is not null)
                completed = chunk.CompletedResponse;
        }

        completed.ShouldNotBeNull();
        completed.Text.ShouldBe("ADAPTED");
    }

    // ── Empty middleware pipeline ─────────────────────────────────

    [Test]
    public async Task EmptyPipeline_PassesThroughUnchanged()
    {
        var inner = new StaticModel("passthrough");
        var model = new MiddlewareAgentModel(inner, []);

        var response = await model.GenerateAsync(MakeRequest("test"));

        response.Text.ShouldBe("passthrough");
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static AgentRequest MakeRequest(string message) => new()
    {
        Messages = [AgentMessage.User(message)]
    };

    // ── Test models ─────────────────────────────────────────────

    private sealed class StaticModel(string text) : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = text });

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new AgentStreamChunk { TextDelta = text };
            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse { Text = text }
            };
        }
    }

    private sealed class EchoModel : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = request.SystemPrompt ?? request.Messages[0].Content });

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            var text = request.SystemPrompt ?? request.Messages[0].Content;
            yield return new AgentStreamChunk { TextDelta = text };
            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse { Text = text }
            };
        }
    }

    private sealed class MultiChunkModel(string[] chunks) : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = string.Concat(chunks) });

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            foreach (var chunk in chunks)
                yield return new AgentStreamChunk { TextDelta = chunk };
            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse { Text = string.Concat(chunks) }
            };
        }
    }

    private sealed class NonStreamingOnlyModel(string text) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = text });
    }

    private sealed class FailNTimesStreamingModel(int failCount) : IStreamingAgentModel
    {
        private int _callCount;

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            _callCount++;
            if (_callCount <= failCount)
                throw new System.Net.Http.HttpRequestException("rate limited", null, System.Net.HttpStatusCode.TooManyRequests);
            return Task.FromResult(new AgentResponse { Text = "success" });
        }

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            _callCount++;
            if (_callCount <= failCount)
                throw new System.Net.Http.HttpRequestException("rate limited", null, System.Net.HttpStatusCode.TooManyRequests);
            await Task.Yield();
            yield return new AgentStreamChunk { TextDelta = "success" };
            yield return new AgentStreamChunk { CompletedResponse = new AgentResponse { Text = "success" } };
        }
    }

    // ── Test middlewares ─────────────────────────────────────────

    private sealed class PrependSystemPromptMiddleware(string systemPrompt) : IAgentModelMiddleware
    {
        public Task<AgentRequest> OnBeforeGenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(request with { SystemPrompt = systemPrompt });

        public Task<AgentResponse> OnAfterGenerateAsync(AgentResponse response, AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(response);
    }

    private sealed class UpperCaseResponseMiddleware : IAgentModelMiddleware
    {
        public Task<AgentRequest> OnBeforeGenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(request);

        public Task<AgentResponse> OnAfterGenerateAsync(AgentResponse response, AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(response with { Text = response.Text?.ToUpperInvariant() });
    }

    private sealed class TrailMiddleware(string name, List<string> trail) : IAgentModelMiddleware
    {
        public Task<AgentRequest> OnBeforeGenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            trail.Add($"before:{name}");
            return Task.FromResult(request);
        }

        public Task<AgentResponse> OnAfterGenerateAsync(AgentResponse response, AgentRequest request, CancellationToken ct = default)
        {
            trail.Add($"after:{name}");
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingMiddleware(bool throwOnBefore = false, bool throwOnAfter = false)
        : IAgentModelMiddleware
    {
        public Task<AgentRequest> OnBeforeGenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            throwOnBefore
                ? throw new InvalidOperationException("Middleware before error")
                : Task.FromResult(request);

        public Task<AgentResponse> OnAfterGenerateAsync(AgentResponse response, AgentRequest request, CancellationToken ct = default) =>
            throwOnAfter
                ? throw new InvalidOperationException("Middleware after error")
                : Task.FromResult(response);
    }

    private sealed class CountingMiddleware : IAgentModelMiddleware
    {
        public int BeforeCount { get; private set; }
        public int AfterCount { get; private set; }

        public Task<AgentRequest> OnBeforeGenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            BeforeCount++;
            return Task.FromResult(request);
        }

        public Task<AgentResponse> OnAfterGenerateAsync(AgentResponse response, AgentRequest request, CancellationToken ct = default)
        {
            AfterCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
