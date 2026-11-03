using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tools;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class StreamingChatWorkflowTests
{
    [Test]
    public async Task WithToolOutputPolicy_AnOversizedResult_IsCappedBeforeTheNextCall()
    {
        var big = new string('x', 10_000);
        string? seenByCallback = null;

        var kit = new ToolKit("k").AddTool("dump", "dump", () => ToolResult.Ok(big));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "dump", "{}"),
            ScriptedAgentModel.Say("done"));

        var execution = await StreamingChatWorkflow.Create("chat", model)
            .WithTools(kit)
            .WithToolOutputPolicy(new ToolOutputPolicy { MaxResultChars = 500, PreviewChars = 100 })
            .OnToolResult((_, result) =>
            {
                seenByCallback = result;
                return Task.CompletedTask;
            })
            .Build()
            .RunAsync(new StreamingChatState { Messages = [AgentMessage.User("go")] })
            .WaitAsync(TimeSpan.FromSeconds(10));

        execution.Status.ShouldBe(ExecutionStatus.Completed);

        var carried = model.Requests[1].Messages.Single(m => m.ToolCallId == "c1").Content.ShouldNotBeNull();
        carried.Length.ShouldBeLessThan(500);
        carried.ShouldContain("[tool output truncated");
        seenByCallback.ShouldBe(big, "the callback is told what the tool returned");
    }

    [Test]
    public async Task WithContextStrategy_AModelThatKnowsItsWindow_CompactsAgainstIt()
    {
        var strategy = new RecordingStrategy();
        var model = new ScriptedAgentModel(ScriptedAgentModel.Say("done")).WithContextWindow("probe", 8_000);

        await StreamingChatWorkflow.Create("chat", model)
            .WithContextStrategy(strategy)
            .Build()
            .RunAsync(new StreamingChatState { Messages = [AgentMessage.User("go")] })
            .WaitAsync(TimeSpan.FromSeconds(10));

        var budget = strategy.Budgets.ShouldHaveSingleItem();
        budget.ModelContextTokens.ShouldBe(8_000);
        budget.ModelName.ShouldBe("probe");
    }

    [Test]
    public async Task Build_ACallToAToolThatDoesNotExist_IsReportedToTheKitsObserver()
    {
        var observer = new RecordingObserver();
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"))
            .WithHallucinationObserver(observer);
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "no_such_tool", "{}"),
            ScriptedAgentModel.Say("done"));

        await StreamingChatWorkflow.Create("chat", model)
            .WithTools(kit)
            .Build()
            .RunAsync(new StreamingChatState { Messages = [AgentMessage.User("go")] })
            .WaitAsync(TimeSpan.FromSeconds(10));

        var reported = observer.Events.ShouldHaveSingleItem();
        reported.RequestedToolName.ShouldBe("no_such_tool");
        reported.RequestedKitName.ShouldBe("k");
        model.Requests[1].Messages.Single(m => m.ToolCallId == "c1").Content
            .ShouldNotBeNull().ShouldContain("Unknown tool 'no_such_tool'");
    }

    [Test]
    public async Task WithTimeProvider_AnUnknownToolReport_IsStampedByThatClock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
        var observer = new RecordingObserver();
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"))
            .WithHallucinationObserver(observer);
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "no_such_tool", "{}"),
            ScriptedAgentModel.Say("done"));

        await StreamingChatWorkflow.Create("chat", model)
            .WithTools(kit)
            .WithTimeProvider(clock)
            .Build()
            .RunAsync(new StreamingChatState { Messages = [AgentMessage.User("go")] })
            .WaitAsync(TimeSpan.FromSeconds(10));

        observer.Events.ShouldHaveSingleItem().OccurredAt.ShouldBe(clock.GetUtcNow());
    }

    private sealed class RecordingStrategy : IContextStrategy
    {
        public List<ContextBudget> Budgets { get; } = [];

        public Task<ContextProjection> ApplyAsync(
            IReadOnlyList<AgentMessage> messages, string? systemPrompt, ContextBudget budget, CancellationToken ct = default)
        {
            Budgets.Add(budget);
            return Task.FromResult(new ContextProjection { Messages = messages });
        }
    }

    private sealed class RecordingObserver : IHallucinationObserver
    {
        public List<HallucinatedToolCallEvent> Events { get; } = [];

        public ValueTask ReportAsync(HallucinatedToolCallEvent @event, CancellationToken ct = default)
        {
            Events.Add(@event);
            return ValueTask.CompletedTask;
        }
    }
}
