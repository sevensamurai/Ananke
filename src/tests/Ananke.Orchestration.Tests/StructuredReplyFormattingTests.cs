using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A structured job's reply survives the two shapes a provider that is only <em>asked</em> for a
/// schema tends to add — a code fence, or a sentence of preamble — and gets exactly one repair
/// round when the reply is not recoverable that way either. A job told to take its answer from its
/// own text is not asked for the schema at all.
/// </summary>
[TestFixture]
public class StructuredReplyFormattingTests
{
    private sealed record Turn(string Input, string? Output = null);
    private sealed record Answer(string Value);

    [Test]
    public async Task AgentJob_FencedJsonReply_Parses()
    {
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Say("```json\n{\"Value\":\"blue\"}\n```"));

        var result = await Job(model).ExecuteAsync(new Turn("colour?"));

        result.Output.ShouldBe("blue");
    }

    [Test]
    public async Task AgentJob_JsonAfterPreamble_Parses()
    {
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Say("Sure, here you go: {\"Value\":\"blue\"} — hope that helps!"));

        var result = await Job(model).ExecuteAsync(new Turn("colour?"));

        result.Output.ShouldBe("blue");
    }

    [Test]
    public async Task AgentJob_InvalidJsonThenValid_RepairsOnce()
    {
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Say("not json at all"),
            ScriptedAgentModel.Say("""{"Value":"blue"}"""));

        var result = await Job(model).ExecuteAsync(new Turn("colour?"));

        result.Output.ShouldBe("blue");
        model.Requests.Count.ShouldBe(2);
        model.Requests[^1].Messages.ShouldContain(m =>
            m.Role == AgentRole.User && m.Content!.Contains("was not valid JSON"));
    }

    [Test]
    public async Task AgentJob_InvalidJsonTwice_Throws()
    {
        var model = new ScriptedAgentModel(ScriptedAgentModel.Say("not json at all")); // repeats

        await Should.ThrowAsync<JsonException>(() => Job(model).ExecuteAsync(new Turn("colour?")));

        model.Requests.Count.ShouldBe(2); // the original attempt and the one repair round
    }

    [Test]
    public async Task AgentJob_InvalidJsonThenValid_TheRepairIsCompactedLikeTheFirstAttempt()
    {
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Say("not json at all"),
            ScriptedAgentModel.Say("""{"Value":"blue"}"""));

        var result = await AgentJobFactory.Create<Turn, Answer>("s", model)
            .WithPrompt(s => s.Input)
            .WithContextStrategy(new KeepLast(2))
            .MapResult((s, a) => s with { Output = a.Value })
            .Build()
            .ExecuteAsync(new Turn("colour?"));

        result.Output.ShouldBe("blue");

        // The bad reply and the repair prompt — not the whole history the strategy would withhold.
        var repair = model.Requests[^1].Messages;
        repair.Count.ShouldBe(2);
        repair[^1].Content!.ShouldContain("was not valid JSON");
    }

    [Test]
    public async Task AgentJob_InvalidJsonThenValid_TheContextLimitBindsOnTheRepair()
    {
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Say("not json at all " + string.Join(' ', Enumerable.Range(0, 800).Select(i => $"w{i}"))),
            ScriptedAgentModel.Say("""{"Value":"blue"}"""));

        var job = AgentJobFactory.Create<Turn, Answer>("s", model)
            .WithPrompt(s => s.Input)
            .WithContextLimit(500)
            .MapResult((s, a) => s with { Output = a.Value })
            .Build();

        await Should.ThrowAsync<InvalidOperationException>(() => job.ExecuteAsync(new Turn("colour?")));

        model.Requests.Count.ShouldBe(1, "the repair would have sent past the limit");
    }

    [Test]
    public async Task AgentJob_WithAnswerFromText_TakesTheTurnsLastText_AndMakesNoSecondCall()
    {
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "look", "{}"),
            ScriptedAgentModel.Say("I chose blue."));

        var result = await AgentJobFactory.Create<Turn, Answer>("s", model)
            .WithPrompt(s => s.Input)
            .WithTools(new ToolKit("k").AddTool("look", "Looks", () => ToolResult.Ok("blue")))
            .WithAnswerFromText(text => new Answer(text))
            .MapResult((s, a) => s with { Output = a.Value })
            .Build()
            .ExecuteAsync(new Turn("colour?"));

        result.Output.ShouldBe("I chose blue.");
        model.Requests.Count.ShouldBe(2);
        model.Requests.ShouldAllBe(r => r.ResponseFormat == null);
    }

    /// <summary>A sliding window of the last <paramref name="count"/> messages.</summary>
    private sealed class KeepLast(int count) : IContextStrategy
    {
        public Task<ContextProjection> ApplyAsync(
            IReadOnlyList<AgentMessage> messages,
            string? systemPrompt,
            ContextBudget budget,
            CancellationToken ct = default) =>
            Task.FromResult(new ContextProjection
            {
                Messages = [.. messages.TakeLast(count)],
                ShadowedCount = Math.Max(0, messages.Count - count),
                Reason = ContextShadowReason.Dropped,
                AppliedBudget = budget.Resolve(int.MaxValue)
            });
    }

    private static AgentJob<Turn, Answer> Job(ScriptedAgentModel model) =>
        AgentJobFactory.Create<Turn, Answer>("s", model)
            .WithPrompt(s => s.Input)
            .MapResult((s, a) => s with { Output = a.Value })
            .Build();
}
