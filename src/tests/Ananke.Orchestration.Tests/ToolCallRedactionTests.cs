using System.Text.Json.Nodes;
using Ananke.Abstractions;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a tool call's event holds of a value whose type marks a property with <see cref="RedactAttribute"/>.
/// </summary>
/// <remarks>
/// Only the record is masked. The tool is handed the value and the model is sent the result as they
/// were, so a mark cannot change the run.
/// </remarks>
[TestFixture]
public class ToolCallRedactionTests
{
    private sealed record Guest
    {
        [Redact]
        public string Phone { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;
    }

    private sealed record Booking
    {
        public string Notes { get; init; } = string.Empty;

        [Redact]
        public string Phone { get; init; } = string.Empty;
    }

    private sealed record Plain
    {
        public string Name { get; init; } = string.Empty;
    }

    private const string Phone = "600 123 456";

    [Test]
    public async Task StreamAsync_AListParameterWithAMarkedProperty_TheEventMasksItsValue()
    {
        Guest[]? handed = null;
        var model = Calling("book", $$"""{"guests":[{"Phone":"{{Phone}}","Name":"Ana"}]}""");
        var tools = new ToolKit("k").AddTool("book", "Books a room.", tool => tool
            .ParamList<Guest>("guests", "Who stays.")
            .OnExecute(args =>
            {
                handed = args.Get<Guest[]>("guests");
                return ToolResult.Ok("booked");
            }));

        var call = (await StreamCallsAsync(model, tools)).ShouldHaveSingleItem();

        var guest = JsonNode.Parse(call.Arguments!)!["guests"]![0]!;
        guest["Phone"]!.GetValue<string>().ShouldBe(Redaction.Marker);
        guest["Name"]!.GetValue<string>().ShouldBe("Ana");
        handed.ShouldNotBeNull().ShouldHaveSingleItem().Phone.ShouldBe(Phone, "the tool is handed the value");
    }

    [Test]
    public async Task StreamAsync_AJsonResultWithAMarkedProperty_TheEventMasksItButTheModelIsSentIt()
    {
        var model = Calling("look_up", "{}");
        var tools = new ToolKit("k").AddTool("look_up", "Finds the booking.",
            () => ToolResult.Json(new Booking { Notes = "late arrival", Phone = Phone }));

        var call = (await StreamCallsAsync(model, tools)).ShouldHaveSingleItem();

        call.Result.ShouldNotBeNull().ShouldNotContain(Phone);
        call.Result.ShouldContain(Redaction.Marker);
        call.Result.ShouldContain("late arrival");
        model.Requests[^1].Messages.Last(m => m.Role == AgentRole.Tool).Content.ShouldNotBeNull().ShouldContain(Phone);
    }

    private sealed record Envelope
    {
        public object? Payload { get; init; }
    }

    [Test]
    public async Task StreamAsync_AJsonResultHoldingAMarkedValueAsAnObject_TheEventMasksIt()
    {
        // The serializer writes an object-typed member as its runtime type, and so must the mask.
        var model = Calling("look_up", "{}");
        var tools = new ToolKit("k").AddTool("look_up", "Finds the booking.",
            () => ToolResult.Json(new Envelope { Payload = new Booking { Notes = "late arrival", Phone = Phone } }));

        var call = (await StreamCallsAsync(model, tools)).ShouldHaveSingleItem();

        call.Result.ShouldNotBeNull().ShouldNotContain(Phone);
        call.Result.ShouldContain("late arrival");
    }

    [Test]
    public async Task StreamAsync_AMarkedValueAcrossTheResultCut_IsMaskedWhole()
    {
        // {"notes":"…","phone":"…": the value starts five characters before the cut and ends after it.
        var notes = new string('x', AgentToolCalled.ResultCap - "{\"notes\":\"".Length - "\",\"phone\":\"".Length - 5);
        var model = Calling("look_up", "{}");
        var tools = new ToolKit("k").AddTool("look_up", "Finds the booking.",
            () => ToolResult.Json(new Booking { Notes = notes, Phone = Phone }));

        var call = (await StreamCallsAsync(model, tools)).ShouldHaveSingleItem();

        call.Result.ShouldNotBeNull().Length.ShouldBe(AgentToolCalled.ResultCap);
        call.Result.ShouldNotContain("600 1");
    }

    [Test]
    public async Task StreamAsync_ATypeWithNoMarks_TheEventHoldsTheArgumentsAsWritten()
    {
        const string arguments = """{ "guests" : [ { "Name" : "Ana" } ] }""";
        var model = Calling("book", arguments);
        var tools = new ToolKit("k").AddTool("book", "Books a room.", tool => tool
            .ParamList<Plain>("guests", "Who stays.")
            .OnExecute(_ => ToolResult.Ok("booked")));

        (await StreamCallsAsync(model, tools)).ShouldHaveSingleItem().Arguments.ShouldBe(arguments);
    }

    private static ScriptedAgentModel Calling(string tool, string arguments) =>
        new(ScriptedAgentModel.Call("call-a", tool, arguments), ScriptedAgentModel.Say("done"));

    private static async Task<List<AgentToolCalled>> StreamCallsAsync(IAgentModel model, ToolKit tools)
    {
        var workflow = new Workflow<State>("redaction")
            .Job("ask", AgentJobFactory.Create<State>("ask", model)
                .WithPrompt(_ => "book it")
                .WithTools(tools)
                .MapResult((s, text) => s with { Text = text })
                .Build())
            .Then("ask", Workflow.End)
            .RecordContent(true);

        var calls = new List<AgentToolCalled>();

        await foreach (var evt in workflow.StreamAsync(new State()).ConfigureAwait(false))
        {
            if (evt is AgentToolCalled called)
                calls.Add(called);
        }

        return calls;
    }

    private sealed record State
    {
        public string Text { get; init; } = string.Empty;
    }
}
