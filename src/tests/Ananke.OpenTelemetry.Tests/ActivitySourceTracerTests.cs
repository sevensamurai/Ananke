using System.Diagnostics;
using Ananke.OpenTelemetry;
using Shouldly;

namespace Ananke.OpenTelemetry.Tests;

[TestFixture]
public class ActivitySourceTracerTests
{
    [Test]
    public void Dispose_ReleasesTheActivitySourceFromItsListeners()
    {
        // A name no other test uses, so the listener counts only this tracer's source.
        var name = $"ananke-tracer-test-{Guid.NewGuid():N}";
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var tracer = new ActivitySourceTracer(name);
        tracer.Source.HasListeners().ShouldBeTrue();

        tracer.Dispose();

        tracer.Source.HasListeners().ShouldBeFalse("a disposed source is released from its listeners");
    }

    [Test]
    public async Task SetAttribute_ANumberAndAFlag_ReachTheActivityAsTheirOwnTypes()
    {
        var name = $"ananke-tracer-test-{Guid.NewGuid():N}";
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);
        using var tracer = new ActivitySourceTracer(name);

        await using (var trace = tracer.StartTrace("w", "e"))
        await using (var span = trace.StartSpan("execute_tool save"))
        {
            span.SetAttribute("output_length", 42L);
            span.SetAttribute("tool.error", true);
        }

        var tool = stopped.Single(a => a.DisplayName == "execute_tool save");
        tool.GetTagItem("output_length").ShouldBe(42L);
        tool.GetTagItem("tool.error").ShouldBe(true);
    }
}
