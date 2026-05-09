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
