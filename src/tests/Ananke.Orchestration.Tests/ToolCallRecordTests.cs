using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tracing;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tests.Fakes;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What one tool call leaves behind: an event and a span that name the same call.
/// </summary>
/// <remarks>
/// A model may ask for the same tool twice in one round, so a tool's name does not say which call a
/// record is of. The id the model gave the call does, and it is the id its result is sent back
/// under — which is what lets an event, a span and the conversation be read against each other.
/// </remarks>
[TestFixture]
public class ToolCallRecordTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RunCall_TwoCallsInOneRound_EachEventCarriesTheModelsIdRoundAndDuration()
    {
        var run = await RunTwoCallsAsync();

        var calls = run.Sink.Events.OfType<AgentToolCalled>().ToList();

        calls.Count.ShouldBe(2);
        calls.Select(c => c.CallId).ShouldBe(["call-a", "call-b"]);
        calls.ShouldAllBe(c => c.Round == 1);
        calls.Select(c => c.Duration).ShouldBe([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)]);
    }

    [Test]
    public async Task RunCall_TwoCallsInOneRound_EachSpanIsNamedForItsToolAndCarriesTheSameId()
    {
        var run = await RunTwoCallsAsync();

        var spans = run.Tracer.Spans.Where(s => s.Kind == SpanKind.ToolCall).ToList();

        spans.Select(s => s.Name).ShouldBe(["execute_tool look_up", "execute_tool notify"]);
        spans.Select(s => s.Attributes["gen_ai.tool.call.id"]).ShouldBe(["call-a", "call-b"]);
        spans.Select(s => s.Attributes["gen_ai.tool.name"]).ShouldBe(["look_up", "notify"]);
        spans.ShouldAllBe(s => (string)s.Attributes["gen_ai.operation.name"] == "execute_tool");
        spans.ShouldAllBe(s => s.Attributes["tool_round"].Equals(1L));
        spans.Select(s => s.Attributes["output_length"]).ShouldBe([5L, 4L]);
    }

    [Test]
    public async Task RunCall_ARemoteTool_TargetIsTheEndpointsHostOnly()
    {
        // A callback URI can carry a key in its query, and the span is exported to wherever the host
        // sends its traces.
        var run = await RunTwoCallsAsync();

        var notify = run.Tracer.Spans.Single(s => s.Name == "execute_tool notify");
        notify.Attributes["tool.execution_mode"].ShouldBe("callback");
        notify.Attributes["tool.target"].ShouldBe("hooks.example.com");

        var lookUp = run.Tracer.Spans.Single(s => s.Name == "execute_tool look_up");
        lookUp.Attributes["tool.execution_mode"].ShouldBe("local");
        lookUp.Attributes.ShouldNotContainKey("tool.target");
    }

    [Test]
    public async Task RunCall_AToolThatReturnsAnError_SpanCarriesTheErrorType()
    {
        var run = await RunTwoCallsAsync(notifyFails: true);

        var notify = run.Tracer.Spans.Single(s => s.Name == "execute_tool notify");
        notify.Attributes["error.type"].ShouldBe("_OTHER");
        notify.Attributes["tool.error"].ShouldBe(true);

        run.Tracer.Spans.Single(s => s.Name == "execute_tool look_up")
            .Attributes.ShouldNotContainKey("error.type");
    }

    private static async Task<(CollectingSink Sink, RecordingTracer Tracer)> RunTwoCallsAsync(bool notifyFails = false)
    {
        var clock = new FakeTimeProvider(T0);
        var sink = new CollectingSink();
        var tracer = new RecordingTracer();

        var model = new ScriptedAgentModel(
            new AgentResponse
            {
                ToolCalls =
                [
                    new AgentToolCall("call-a", "look_up", """{"place":"hakone"}"""),
                    new AgentToolCall("call-b", "notify", "{}")
                ]
            },
            ScriptedAgentModel.Say("done"));

        var tools = new ToolKit("k")
            .AddTool("look_up", "Looks a place up.", tool => tool
                .Param("place", "The place.")
                .OnExecute(_ =>
                {
                    clock.Advance(TimeSpan.FromSeconds(2));
                    return ToolResult.Ok("found");
                }))
            .AddTool("notify", "Tells someone.", tool => tool
                .Callback(new Uri("https://hooks.example.com/run?code=s3cret"), verifyReachable: false)
                .OnExecute(_ =>
                {
                    clock.Advance(TimeSpan.FromSeconds(5));
                    return notifyFails ? ToolResult.Error("down") : ToolResult.Ok("sent");
                }));

        var job = AgentJobFactory.Create<State>("ask", model)
            .WithPrompt(_ => "look it up and tell them")
            .WithTools(tools)
            .WithTimeProvider(clock)
            .MapResult((s, text) => s with { Text = text })
            .Build();

        using (WorkflowEventReporting.BeginScope(sink))
        {
            await new Workflow<State>("record")
                .Job("ask", job)
                .Then("ask", Workflow.End)
                .UseTracing(tracer)
                .RunAsync(new State())
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
        }

        return (sink, tracer);
    }

    private sealed record State
    {
        public string Text { get; init; } = string.Empty;
    }

    private sealed class CollectingSink : IWorkflowEventSink
    {
        private readonly List<WorkflowEvent> _events = [];

        public IReadOnlyList<WorkflowEvent> Events
        {
            get { lock (_events) return [.. _events]; }
        }

        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            lock (_events) _events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
