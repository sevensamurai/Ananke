using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Distributed;
using Ananke.StateMachine.Channels;
using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class StateMachineChannelWorkerTests
{
    private InMemoryDistributedLock _lock = new();

    [SetUp]
    public void SetUp() => _lock = new InMemoryDistributedLock();

    [TearDown]
    public ValueTask TearDown() => _lock.DisposeAsync();

    // ── HandleAsync: null context ─────────────────────────────────────

    [Test]
    public async Task HandleAsync_NullContext_SkipsTransitionLeavesStateUnchanged()
    {
        var machine = new LightMachine(_lock, _lock);
        await using var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(machine);

        await worker.HandleAsync(null, LightAction.TurnOn, CancellationToken.None);

        machine.CurrentState.ShouldBe(Light.Off);
    }

    [Test]
    public async Task HandleAsync_NullContext_DoesNotInvokeOnTransition()
    {
        var machine = new LightMachine(_lock, _lock);
        var called = false;
        await using var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(machine)
        {
            OnTransition = (_, _, _) => called = true
        };

        await worker.HandleAsync(null, LightAction.TurnOn, CancellationToken.None);

        called.ShouldBeFalse();
    }

    // ── HandleAsync: successful transition ───────────────────────────

    [Test]
    public async Task HandleAsync_ValidTransition_DispatchesToMachine()
    {
        var machine = new LightMachine(_lock, _lock);
        await using var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(machine);

        await worker.HandleAsync(new TestContext("1"), LightAction.TurnOn, CancellationToken.None);

        machine.CurrentState.ShouldBe(Light.On);
    }

    [Test]
    public async Task HandleAsync_SuccessfulTransition_InvokesOnTransitionWithSuccess()
    {
        var machine = new LightMachine(_lock, _lock);
        TransitionResult<Light>? captured = null;
        await using var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(machine)
        {
            OnTransition = (_, _, result) => captured = result
        };

        await worker.HandleAsync(new TestContext("1"), LightAction.TurnOn, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Success.ShouldBeTrue();
        captured!.CurrentState.ShouldBe(Light.On);
    }

    // ── HandleAsync: failed transition ───────────────────────────────

    [Test]
    public async Task HandleAsync_InvalidTransition_InvokesOnTransitionWithFailure()
    {
        var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };
        var machine = new LightMachine(_lock, _lock, options);
        TransitionResult<Light>? captured = null;
        await using var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(machine)
        {
            OnTransition = (_, _, result) => captured = result
        };

        await worker.HandleAsync(new TestContext("1"), LightAction.TurnOff, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Success.ShouldBeFalse();
    }

    [Test]
    public async Task HandleAsync_InvalidTransition_StateUnchanged()
    {
        var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };
        var machine = new LightMachine(_lock, _lock, options);
        await using var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(machine);

        await worker.HandleAsync(new TestContext("1"), LightAction.TurnOff, CancellationToken.None);

        machine.CurrentState.ShouldBe(Light.Off);
    }

    // ── HandleAsync: post-transition worker ──────────────────────────

    [Test]
    public async Task HandleAsync_WithPostWorker_EnqueuesTransitionEvent()
    {
        var machine = new LightMachine(_lock, _lock);
        var received = new TaskCompletionSource<TransitionEvent<TestContext, Light, LightAction>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(
            machine, new OnceWorker(received));

        await worker.HandleAsync(new TestContext("pw-1"), LightAction.TurnOn, CancellationToken.None);

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        evt.Transition.ShouldBe(LightAction.TurnOn);
        evt.Result.Success.ShouldBeTrue();
        evt.Context.Id.ShouldBe("pw-1");
    }

    [Test]
    public async Task HandleAsync_WithPostWorker_IncludesFailedResults()
    {
        var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };
        var machine = new LightMachine(_lock, _lock, options);
        var received = new TaskCompletionSource<TransitionEvent<TestContext, Light, LightAction>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(
            machine, new OnceWorker(received));

        await worker.HandleAsync(new TestContext("pw-2"), LightAction.TurnOff, CancellationToken.None);

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        evt.Result.Success.ShouldBeFalse();
    }

    // ── DisposeAsync ─────────────────────────────────────────────────

    [Test]
    public async Task DisposeAsync_WithoutPostWorker_CompletesCleanly()
    {
        var machine = new LightMachine(_lock, _lock);
        var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(machine);

        await Should.NotThrowAsync(() => worker.DisposeAsync().AsTask());
    }

    [Test]
    public async Task DisposeAsync_WithPostWorker_CompletesCleanly()
    {
        var machine = new LightMachine(_lock, _lock);
        var worker = new StateMachineChannelWorker<TestContext, Light, LightAction, LightNotify>(
            machine, new NoOpPostWorker());

        await Should.NotThrowAsync(() => worker.DisposeAsync().AsTask());
    }

    // ── TransitionEvent record ────────────────────────────────────────

    [Test]
    public void TransitionEvent_PropertiesRoundTrip()
    {
        var ctx = new TestContext("42");
        var result = TransitionResult<Light>.Succeeded(Light.Off, Light.On);
        var evt = new TransitionEvent<TestContext, Light, LightAction>
        {
            Context = ctx,
            Transition = LightAction.TurnOn,
            Result = result
        };

        evt.Context.ShouldBe(ctx);
        evt.Transition.ShouldBe(LightAction.TurnOn);
        evt.Result.Success.ShouldBeTrue();
    }

    // ── Test helpers ─────────────────────────────────────────────────

    private sealed class OnceWorker(
        TaskCompletionSource<TransitionEvent<TestContext, Light, LightAction>> tcs)
        : IBackgroundWorker<TransitionEvent<TestContext, Light, LightAction>>
    {
        public Task HandleAsync(TransitionEvent<TestContext, Light, LightAction>? item, CancellationToken token)
        {
            if (item is not null)
                tcs.TrySetResult(item);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpPostWorker
        : IBackgroundWorker<TransitionEvent<TestContext, Light, LightAction>>
    {
        public Task HandleAsync(TransitionEvent<TestContext, Light, LightAction>? item, CancellationToken token)
            => Task.CompletedTask;
    }
}
