using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Ananke.Abstractions;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Budget;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a model call inside a workflow publishes on the usage meter, as a metrics listener such as
/// the OpenTelemetry budget meter reads it.
/// </summary>
[TestFixture]
public class UsageMetricsTests
{
    private sealed record State
    {
        public string Text { get; init; } = "";
    }

    private sealed record Answer
    {
        public string? Text { get; init; }
    }

    [Test]
    public async Task RunAsync_AnAgentCall_PublishesItsTokensTaggedWithTheWorkflow()
    {
        using var listener = new UsageListener();
        var name = $"usage-{Guid.NewGuid():N}";

        await Ask(name, SimulatedAgentModel.Json(new Answer { Text = "hi" },
            new SimulatedModelOptions { InputTokens = 120, OutputTokens = 30 }));

        listener.Sum(UsageInstrumentNames.TokensIn, name).ShouldBe(120);
        listener.Sum(UsageInstrumentNames.TokensOut, name).ShouldBe(30);
        listener.Sum(UsageInstrumentNames.Cost, name).ShouldBe(0);
    }

    [Test]
    public async Task RunAsync_ACallPricedByARouter_PublishesItsCostAndCountsItsTokensOnce()
    {
        // The agent job reports the call's tokens and the router reports its cost: a million input
        // tokens at $2 per million.
        using var listener = new UsageListener();
        var name = $"usage-{Guid.NewGuid():N}";
        var routed = new CapabilityModelRouter()
            .AddModel(new ModelProfile
            {
                Name = "priced",
                Model = SimulatedAgentModel.Json(new Answer { Text = "hi" },
                    new SimulatedModelOptions { InputTokens = 1_000_000, OutputTokens = 0 }),
                Capabilities = ModelCapability.TextGeneration | ModelCapability.StructuredOutput,
                CostPer1KInputTokens = 0.002m
            })
            .ToAgentModel();

        await Ask(name, routed);

        listener.Sum(UsageInstrumentNames.TokensIn, name).ShouldBe(1_000_000);
        listener.Sum(UsageInstrumentNames.Cost, name).ShouldBe(2.0, tolerance: 1e-9);
    }

    private static Task Ask(string workflow, IAgentModel model) =>
        new Workflow<State>(workflow)
            .Job("ask", AgentJobFactory.Create<State, Answer>("ask", model)
                .WithPrompt(_ => "say hi")
                .MapResult((s, r) => s with { Text = r.Text ?? "" })
                .Build())
            .Then("ask", Workflow.End)
            .RunAsync(new State())
            .WaitAsync(TimeSpan.FromSeconds(10));

    /// <summary>Collects what the usage meter publishes, by instrument and workflow tag.</summary>
    private sealed class UsageListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Instrument, string? Workflow, double Value)> _seen = new();

        public UsageListener()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AnankeSourceNames.OrchestrationUsage)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(i, v, tags));
            _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i, v, tags));
            _listener.Start();
        }

        public double Sum(string instrument, string workflow) =>
            _seen.Where(m => m.Instrument == instrument && m.Workflow == workflow).Sum(m => m.Value);

        public void Dispose() => _listener.Dispose();

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string? workflow = null;
            foreach (var tag in tags)
                if (tag.Key == UsageInstrumentNames.WorkflowTag)
                    workflow = tag.Value as string;

            _seen.Enqueue((instrument.Name, workflow, value));
        }
    }
}
