using Ananke.Abstractions.Distributed;
using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class OperationalStatusTests
{
    private InMemoryDistributedLock _lock = new();

    [TearDown]
    public ValueTask TearDown() => _lock.DisposeAsync();

    [SetUp]
    public void SetUp() => _lock = new InMemoryDistributedLock();

    // ── Default status ───────────────────────────────────────────────

    [Test]
    public async Task NewMachine_StatusIsOperative()
    {
        var machine = new LightMachine(_lock, _lock);
        var ctx = new TestContext("1");

        (await machine.GetStatusAsync(ctx)).OperationalStatus.ShouldBe(OperationalStatus.Operative);
        (await machine.GetStatusAsync(ctx)).OperationalStatusReason.ShouldBeNull();
    }

    // ── Fault ────────────────────────────────────────────────────────

    [Test]
    public async Task FaultAsync_SetsStatusToFaulted()
    {
        var machine = new LightMachine(_lock, _lock);
        var ctx = new TestContext("1");

        var change = await machine.FaultAsync(ctx, "Hardware failure");

        change.Success.ShouldBeTrue();
        change.PreviousStatus.ShouldBe(OperationalStatus.Operative);
        change.CurrentStatus.ShouldBe(OperationalStatus.Faulted);
        change.Reason.ShouldBe("Hardware failure");

        (await machine.GetStatusAsync(ctx)).OperationalStatus.ShouldBe(OperationalStatus.Faulted);
        (await machine.GetStatusAsync(ctx)).OperationalStatusReason.ShouldBe("Hardware failure");
    }

    [Test]
    public async Task FaultAsync_AlreadyFaulted_ReturnsFalse()
    {
        var machine = new LightMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.FaultAsync(ctx, "First fault");
        var change = await machine.FaultAsync(ctx, "Second fault");

        change.Success.ShouldBeFalse();
        change.PreviousStatus.ShouldBe(OperationalStatus.Faulted);
        change.CurrentStatus.ShouldBe(OperationalStatus.Faulted);
    }

    [Test]
    public async Task FaultAsync_BlocksSubsequentTransitions()
    {
        var machine = new LightMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.FaultAsync(ctx, "Broken");
        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("Faulted");
    }

    // ── Reset ────────────────────────────────────────────────────────

    [Test]
    public async Task ResetAsync_RestoresOperativeStatus()
    {
        var machine = new LightMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.FaultAsync(ctx, "Broken");
        var change = await machine.ResetAsync(ctx, "Repaired");

        change.Success.ShouldBeTrue();
        change.PreviousStatus.ShouldBe(OperationalStatus.Faulted);
        change.CurrentStatus.ShouldBe(OperationalStatus.Operative);
        change.Reason.ShouldBe("Repaired");

        (await machine.GetStatusAsync(ctx)).OperationalStatus.ShouldBe(OperationalStatus.Operative);
        (await machine.GetStatusAsync(ctx)).OperationalStatusReason.ShouldBeNull();
    }

    [Test]
    public async Task ResetAsync_AlreadyOperative_ReturnsFalse()
    {
        var machine = new LightMachine(_lock, _lock);
        var ctx = new TestContext("1");

        var change = await machine.ResetAsync(ctx, "Unnecessary reset");

        change.Success.ShouldBeFalse();
        change.Reason.ShouldBe("Already operative");
    }

    [Test]
    public async Task ResetAsync_AllowsTransitionsAgain()
    {
        var machine = new LightMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.FaultAsync(ctx, "Broken");
        await machine.ResetAsync(ctx, "Fixed");

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);
        result.Success.ShouldBeTrue();
    }

    // ── Persistence of operational status ────────────────────────────

    [Test]
    public async Task FaultAsync_PersistsStatusToDistributedStore()
    {
        var machine = new LightMachine(_lock, _lock);
        var ctx = new TestContext("1");

        // Transition to create persisted state, then fault
        await machine.TransitionAsync(ctx, LightAction.TurnOn);
        await machine.FaultAsync(ctx, "Hardware error");

        // A second instance over the same store reads the stored status.
        var machine2 = new LightMachine(_lock, _lock);
        var result = await machine2.TransitionAsync(ctx, LightAction.TurnOff);

        result.Success.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("Hardware error");
        (await machine2.GetStatusAsync(ctx)).OperationalStatus.ShouldBe(OperationalStatus.Faulted);
    }

    [Test]
    public async Task ResetAsync_PersistsStatusToDistributedStore()
    {
        var machine = new LightMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.FaultAsync(ctx, "Broken");
        await machine.ResetAsync(ctx, "Fixed");

        // After reset, transitions should work
        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);
        result.Success.ShouldBeTrue();
        (await machine.GetStatusAsync(ctx)).OperationalStatus.ShouldBe(OperationalStatus.Operative);
    }
}
