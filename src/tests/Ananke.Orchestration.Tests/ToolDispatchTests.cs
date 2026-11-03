using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Pins <see cref="ToolDispatch"/>'s own contract: a tool call's exceptions never escape it,
/// except those that exist to unwind a run rather than report a tool failure.
/// </summary>
[TestFixture]
public class ToolDispatchTests
{
    [Test]
    public async Task ToolDispatch_GuardrailException_Propagates()
    {
        var tool = new ToolDefinition
        {
            Name = "guarded",
            Description = "d",
            Parameters = [],
            Execute = (_, _) => throw new GuardrailException("rule", new AgentResponse())
        };

        await Should.ThrowAsync<GuardrailException>(
            () => ToolDispatch.InvokeAsync(tool, new Dictionary<string, object?>(), CancellationToken.None));
    }

    [Test]
    public async Task ToolDispatch_Cancellation_Propagates()
    {
        var tool = new ToolDefinition
        {
            Name = "slow",
            Description = "d",
            Parameters = [],
            Execute = (_, ct) => throw new OperationCanceledException(ct)
        };

        await Should.ThrowAsync<OperationCanceledException>(
            () => ToolDispatch.InvokeAsync(tool, new Dictionary<string, object?>(), CancellationToken.None));
    }

    [Test]
    public async Task ToolDispatch_ACallTimeoutFromAModelTheToolCalls_Propagates()
    {
        // A tool that runs a job on a time-limited model: the call ran out on every attempt, which
        // ends the run wherever it happens — reported as a tool failure, the model would call it again.
        var tool = Throwing(new CallTimeoutException("The model had not answered after 5 minutes, on 2 attempts."));

        await Should.ThrowAsync<CallTimeoutException>(
            () => ToolDispatch.InvokeAsync(tool, new Dictionary<string, object?>(), CancellationToken.None));
    }

    [Test]
    public async Task ToolDispatch_ARepeatingReplyWrappedByTheTool_Propagates()
    {
        var tool = Throwing(new InvalidOperationException(
            "The research job failed.", new RepeatedOutputException("The reply repeats one passage.")));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => ToolDispatch.InvokeAsync(tool, new Dictionary<string, object?>(), CancellationToken.None));

        thrown.InnerException.ShouldBeOfType<RepeatedOutputException>();
    }

    [Test]
    public async Task ToolDispatch_AnErrorThatOnlyReadsLikeADepletedAccount_IsAToolFailure()
    {
        // A third-party service the tool calls is not the model provider: its quota is the tool's
        // failure to report, not a reason to end the run.
        var tool = Throwing(new HttpRequestException("You exceeded your current quota, please check your plan."));

        var result = await ToolDispatch.InvokeAsync(tool, new Dictionary<string, object?>(), CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.Value.ShouldContain("exceeded your current quota");
    }

    private static ToolDefinition Throwing(Exception exception) => new()
    {
        Name = "research",
        Description = "d",
        Parameters = [],
        Execute = (_, _) => throw exception
    };
}

/// <summary>
/// The tool loop shared by <see cref="AgentJob{TState, TResponse}"/> and
/// <see cref="TextAgentJob{TState}"/>: a tool that throws, or is missing an argument, or is
/// called with malformed arguments, must reach the model as a retryable error rather than
/// failing the job outright.
/// </summary>
[TestFixture]
public class AgentJobToolDispatchTests
{
    private sealed record Turn(string Input, string? Output = null);

    [Test]
    public async Task AgentJob_ToolMissingRequiredArgument_ModelSeesErrorAndJobCompletes()
    {
        var kit = new ToolKit("k").AddTool(
            "lookup", "look up a city", (string city) => ToolResult.Ok($"weather in {city}"),
            "city", "city name");
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "lookup", "{}"),
            ScriptedAgentModel.Say("done"));

        var job = AgentJobFactory.Create<Turn>("tools", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var result = await job.ExecuteAsync(new Turn("weather?"));

        result.Output.ShouldBe("done");
        var toolResult = model.Requests[1].Messages.Last(m => m.Role == AgentRole.Tool);
        toolResult.Content!.ShouldContain("Missing required tool argument");
    }

    [Test]
    public async Task AgentJob_ToolThrows_ModelSeesErrorAndJobCompletes()
    {
        var kit = new ToolKit("k").AddTool(
            "fetch", "fetch a page",
            (Func<string, ToolResult>)(_ => throw new HttpRequestException("503 from upstream")),
            "url", "url");
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "fetch", """{"url":"http://x"}"""),
            ScriptedAgentModel.Say("done"));

        var job = AgentJobFactory.Create<Turn>("tools", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var result = await job.ExecuteAsync(new Turn("go"));

        result.Output.ShouldBe("done");
        var toolResult = model.Requests[1].Messages.Last(m => m.Role == AgentRole.Tool);
        toolResult.Content!.ShouldContain("503 from upstream");
    }

    [Test]
    public async Task AgentJob_ToolArgumentsNotAnObject_ModelSeesErrorAndJobCompletes()
    {
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "ping", "null"),
            ScriptedAgentModel.Say("done"));

        var job = AgentJobFactory.Create<Turn>("tools", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var result = await job.ExecuteAsync(new Turn("go"));

        result.Output.ShouldBe("done");
        var toolResult = model.Requests[1].Messages.Last(m => m.Role == AgentRole.Tool);
        toolResult.Content!.ShouldContain("Invalid JSON arguments");
    }

    [Test]
    public async Task AgentJob_ToolArgumentsEmpty_TreatedAsNoArguments()
    {
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "ping", ""),
            ScriptedAgentModel.Say("done"));

        var job = AgentJobFactory.Create<Turn>("tools", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        await job.ExecuteAsync(new Turn("go"));

        var toolResult = model.Requests[1].Messages.Last(m => m.Role == AgentRole.Tool);
        toolResult.Content.ShouldBe("pong");
    }
}

/// <summary>The chat workflow's "tools" job goes through the same dispatch as the job engine.</summary>
[TestFixture]
public class StreamingChatToolDispatchTests
{
    [Test]
    public async Task StreamingChat_MalformedToolArguments_CompletesWithReply()
    {
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "ping", "{not json"),
            ScriptedAgentModel.Say("done"));

        var execution = await StreamingChatWorkflow.Create("chat", model)
            .WithTools(kit)
            .Build()
            .RunAsync(new StreamingChatState { Messages = [AgentMessage.User("hi")] });

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.State.FullText.ShouldBe("done");
    }
}
