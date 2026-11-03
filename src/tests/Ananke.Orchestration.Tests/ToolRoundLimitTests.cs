using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What happens when a run reaches its tool-round limit while the model still wants a tool:
/// one more chance to answer without calling anything, rather than an abrupt failure or a
/// silently empty turn.
/// </summary>
[TestFixture]
public class ToolRoundLimitTests
{
    private sealed record Turn(string Input, string? Output = null);

    [Test]
    public async Task AgentJob_ToolRoundLimit_FinalCallProducesAnswer()
    {
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "ping", "{}"),
            ScriptedAgentModel.Call("c2", "ping", "{}"),
            ScriptedAgentModel.Say("done"));

        var job = AgentJobFactory.Create<Turn>("limited", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WithMaxToolRounds(1)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var result = await job.ExecuteAsync(new Turn("go"));

        result.Output.ShouldBe("done");
        model.Requests.Count.ShouldBe(3); // initial + round 1 + the final chance
        var lastRequest = model.Requests[^1];
        lastRequest.Messages.ShouldContain(m =>
            m.Role == AgentRole.Tool
            && m.Content == "Not executed: the tool-call limit for this turn was reached.");
    }

    [Test]
    public async Task AgentJob_ToolRoundLimit_StillCallingTools_Throws()
    {
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(ScriptedAgentModel.Call("c1", "ping", "{}")); // never answers

        var job = AgentJobFactory.Create<Turn>("stubborn", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WithMaxToolRounds(1)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => job.ExecuteAsync(new Turn("go")));

        ex.Message.ShouldContain("exceeded the maximum");
        model.Requests.Count.ShouldBe(3); // initial + round 1 + the final chance, then give up
    }

    [Test]
    public async Task AgentJob_DefaultMaxToolRounds_IsTen()
    {
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(ScriptedAgentModel.Call("c1", "ping", "{}")); // never answers

        var job = AgentJobFactory.Create<Turn>("infinite", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        await Should.ThrowAsync<InvalidOperationException>(() => job.ExecuteAsync(new Turn("go")));

        // 1 initial call + 10 rounds (the default) + 1 final chance at the limit = 12.
        model.Requests.Count.ShouldBe(12);
    }

    [Test]
    public async Task StreamingChat_ToolRoundLimit_EndsWithReplyAndPersistsTurn()
    {
        var memory = new InMemoryConversationMemory();
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "ping", "{}"),
            ScriptedAgentModel.Call("c2", "ping", "{}"),
            ScriptedAgentModel.Say("done"));

        var execution = await StreamingChatWorkflow.Create("chat", model)
            .WithTools(kit)
            .WithMemory(memory)
            .WithMaxToolRounds(1)
            .Build()
            .RunAsync(new StreamingChatState
            {
                Messages = [AgentMessage.User("go")],
                SessionId = "s1"
            });

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.State.FullText.ShouldBe("done");

        var history = await memory.GetHistoryAsync("s1");
        history.ShouldNotBeEmpty();
        history.ShouldContain(m => m.Content == "done");
    }

    [Test]
    public async Task StreamingChat_ATurnWithToolRounds_IsStoredInMemoryOnce()
    {
        // Each round's exit is not the turn's end: stored at every round, the user's message and every
        // call before the last would be stored again, and a provider refuses a repeated call id.
        var memory = new InMemoryConversationMemory();
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "ping", "{}"),
            ScriptedAgentModel.Call("c2", "ping", "{}"),
            ScriptedAgentModel.Say("done"));

        await StreamingChatWorkflow.Create("chat", model)
            .WithTools(kit)
            .WithMemory(memory)
            .Build()
            .RunAsync(new StreamingChatState { Messages = [AgentMessage.User("go")], SessionId = "s1" });

        var history = await memory.GetHistoryAsync("s1");
        history.Select(m => m.Role).ShouldBe(
        [
            AgentRole.User,
            AgentRole.Assistant, AgentRole.Tool,
            AgentRole.Assistant, AgentRole.Tool,
            AgentRole.Assistant
        ]);
    }

    [Test]
    public async Task StreamingChat_ToolRoundLimit_ThePromptToAnswerIsNotStoredInMemory()
    {
        // The workflow's own prompt, not something the user said: the next turn's history must not
        // carry it, as an agent job's does not.
        var memory = new InMemoryConversationMemory();
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "ping", "{}"),
            ScriptedAgentModel.Call("c2", "ping", "{}"),
            ScriptedAgentModel.Say("done"));

        await StreamingChatWorkflow.Create("chat", model)
            .WithTools(kit)
            .WithMemory(memory)
            .WithMaxToolRounds(1)
            .Build()
            .RunAsync(new StreamingChatState { Messages = [AgentMessage.User("go")], SessionId = "s1" });

        // The calls the limit stopped keep their stub results, so the stored transcript stays valid.
        var history = await memory.GetHistoryAsync("s1");
        history.Where(m => m.Role == AgentRole.User).Select(m => m.Content).ShouldBe(["go"]);
        history.Last().Content.ShouldBe("done");
    }

    [Test]
    public async Task StreamingChat_APermanentToolError_ThePromptAfterItIsNotStoredInMemory()
    {
        var memory = new InMemoryConversationMemory();
        var kit = new ToolKit("k").AddTool("pay", "pay", () => ToolResult.Fatal("card declined"));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "pay", "{}"),
            ScriptedAgentModel.Say("The card was declined."));

        await StreamingChatWorkflow.Create("chat", model)
            .WithTools(kit)
            .WithMemory(memory)
            .Build()
            .RunAsync(new StreamingChatState { Messages = [AgentMessage.User("pay it")], SessionId = "s1" });

        model.Requests[^1].Messages.ShouldContain(m => m.Content != null && m.Content.Contains("permanent error"));

        var history = await memory.GetHistoryAsync("s1");
        history.Where(m => m.Role == AgentRole.User).Select(m => m.Content).ShouldBe(["pay it"]);
        history.ShouldContain(m => m.Content == "The card was declined.");
    }

    [Test]
    public async Task StreamingChat_ToolRoundLimit_StillCallingTools_TheApologyIsStreamed()
    {
        // A client that renders deltas shows only what was streamed: the apology has to be one.
        var deltas = new List<string>();
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new ScriptedAgentModel(ScriptedAgentModel.Call("c1", "ping", "{}")); // repeats

        var execution = await StreamingChatWorkflow.Create("chat", model)
            .WithTools(kit)
            .WithMaxToolRounds(1)
            .OnTextDelta(delta =>
            {
                deltas.Add(delta);
                return Task.CompletedTask;
            })
            .Build()
            .RunAsync(new StreamingChatState { Messages = [AgentMessage.User("go")] });

        execution.State.FullText.ShouldBe("I could not finish within the tool-call limit.");
        string.Concat(deltas).ShouldEndWith("I could not finish within the tool-call limit.");
    }

    private static AgentResponse Calls(params (string Id, string Tool)[] calls) =>
        new() { ToolCalls = [.. calls.Select(c => new AgentToolCall(c.Id, c.Tool, "{}"))] };

    [Test]
    public async Task AgentJob_AToolThatEndsTheTurn_LeavesTheRestOfItsRoundUnrun_AndAnswers()
    {
        var looked = 0;
        var kit = new ToolKit("k")
            .AddTool("look", "Look.", () => { looked++; return ToolResult.Ok("seen"); })
            .AddTool("propose", "Propose.", b => { b.EndsTurn().OnExecute(_ => ToolResult.Ok("proposed")); });
        var model = new ScriptedAgentModel(Calls(("c1", "propose"), ("c2", "look")), ScriptedAgentModel.Say("done"));

        var job = AgentJobFactory.Create<Turn>("proposer", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var result = await job.ExecuteAsync(new Turn("go"));

        result.Output.ShouldBe("done");
        looked.ShouldBe(0);
        model.Requests.Count.ShouldBe(2);
    }

    [Test]
    public async Task AgentJob_AfterATurnHasEnded_ACallIsNotRun_AndTheModelIsAskedToAnswer()
    {
        var looked = 0;
        var kit = new ToolKit("k")
            .AddTool("look", "Look.", () => { looked++; return ToolResult.Ok("seen"); })
            .AddTool("propose", "Propose.", b => { b.EndsTurn().OnExecute(_ => ToolResult.Ok("proposed")); });
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "propose", "{}"),
            ScriptedAgentModel.Call("c2", "look", "{}"),
            ScriptedAgentModel.Say("done"));

        var job = AgentJobFactory.Create<Turn>("proposer", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var result = await job.ExecuteAsync(new Turn("go"));

        result.Output.ShouldBe("done");
        looked.ShouldBe(0);
        model.Requests.Count.ShouldBe(3);
    }

    [Test]
    public async Task AgentJob_AfterATurnHasEnded_StillCallingTools_ThrowsNamingTheTurnsEnd()
    {
        // The round limit was never reached, so an error that says to raise it misleads.
        var kit = new ToolKit("k")
            .AddTool("look", "Look.", () => ToolResult.Ok("seen"))
            .AddTool("propose", "Propose.", b => { b.EndsTurn().OnExecute(_ => ToolResult.Ok("proposed")); });
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "propose", "{}"),
            ScriptedAgentModel.Call("c2", "look", "{}")); // repeats

        var job = AgentJobFactory.Create<Turn>("proposer", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => job.ExecuteAsync(new Turn("go")));

        ex.Message.ShouldContain("ended its turn");
        ex.Message.ShouldNotContain("WithMaxToolRounds");
    }

    [Test]
    public async Task AgentJob_ToolRoundLimit_ACallThatEndsTheTurn_IsStillRun()
    {
        var proposed = 0;
        var kit = new ToolKit("k")
            .AddTool("look", "Look.", () => ToolResult.Ok("seen"))
            .AddTool("propose", "Propose.", b => { b.EndsTurn().OnExecute(_ => { proposed++; return ToolResult.Ok("proposed"); }); });
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "look", "{}"),
            ScriptedAgentModel.Call("c2", "propose", "{}"),
            ScriptedAgentModel.Say("done"));

        var job = AgentJobFactory.Create<Turn>("limited", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WithMaxToolRounds(1)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var result = await job.ExecuteAsync(new Turn("go"));

        result.Output.ShouldBe("done");
        proposed.ShouldBe(1);
    }

    private sealed record Answer(string Text);

    /// <summary>A job whose <c>finish</c> tool ends it, and whose <c>look</c> tool counts its calls.</summary>
    private static (ToolKit Kit, Func<int> Looked) Finishing()
    {
        var looked = 0;
        var kit = new ToolKit("k")
            .AddTool("look", "Look.", () => { looked++; return ToolResult.Ok("seen"); })
            .AddTool("finish", "Finish.", b => { b.EndsJob().OnExecute(_ => ToolResult.Ok("finished")); });

        return (kit, () => looked);
    }

    private static AgentResponse Said(string text, params (string Id, string Tool)[] calls) =>
        new() { Text = text, ToolCalls = [.. calls.Select(c => new AgentToolCall(c.Id, c.Tool, "{}"))], StopReason = AgentStopReason.ToolCalls };

    [Test]
    public async Task AgentJob_AToolThatEndsTheJob_IsTheLastThingDone_AndNoAnswerIsAskedFor()
    {
        var (kit, looked) = Finishing();
        var model = new ScriptedAgentModel(Said("Finishing with this.", ("c1", "finish"), ("c2", "look")));

        var job = AgentJobFactory.Create<Turn, Answer>("finisher", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WhenEndedByTool((s, said) => s with { Output = said })
            .MapResult((s, r) => s with { Output = r.Text })
            .Build();

        var result = await job.ExecuteAsync(new Turn("go")).WaitAsync(TimeSpan.FromSeconds(10));

        result.Output.ShouldBe("Finishing with this.");
        looked().ShouldBe(0);
        model.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task AgentJob_EndedByATool_KeepsTheCallAndItsResultInMemory()
    {
        var memory = new InMemoryConversationMemory();
        var (kit, _) = Finishing();
        var model = new ScriptedAgentModel(Said("", ("c1", "finish")));

        await AgentJobFactory.Create<Turn, Answer>("finisher", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WithMemory(memory, _ => "s1")
            .MapResult((s, r) => s with { Output = r.Text })
            .Build()
            .ExecuteAsync(new Turn("go")).WaitAsync(TimeSpan.FromSeconds(10));

        var history = await memory.GetHistoryAsync("s1");
        history.Select(m => m.Role).ShouldBe([AgentRole.User, AgentRole.Assistant, AgentRole.Tool]);
        history[1].ToolCalls.ShouldNotBeNull().ShouldHaveSingleItem().FunctionName.ShouldBe("finish");
        history[2].Content.ShouldBe("finished");
    }

    [Test]
    public async Task AgentJob_ToolRoundLimit_ACallThatEndsTheJob_IsRun_AndNoAnswerIsAskedFor()
    {
        var (kit, _) = Finishing();
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "look", "{}"),
            Said("Finishing at the limit.", ("c2", "finish")));

        var result = await AgentJobFactory.Create<Turn, Answer>("limited", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WithMaxToolRounds(1)
            .WhenEndedByTool((s, said) => s with { Output = said })
            .MapResult((s, r) => s with { Output = r.Text })
            .Build()
            .ExecuteAsync(new Turn("go")).WaitAsync(TimeSpan.FromSeconds(10));

        result.Output.ShouldBe("Finishing at the limit.");
        model.Requests.Count.ShouldBe(2);
    }

    [Test]
    public async Task AgentJob_RoundsThatOnlyRepeatEarlierCalls_EndTheRun_AsRepeatedOutput()
    {
        // A loop, not a turn to wrap up: asked to answer instead, the model would be paid to go on.
        var kit = new ToolKit("k").AddTool("ping", "ping", () => ToolResult.Ok("pong"));
        var model = new CallsUntilToldToAnswer("ping");

        var job = AgentJobFactory.Create<Turn>("repeater", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WithMaxToolRounds(10)
            .WithMaxRepeatedToolRounds(2)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var thrown = await Should.ThrowAsync<RepeatedOutputException>(() => job.ExecuteAsync(new Turn("go")));

        thrown.Message.ShouldContain("ping");
        TerminalProviderError.Is(thrown).ShouldBeTrue();
        model.Requests.ShouldBe(3); // a new call, then two rounds that only repeat it
        model.ToolResults[0].ShouldBe("pong");
        model.ToolResults[1].ShouldNotBe("pong");
    }

    [Test]
    public async Task AgentJob_ARepeatedCall_WhoseAnswerWasCompactedAway_IsAnsweredAgain()
    {
        // Told "answered before" about an answer it was no longer sent, the model could only ask again,
        // and each time would count towards ending the run.
        var kit = new ToolKit("k").AddTool("look", "look", () => ToolResult.Ok("seen"));
        var strategy = new ShowsOnlyUserMessages();
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "look", "{}"),
            ScriptedAgentModel.Call("c2", "look", "{}"),
            ScriptedAgentModel.Say("done"));

        var result = await AgentJobFactory.Create<Turn>("compacted", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WithContextStrategy(strategy)
            .WithMaxRepeatedToolRounds(1)
            .MapResult((s, r) => s with { Output = r })
            .Build()
            .ExecuteAsync(new Turn("go"));

        result.Output.ShouldBe("done");
        strategy.LastSeen.Single(m => m.ToolCallId == "c2").Content.ShouldBe("seen");
    }

    /// <summary>Sends the model only the user's messages, and keeps the whole list it was given.</summary>
    private sealed class ShowsOnlyUserMessages : IContextStrategy
    {
        public List<AgentMessage> LastSeen { get; private set; } = [];

        public Task<ContextProjection> ApplyAsync(
            IReadOnlyList<AgentMessage> messages, string? systemPrompt, ContextBudget budget, CancellationToken ct = default)
        {
            LastSeen = [.. messages];
            var kept = messages.Where(m => m.Role == AgentRole.User).ToList();

            return Task.FromResult(new ContextProjection
            {
                Messages = kept,
                ShadowedCount = messages.Count - kept.Count,
                Reason = ContextShadowReason.Dropped,
                AppliedBudget = budget.Resolve(int.MaxValue)
            });
        }
    }

    /// <summary>Calls one tool every time, until the last thing it was sent asks it to answer.</summary>
    private sealed class CallsUntilToldToAnswer(string tool) : IAgentModel
    {
        public int Requests { get; private set; }

        /// <summary>What each call was answered, read when the answer arrives.</summary>
        public List<string> ToolResults { get; } = [];

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Requests++;
            var last = request.Messages.LastOrDefault();

            if (last is { Role: AgentRole.Tool })
                ToolResults.Add(last.Content ?? string.Empty);

            return Task.FromResult(last is { Role: AgentRole.User } && Requests > 1
                ? ScriptedAgentModel.Say("done")
                : ScriptedAgentModel.Call($"c{Requests}", tool, "{}"));
        }
    }

    [Test]
    public async Task AgentJob_ACallAnsweredDifferently_IsNotARepeat()
    {
        var count = 0;
        var kit = new ToolKit("k").AddTool("tick", "tick", () => ToolResult.Ok($"tick {++count}"));
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "tick", "{}"),
            ScriptedAgentModel.Call("c2", "tick", "{}"),
            ScriptedAgentModel.Call("c3", "tick", "{}"),
            ScriptedAgentModel.Say("done"));

        var job = AgentJobFactory.Create<Turn>("ticker", model)
            .WithPrompt(s => s.Input)
            .WithTools(kit)
            .WithMaxRepeatedToolRounds(1)
            .MapResult((s, r) => s with { Output = r })
            .Build();

        var result = await job.ExecuteAsync(new Turn("go"));

        result.Output.ShouldBe("done");
        count.ShouldBe(3);
    }
}
