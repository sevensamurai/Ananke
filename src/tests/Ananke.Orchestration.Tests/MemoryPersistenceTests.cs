using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a job writes to <see cref="Ananke.Abstractions.Memory.IConversationMemory"/> after one turn:
/// exactly the new turn, once, and none of the loop's own scaffolding.
/// </summary>
[TestFixture]
public class MemoryPersistenceTests
{
    private sealed record Turn(string Input, string? Output = null);
    private sealed record Answer(string Value);

    [Test]
    public async Task AgentJob_WithMemoryAndCompaction_PersistsOnlyTheNewTurn()
    {
        var memory = new InMemoryConversationMemory();
        await SeedHistory(memory, "s1");
        var before = (await memory.GetHistoryAsync("s1")).Count;

        var model = new ScriptedAgentModel(ScriptedAgentModel.Say("""{"Value":"reply"}"""));
        var job = AgentJobFactory.Create<Turn, Answer>("structured", model)
            .WithPrompt(s => s.Input)
            .WithMemory(memory, _ => "s1")
            .WithContextStrategy(new SlidingWindowContextStrategy(700))
            .MapResult((s, r) => s with { Output = r.Value })
            .Build();

        await job.ExecuteAsync(new Turn("new question"));

        var after = await memory.GetHistoryAsync("s1");
        after.Count.ShouldBe(before + 2);
        after.GroupBy(m => m.Content).ShouldAllBe(g => g.Count() == 1);
    }

    [Test]
    public async Task TextAgentJob_WithMemoryAndCompaction_PersistsOnlyTheNewTurn()
    {
        var memory = new InMemoryConversationMemory();
        await SeedHistory(memory, "s1");
        var before = (await memory.GetHistoryAsync("s1")).Count;

        var model = new ScriptedAgentModel(ScriptedAgentModel.Say("reply"));
        var job = AgentJobFactory.Create<Turn>("text", model)
            .WithPrompt(s => s.Input)
            .WithMemory(memory, _ => "s1")
            .WithContextStrategy(new SlidingWindowContextStrategy(700))
            .MapResult((s, r) => s with { Output = r })
            .Build();

        await job.ExecuteAsync(new Turn("new question"));

        var after = await memory.GetHistoryAsync("s1");
        after.Count.ShouldBe(before + 2);
        after.GroupBy(m => m.Content).ShouldAllBe(g => g.Count() == 1);
    }

    [Test]
    public async Task AgentJob_StructuredWithTools_DoesNotPersistCoercionPrompt()
    {
        var memory = new InMemoryConversationMemory();
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "ping", "{}"),
            ScriptedAgentModel.Say("""{"Value":"done"}"""));

        var job = AgentJobFactory.Create<Turn, Answer>("structured-tools", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WithMemory(memory, _ => "s1")
            .MapResult((s, r) => s with { Output = r.Value })
            .Build();

        await job.ExecuteAsync(new Turn("go"));

        var history = await memory.GetHistoryAsync("s1");
        history.ShouldNotBeEmpty();
        history.ShouldAllBe(m => m.Content == null || !m.Content.Contains("Based on everything above"));
    }

    [Test]
    public async Task AgentJob_PermanentToolError_DoesNotPersistNudge()
    {
        var memory = new InMemoryConversationMemory();
        var kit = new ToolKit("k").AddTool(new ToolDefinition
        {
            Name = "broken",
            Description = "always fails permanently",
            Parameters = [],
            Execute = (_, _) => Task.FromResult(ToolResult.Fatal("configuration is invalid"))
        });
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "broken", "{}"),
            ScriptedAgentModel.Say("done"));

        var job = AgentJobFactory.Create<Turn>("text-nudge", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WithMemory(memory, _ => "s1")
            .MapResult((s, r) => s with { Output = r })
            .Build();

        await job.ExecuteAsync(new Turn("go"));

        var history = await memory.GetHistoryAsync("s1");
        history.ShouldNotBeEmpty();
        history.ShouldAllBe(m => m.Content == null || !m.Content.Contains("permanent error"));
    }

    /// <summary>Six old turns, ~400 characters each — enough for a 700-token window to compact.</summary>
    private static async Task SeedHistory(InMemoryConversationMemory memory, string sessionId)
    {
        for (var i = 0; i < 6; i++)
            await memory.AddAsync(sessionId, [
                AgentMessage.User($"old question {i} " + new string('x', 400)),
                AgentMessage.Assistant($"old answer {i} " + new string('y', 400))]).ConfigureAwait(false);
    }
}
