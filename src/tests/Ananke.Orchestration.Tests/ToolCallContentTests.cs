using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Extensions;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a tool call's event says about what was asked and what came back, and who decides.
/// </summary>
/// <remarks>
/// A tool's arguments and result are whatever the model and the tool wrote, which can be anything a
/// user said or a tool read. Every consumer of a stream would receive them, so they are recorded only
/// when the workflow asks — and asking must not change what the model is sent.
/// </remarks>
[TestFixture]
public class ToolCallContentTests
{
    [Test]
    public async Task StreamAsync_ContentOff_TheEventHasNoArgumentsOrResult()
    {
        var calls = await StreamCallsAsync(Ask(new ScriptedAgentModel(OneCall(), ScriptedAgentModel.Say("done"))));

        var call = calls.ShouldHaveSingleItem();
        call.Arguments.ShouldBeNull();
        call.Result.ShouldBeNull();
        call.ResultLength.ShouldBe("found one stay".Length);
        call.IsError.ShouldBeFalse();
        call.CallId.ShouldBe("call-a");
        call.Round.ShouldBe(1);
    }

    [Test]
    public async Task StreamAsync_ContentOn_TheEventCarriesTheArgumentsAndResult()
    {
        var calls = await StreamCallsAsync(
            Ask(new ScriptedAgentModel(OneCall(), ScriptedAgentModel.Say("done"))).RecordContent(true));

        var call = calls.ShouldHaveSingleItem();
        call.Arguments.ShouldBe("""{"place":"hakone"}""");
        call.Result.ShouldBe("found one stay");
    }

    [Test]
    public async Task StreamAsync_ContentOnAndOff_TheModelIsSentTheSameRequests()
    {
        // A switch that only records must not change what the model is shown.
        var off = new ScriptedAgentModel(OneCall(), ScriptedAgentModel.Say("done"));
        var on = new ScriptedAgentModel(OneCall(), ScriptedAgentModel.Say("done"));

        await StreamCallsAsync(Ask(off));
        await StreamCallsAsync(Ask(on).RecordContent(true));

        on.Requests.Count.ShouldBe(2);
        on.Requests.Select(Sent).ShouldBe(off.Requests.Select(Sent));
    }

    [Test]
    public async Task StreamAsync_ContentOnThroughTheRegisteredRunner_TheEventCarriesTheArguments()
    {
        var services = new ServiceCollection();
        services.AddWorkflowOrchestration(o => o.RecordContent(true));
        await using var provider = services.BuildServiceProvider();

        var calls = await StreamCallsAsync(Ask(new ScriptedAgentModel(OneCall(), ScriptedAgentModel.Say("done")))
            .UseRunner(provider.GetRequiredService<IWorkflowRunner>()));

        calls.ShouldHaveSingleItem().Arguments.ShouldBe("""{"place":"hakone"}""");
    }

    [Test]
    public async Task StreamAsync_ContentOnInTheParent_ASubWorkflowsCallCarriesTheArguments()
    {
        var inner = Ask(new ScriptedAgentModel(OneCall(), ScriptedAgentModel.Say("done")));
        var outer = new Workflow<State>("outer")
            .SubFlow("nested", inner, s => s, (_, child) => child)
            .Then("nested", Workflow.End)
            .RecordContent(true);

        var calls = await StreamCallsAsync(outer);

        calls.ShouldHaveSingleItem().Arguments.ShouldBe("""{"place":"hakone"}""");
    }

    private static AgentResponse OneCall() => ScriptedAgentModel.Call("call-a", "look_up", """{"place":"hakone"}""");

    private static Workflow<State> Ask(IAgentModel model) =>
        new Workflow<State>("content")
            .Job("ask", AgentJobFactory.Create<State>("ask", model)
                .WithPrompt(_ => "find somewhere to stay")
                .WithTools(new ToolKit("k").AddTool("look_up", "Looks a place up.", tool => tool
                    .Param("place", "The place.")
                    .OnExecute(_ => ToolResult.Ok("found one stay"))))
                .MapResult((s, text) => s with { Text = text })
                .Build())
            .Then("ask", Workflow.End);

    private static async Task<List<AgentToolCalled>> StreamCallsAsync(Workflow<State> workflow)
    {
        var calls = new List<AgentToolCalled>();

        await foreach (var evt in workflow.StreamAsync(new State()).ConfigureAwait(false))
        {
            if (evt is AgentToolCalled called)
                calls.Add(called);
        }

        return calls;
    }

    /// <summary>Everything a request shows the model, as one string to compare.</summary>
    private static string Sent(AgentRequest request) =>
        string.Join("\n", [
            request.SystemPrompt ?? "",
            string.Join(",", request.Tools?.Select(t => t.Name) ?? []),
            .. request.Messages.Select(m => string.Join("|",
                m.Role, m.Content, m.ToolCallId,
                string.Join(",", m.ToolCalls?.Select(c => $"{c.Id}:{c.FunctionName}:{c.Arguments}") ?? [])))
        ]);

    private sealed record State
    {
        public string Text { get; init; } = string.Empty;
    }
}
