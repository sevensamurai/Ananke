using System.Net;
using Ananke.Orchestration.Workflows;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

public record TextAgentState
{
    public string Input { get; init; } = "";
    public string Output { get; init; } = "";
}

[TestFixture]
public class TextAgentJobTests
{
    [Test]
    public async Task TextAgentJob_ReturnsPlainText()
    {
        var model = SimulatedModel.Fixed("Hello from the LLM!");

        var agent = AgentJobFactory.Create<TextAgentState>("greet", model)
            .WithSystemPrompt("You greet.")
            .WithPrompt(s => s.Input)
            .MapResult((s, text) => s with { Output = text })
            .Build();

        var exec = await new Workflow<TextAgentState>("text-agent")
            .Job("greet", agent)
            .Then("greet", Workflow.End)
            .RunAsync(new TextAgentState { Input = "Hi" });

        exec.Status.ShouldBe(ExecutionStatus.Completed);
        exec.State.Output.ShouldBe("Hello from the LLM!");
    }

    [Test]
    public async Task TextAgentJob_WithTools_ReturnsText()
    {
        var model = SimulatedModel.Sequence(
            new AgentResponse
            {
                Text = "thinking...",
                ToolCalls = [new AgentToolCall("call_1", "get_time", "{}")]
            },
            new AgentResponse { Text = "The current time is 12:00 PM." }
        );

        var tools = new ToolKit("time")
            .AddTool("get_time", "Gets the current time",
                () => ToolResult.Ok("12:00 PM"));

        var agent = AgentJobFactory.Create<TextAgentState>("time-check", model)
            .WithPrompt(s => s.Input)
            .WithTools(tools)
            .MapResult((s, text) => s with { Output = text })
            .Build();

        var exec = await new Workflow<TextAgentState>("tools-text")
            .Job("time-check", agent)
            .Then("time-check", Workflow.End)
            .RunAsync(new TextAgentState { Input = "What time is it?" });

        exec.Status.ShouldBe(ExecutionStatus.Completed);
        exec.State.Output.ShouldBe("The current time is 12:00 PM.");
    }

    // ── Malformed tool-call JSON self-correction (F-4) ────────────

    [Test]
    public async Task TextAgentJob_MalformedToolCallJson_SelfCorrectsAndCompletes()
    {
        var model = SimulatedModel.Sequence(
            new AgentResponse
            {
                Text = "thinking...",
                ToolCalls = [new AgentToolCall("call_1", "get_time", "{\"a\":")] // malformed JSON
            },
            new AgentResponse { Text = "Recovered after invalid JSON." }
        );

        var tools = new ToolKit("time")
            .AddTool("get_time", "Gets the current time",
                () => ToolResult.Ok("12:00 PM"));

        var agent = AgentJobFactory.Create<TextAgentState>("malformed-json", model)
            .WithPrompt(s => s.Input)
            .WithTools(tools)
            .MapResult((s, text) => s with { Output = text })
            .Build();

        var exec = await new Workflow<TextAgentState>("malformed-json")
            .Job("malformed-json", agent)
            .Then("malformed-json", Workflow.End)
            .RunAsync(new TextAgentState { Input = "What time is it?" });

        exec.Status.ShouldBe(ExecutionStatus.Completed);
        exec.State.Output.ShouldBe("Recovered after invalid JSON.");
    }

    [Test]
    public void TextAgentJob_MissingPrompt_ThrowsOnBuild()
    {
        var model = SimulatedModel.Fixed("text");

        Should.Throw<ArgumentNullException>(() =>
            AgentJobFactory.Create<TextAgentState>("bad", model)
                .MapResult((s, text) => s with { Output = text })
                .Build());
    }

    [Test]
    public void TextAgentJob_MissingMapResult_ThrowsOnBuild()
    {
        var model = SimulatedModel.Fixed("text");

        Should.Throw<ArgumentNullException>(() =>
            AgentJobFactory.Create<TextAgentState>("bad", model)
                .WithPrompt(s => s.Input)
                .Build());
    }

    [Test]
    public async Task TextAgentJob_OnResponse_InvokedWithText()
    {
        var captured = "";
        var model = SimulatedModel.Fixed("response text");

        var agent = AgentJobFactory.Create<TextAgentState>("on-resp", model)
            .WithPrompt(s => s.Input)
            .MapResult((s, text) => s with { Output = text })
            .OnResponse((s, text) => captured = text)
            .Build();

        await new Workflow<TextAgentState>("on-response")
            .Job("on-resp", agent)
            .Then("on-resp", Workflow.End)
            .RunAsync(new TextAgentState { Input = "test" });

        captured.ShouldBe("response text");
    }

    // ── Retry predicate (F-3) ─────────────────────────────────────

    [Test]
    public async Task ExecuteAsync_GuardrailException_NotRetried()
    {
        var model = ThrowingModel.Always(
            new GuardrailException("no-pii", new AgentResponse { Text = "blocked" }));

        var agent = AgentJobFactory.Create<TextAgentState>("guarded", model)
            .WithPrompt(s => s.Input)
            .MapResult((s, text) => s with { Output = text })
            .WithRetry(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(1))
            .Build();

        await Should.ThrowAsync<GuardrailException>(
            () => agent.ExecuteAsync(new TextAgentState { Input = "test" }));

        model.CallCount.ShouldBe(1); // never retried, regardless of maxAttempts
    }

    [Test]
    public async Task ExecuteAsync_RateLimitException_IsRetriedUntilSuccess()
    {
        var model = ThrowingModel.FailNTimesThenSucceed(
            failCount: 2,
            () => new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests),
            successText: "recovered");

        var agent = AgentJobFactory.Create<TextAgentState>("rate-limited", model)
            .WithPrompt(s => s.Input)
            .MapResult((s, text) => s with { Output = text })
            .WithRetry(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(1))
            .Build();

        var result = await agent.ExecuteAsync(new TextAgentState { Input = "test" });

        result.Output.ShouldBe("recovered");
        model.CallCount.ShouldBe(3); // 2 failures + 1 success
    }

    [Test]
    public async Task ExecuteAsync_NonRetryableException_ThrowsImmediately()
    {
        var model = ThrowingModel.Always(
            new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized));

        var agent = AgentJobFactory.Create<TextAgentState>("unauthorized", model)
            .WithPrompt(s => s.Input)
            .MapResult((s, text) => s with { Output = text })
            .WithRetry(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(1))
            .Build();

        await Should.ThrowAsync<HttpRequestException>(
            () => agent.ExecuteAsync(new TextAgentState { Input = "test" }));

        model.CallCount.ShouldBe(1); // non-retryable — no backoff, no attempt burned
    }

    /// <summary>
    /// Fake model that throws a queued sequence of exceptions before returning a fixed response.
    /// </summary>
    private sealed class ThrowingModel : IAgentModel
    {
        private readonly Queue<Exception> _exceptions;
        private readonly AgentResponse _finalResponse;

        public int CallCount { get; private set; }

        private ThrowingModel(IEnumerable<Exception> exceptions, AgentResponse finalResponse)
        {
            _exceptions = new Queue<Exception>(exceptions);
            _finalResponse = finalResponse;
        }

        public static ThrowingModel Always(Exception ex) =>
            new(Enumerable.Repeat(ex, 1_000), new AgentResponse { Text = "unreachable" });

        public static ThrowingModel FailNTimesThenSucceed(
            int failCount, Func<Exception> exceptionFactory, string successText) =>
            new(Enumerable.Range(0, failCount).Select(_ => exceptionFactory()),
                new AgentResponse { Text = successText });

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            CallCount++;
            if (_exceptions.Count > 0)
                throw _exceptions.Dequeue();
            return Task.FromResult(_finalResponse);
        }
    }

    private sealed class SimulatedModel : IAgentModel
    {
        private readonly Queue<AgentResponse> _responses;

        private SimulatedModel(IEnumerable<AgentResponse> responses) =>
            _responses = new Queue<AgentResponse>(responses);

        public static SimulatedModel Fixed(string text) =>
            new([new AgentResponse { Text = text }]);

        public static SimulatedModel Sequence(params AgentResponse[] responses) =>
            new(responses);

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(_responses.Count > 1 ? _responses.Dequeue() : _responses.Peek());
    }
}
