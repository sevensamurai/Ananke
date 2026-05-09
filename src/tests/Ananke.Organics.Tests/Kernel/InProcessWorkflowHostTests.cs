using System.Threading.Channels;
using Ananke.Organics.Kernel;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel;

[TestFixture]
public class InProcessWorkflowHostTests
{
    private InProcessWorkflowHost _mesh = null!;

    [SetUp]
    public void SetUp()
    {
        _mesh = new InProcessWorkflowHost();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _mesh.DisposeAsync();
    }

    // ── Spawn + ListAlive ───────────────────────────────────────────

    [Test]
    public async Task Spawn_ListAlive_ContainsName()
    {
        await _mesh.StartAsync("cell-1", WorkflowLoops.Park);
        await _mesh.WhenStartedAsync("cell-1").WaitAsync(TimeSpan.FromSeconds(5));

        _mesh.ListActive().ShouldContain("cell-1");
    }

    [Test]
    public async Task Spawn_DuplicateName_Throws()
    {
        await _mesh.StartAsync("cell-1", WorkflowLoops.Park);

        await Should.ThrowAsync<InvalidOperationException>(
            () => _mesh.StartAsync("cell-1", WorkflowLoops.Park));
    }

    // ── KillAsync ───────────────────────────────────────────────────

    [Test]
    public async Task KillAsync_RemovesFromAlive()
    {
        await _mesh.StartAsync("cell-1", WorkflowLoops.Park);
        await _mesh.WhenStartedAsync("cell-1").WaitAsync(TimeSpan.FromSeconds(5));

        await _mesh.StopAsync("cell-1");

        _mesh.ListActive().ShouldNotContain("cell-1");
    }

    [Test]
    public async Task KillAsync_CancelsToken()
    {
        var wasCancelled = false;
        await _mesh.StartAsync("cell-1", async ct =>
        {
            try { await WorkflowLoops.Park(ct); }
            catch (OperationCanceledException) { wasCancelled = true; }
        });
        await _mesh.WhenStartedAsync("cell-1").WaitAsync(TimeSpan.FromSeconds(5));

        await _mesh.StopAsync("cell-1");

        wasCancelled.ShouldBeTrue();
    }

    [Test]
    public async Task KillAsync_UnknownName_NoOp()
    {
        await Should.NotThrowAsync(() => _mesh.StopAsync("nonexistent"));
    }

    // ── DisposeAsync ────────────────────────────────────────────────

    [Test]
    public async Task DisposeAsync_KillsAll()
    {
        var cancelCount = 0;
        for (var i = 0; i < 3; i++)
        {
            await _mesh.StartAsync($"cell-{i}", async ct =>
            {
                try { await WorkflowLoops.Park(ct); }
                catch (OperationCanceledException) { Interlocked.Increment(ref cancelCount); }
            });
            await _mesh.WhenStartedAsync($"cell-{i}").WaitAsync(TimeSpan.FromSeconds(5));
        }

        await _mesh.DisposeAsync();

        _mesh.ListActive().ShouldBeEmpty();
        cancelCount.ShouldBe(3);
    }

    // ── Crashed loop ────────────────────────────────────────────────

    [Test]
    public async Task CrashedLoop_RemovedFromAlive()
    {
        await _mesh.StartAsync("crasher", _ => throw new ApplicationException("boom"));
        await _mesh.WhenStoppedAsync("crasher").WaitAsync(TimeSpan.FromSeconds(5));

        _mesh.ListActive().ShouldNotContain("crasher");
    }

    // ── Pause / Resume ──────────────────────────────────────────────

    [Test]
    public async Task PauseAsync_CellStaysAlive()
    {
        await _mesh.StartAsync("cell", WorkflowLoops.Park);
        await _mesh.WhenStartedAsync("cell").WaitAsync(TimeSpan.FromSeconds(5));

        await _mesh.PauseAsync("cell");

        _mesh.ListActive().ShouldContain("cell");
    }

    [Test]
    public async Task PauseAsync_BlocksNextIteration()
    {
        var gate = Channel.CreateUnbounded<int>();
        var iteration = 0;
        await _mesh.StartAsync("cell", async ct =>
        {
            while (!ct.IsCancellationRequested)
                await gate.Writer.WriteAsync(Interlocked.Increment(ref iteration), ct);
        });

        // Observe at least two iterations to confirm the loop is running
        await gate.Reader.ReadAsync();
        await gate.Reader.ReadAsync();

        await _mesh.PauseAsync("cell");
        await _mesh.WhenPausedAsync("cell").WaitAsync(TimeSpan.FromSeconds(5));

        // Drain any items written between our last read and the loop stopping —
        // WhenPausedAsync guarantees the loop is fully done, so after the drain
        // the channel must stay empty forever.
        while (gate.Reader.TryRead(out _)) { }
        gate.Reader.TryRead(out _).ShouldBeFalse();
    }

    [Test]
    public async Task ResumeAsync_CellContinues()
    {
        var gate = Channel.CreateUnbounded<int>();
        var iteration = 0;
        await _mesh.StartAsync("cell", async ct =>
        {
            while (!ct.IsCancellationRequested)
                await gate.Writer.WriteAsync(Interlocked.Increment(ref iteration), ct);
        });

        // Confirm running, then pause
        await gate.Reader.ReadAsync();
        await _mesh.PauseAsync("cell");
        await _mesh.WhenPausedAsync("cell").WaitAsync(TimeSpan.FromSeconds(5));
        var countAtPause = Volatile.Read(ref iteration);

        // Resume and wait for at least one new iteration
        await _mesh.ResumeAsync("cell");
        await _mesh.WhenStartedAsync("cell").WaitAsync(TimeSpan.FromSeconds(5));
        await gate.Reader.ReadAsync();

        Volatile.Read(ref iteration).ShouldBeGreaterThan(countAtPause);
    }

    [Test]
    public async Task PauseAsync_ThenStopAsync_GracefulShutdown()
    {
        await _mesh.StartAsync("cell", WorkflowLoops.Park);
        await _mesh.WhenStartedAsync("cell").WaitAsync(TimeSpan.FromSeconds(5));

        await _mesh.PauseAsync("cell");
        await _mesh.StopAsync("cell");

        _mesh.ListActive().ShouldNotContain("cell");
    }

    [Test]
    public async Task ResumeAsync_NotPaused_NoOp()
    {
        await _mesh.StartAsync("cell", WorkflowLoops.Park);
        await _mesh.WhenStartedAsync("cell").WaitAsync(TimeSpan.FromSeconds(5));

        // Should not throw
        await Should.NotThrowAsync(() => _mesh.ResumeAsync("cell"));
    }

    [Test]
    public async Task PauseAsync_UnknownCell_NoOp()
    {
        await Should.NotThrowAsync(() => _mesh.PauseAsync("nonexistent"));
    }

    [Test]
    public async Task ResumeAsync_UnknownCell_NoOp()
    {
        await Should.NotThrowAsync(() => _mesh.ResumeAsync("nonexistent"));
    }
}
