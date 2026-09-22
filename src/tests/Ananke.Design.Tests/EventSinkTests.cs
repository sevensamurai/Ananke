using System.Text.Json;
using Ananke.Design;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Shouldly;

namespace Ananke.Design.Tests;

/// <summary>
/// Where a run's events go: a file to read afterwards, and more than one reader at once.
/// </summary>
/// <remarks>
/// A scope takes one sink (<c>WorkflowEventReporting.BeginScope</c>), so a consumer that both narrates
/// and keeps a record writes the fan-out itself. The record is JSON lines because a run is read back
/// one event at a time, by <c>jq</c> as often as by code.
/// </remarks>
[TestFixture]
public class EventSinkTests
{
    [Test]
    public async Task JsonLines_WritesOneObjectPerPlanEventAndToolCall()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.jsonl");

        try
        {
            using (var sink = JsonLinesEventSink.Open(path))
            {
                await sink.ReportAsync(Reported());
                await sink.ReportAsync(ToolCalled());

                // Neither a plan event nor a tool call: the record is of the plan, not of the runner.
                await sink.ReportAsync(new Noise { WorkflowName = "w", ExecutionId = "e" });
            }

            var lines = await File.ReadAllLinesAsync(path);

            lines.Length.ShouldBe(2);

            var first = JsonDocument.Parse(lines[0]).RootElement;
            first.GetProperty("type").GetString().ShouldBe(nameof(PlanNodeReported));
            first.GetProperty("event").GetProperty("NodeId").GetString().ShouldBe("parse");

            JsonDocument.Parse(lines[1]).RootElement.GetProperty("type").GetString()
                .ShouldBe(nameof(AgentToolCalled));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Test]
    public async Task Several_HandsEveryEventToEachSinkInOrder()
    {
        var seen = new List<string>();
        var sink = EventSinks.Several(new Recording("first", seen), new Recording("second", seen));

        await sink.ReportAsync(Reported());

        seen.ShouldBe(["first", "second"]);
    }

    // ── Fixtures ──

    private static PlanNodeReported Reported() => new()
    {
        WorkflowName = "w",
        ExecutionId = "e",
        PlanId = "plan",
        PlanVersion = 1,
        NodeId = "parse",
        Summary = "found one way"
    };

    private static AgentToolCalled ToolCalled() => new()
    {
        WorkflowName = "w",
        ExecutionId = "e",
        AgentName = "plan-node:parse",
        ToolName = "search",
        Arguments = """{"place":"hakone"}""",
        Result = "{}",
        ResultLength = 2
    };

    private sealed record Noise : WorkflowEvent;

    private sealed class Recording(string name, List<string> into) : IWorkflowEventSink
    {
        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            into.Add(name);
            return ValueTask.CompletedTask;
        }
    }
}
