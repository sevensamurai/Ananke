using System.Diagnostics.Metrics;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tools;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class HallucinationDetectionTests
{
    // ── Observer is invoked with correct event data ───────────────────────────

    [Test]
    public async Task UnknownTool_InvokesHallucinationObserver()
    {
        var captured = new List<HallucinatedToolCallEvent>();
        var observer = new CapturingHallucinationObserver(captured);

        var model = new ToolCallThenTextModel("ghost_tool", "{}", "done");
        var kit = new ToolKit("ops")
            .AddTool("real_tool", "A real tool", () => ToolResult.Ok("real"))
            .WithHallucinationObserver(observer);

        var agent = AgentJobFactory.Create<string>("hal-test", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        captured.Count.ShouldBe(1);
        captured[0].RequestedToolName.ShouldBe("ghost_tool");
        captured[0].RequestedKitName.ShouldBe("ops");
        captured[0].AgentId.ShouldBe("hal-test");
    }

    // ── Well-formed error result returned to model ────────────────────────────

    [Test]
    public async Task UnknownTool_ReturnsErrorResultToModel()
    {
        string? toolResultSentToModel = null;
        var model = new ToolCallThenTextModel("ghost_tool", "{}", "done",
            onToolResult: r => toolResultSentToModel = r);

        var kit = new ToolKit("ops").AddTool("real_tool", "Real", () => ToolResult.Ok("ok"));

        var agent = AgentJobFactory.Create<string>("err-test", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        toolResultSentToModel.ShouldNotBeNull();
        toolResultSentToModel.ShouldContain("ghost_tool");
        toolResultSentToModel.ShouldContain("not registered");
    }

    // ── ToolMetrics.HallucinationReported is incremented ─────────────────────

    [Test]
    public async Task UnknownTool_IncrementsHallucinationCounter()
    {
        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "ananke.tools.hallucination")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        var model = new ToolCallThenTextModel("ghost_tool", "{}", "done");
        var kit = new ToolKit("ops").AddTool("real_tool", "Real", () => ToolResult.Ok("ok"));
        var agent = AgentJobFactory.Create<string>("counter-test", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        measurements.ShouldNotBeEmpty();
        measurements.Sum().ShouldBe(1L);
    }

    // ── Known tool is not affected ────────────────────────────────────────────

    [Test]
    public async Task KnownTool_DoesNotInvokeHallucinationObserver()
    {
        var captured = new List<HallucinatedToolCallEvent>();
        var observer = new CapturingHallucinationObserver(captured);

        var model = new ToolCallThenTextModel("real_tool", "{}", "done");
        var kit = new ToolKit("ops")
            .AddTool("real_tool", "Real", () => ToolResult.Ok("ok"))
            .WithHallucinationObserver(observer);

        var agent = AgentJobFactory.Create<string>("no-hal", model)
            .WithPrompt(s => s)
            .WithTools(kit)
            .MapResult((_, text) => text)
            .Build();

        await agent.ExecuteAsync("go");

        captured.ShouldBeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingHallucinationObserver(List<HallucinatedToolCallEvent> captured)
        : IHallucinationObserver
    {
        public ValueTask ReportAsync(HallucinatedToolCallEvent @event, CancellationToken ct = default)
        {
            captured.Add(@event);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Simulates a model that first requests one tool call, then returns plain text.
    /// Optionally captures the tool result string that the model would receive back.
    /// </summary>
    private sealed class ToolCallThenTextModel(
        string toolName,
        string toolArgs,
        string finalText,
        Action<string>? onToolResult = null) : IAgentModel
    {
        private int _turn;

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            if (_turn++ == 0)
            {
                return Task.FromResult(new AgentResponse
                {
                    Text = string.Empty,
                    ToolCalls = [new AgentToolCall("call_1", toolName, toolArgs)]
                });
            }

            // Capture the tool result message if a callback is set
            if (onToolResult is not null)
            {
                var toolMsg = request.Messages.LastOrDefault(m => m.Role == AgentRole.Tool);
                if (toolMsg?.Content is not null)
                    onToolResult(toolMsg.Content);
            }

            return Task.FromResult(new AgentResponse { Text = finalText });
        }
    }
}
