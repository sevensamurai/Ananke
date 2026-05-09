using Ananke.Organics.Kernel;
using Ananke.Organics.Kernel.Snapshots;
using Ananke.Organics.Sensing;
using Ananke.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel;

[TestFixture]
public class WorkflowHostExtensionsTests
{
    private InProcessWorkflowHost _mesh = null!;
    private InMemoryCapabilityMap _landscape = null!;

    [SetUp]
    public void SetUp()
    {
        _mesh = new InProcessWorkflowHost();
        // Use a large signalTimeout so FakeTimeProvider-stamped signals (epoch 2000)
        // are not treated as stale when Discover checks against real UtcNow.
        _landscape = new InMemoryCapabilityMap(signalTimeout: TimeSpan.FromDays(365 * 100));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _mesh.DisposeAsync();
    }

    [Test]
    public async Task StartWithHealthCheck_EmitsSignals()
    {
        var cell = new WorkflowSnapshotBuilder("test-cell", "test")
            .Tools(["tool_a", "tool_b"])
            .Build();

        await _mesh.StartWithHealthCheckAsync(cell, _landscape);
        await _landscape.WhenRegisteredAsync("test-cell").WaitAsync(TimeSpan.FromSeconds(5));

        var sensed = _landscape.Discover("test");
        sensed.Count.ShouldBe(1);
        sensed[0].WorkflowName.ShouldBe("test-cell");
        sensed[0].Capabilities.ShouldContain("tool_a");
        sensed[0].Capabilities.ShouldContain("tool_b");
    }

    [Test]
    public async Task StartWithHealthCheck_SetsLineage()
    {
        var cell = new WorkflowSnapshotBuilder("child-cell", "orders")
            .SplitFrom("parent-cell")
            .Build();

        await _mesh.StartWithHealthCheckAsync(cell, _landscape);
        await _landscape.WhenRegisteredAsync("child-cell").WaitAsync(TimeSpan.FromSeconds(5));

        var sensed = _landscape.Discover("orders");
        sensed.Count.ShouldBe(1);
        sensed[0].WorkflowName.ShouldBe("child-cell");
    }

    [Test]
    public async Task StartWithHealthCheck_BootstrapDelay_DelaysFirstSignal()
    {
        var clock = new FakeTimeProvider();
        var cell = new WorkflowSnapshotBuilder("slow-cell", "catalog")
            .Build();
        var delayingTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await _mesh.StartWithHealthCheckAsync(cell, _landscape,
            heartbeatInterval: null,
            bootstrapDelay: TimeSpan.FromMilliseconds(500),
            timeProvider: clock,
            onDelaying: () => delayingTcs.TrySetResult());

        // Not yet registered — the bootstrap timer has not elapsed.
        _landscape.Discover("catalog").Count.ShouldBe(0);

        // Wait until the loop has entered the bootstrap Delay (timer is now registered).
        await delayingTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Advance past the bootstrap delay; the first heartbeat fires immediately.
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await _landscape.WhenRegisteredAsync("slow-cell").WaitAsync(TimeSpan.FromSeconds(5));

        _landscape.Discover("catalog").Count.ShouldBe(1);
    }

    [Test]
    public async Task StartWithHealthCheck_KillStopsHeartbeat()
    {
        var cell = new WorkflowSnapshotBuilder("mortal-cell", "test")
            .Build();

        await _mesh.StartWithHealthCheckAsync(cell, _landscape);
        await _landscape.WhenRegisteredAsync("mortal-cell").WaitAsync(TimeSpan.FromSeconds(5));

        _mesh.ListActive().ShouldContain("mortal-cell");

        await _mesh.StopAsync("mortal-cell");

        _mesh.ListActive().ShouldNotContain("mortal-cell");
    }

    [Test]
    public async Task StartWithHealthCheck_NullCell_Throws()
    {
        await Should.ThrowAsync<ArgumentNullException>(() =>
            _mesh.StartWithHealthCheckAsync(null!, _landscape));
    }

    [Test]
    public async Task StartWithHealthCheck_NullLandscape_Throws()
    {
        var cell = new WorkflowSnapshotBuilder("test", "test").Build();

        await Should.ThrowAsync<ArgumentNullException>(() =>
            _mesh.StartWithHealthCheckAsync(cell, null!));
    }
}
