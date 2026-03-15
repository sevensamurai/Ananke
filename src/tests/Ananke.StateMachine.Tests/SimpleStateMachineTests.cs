using Shouldly;

namespace Ananke.StateMachine.Tests;

// ── Enums for simplified machine tests (reuse-safe names) ────────

enum SimplePhase { Searching, Paperwork, Done }
enum SimpleAction { StartPaperwork, Complete, Interrupt, Resume }

[TestFixture]
public class SimpleStateMachineTests
{
    // ══════════════════════════════════════════════════════════════
    //  Phase 1: Factory + FireAsync + guards + interrupt stack
    // ══════════════════════════════════════════════════════════════

    [Test]
    public void Create_ReturnsInstanceWithInitialState()
    {
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork));

        machine.CurrentState.ShouldBe(SimplePhase.Searching);
        machine.IsInterrupted.ShouldBeFalse();
    }

    [Test]
    public async Task FireAsync_ValidTransition_ChangesState()
    {
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork)
                .From(SimplePhase.Paperwork).On(SimpleAction.Complete).To(SimplePhase.Done));

        var result = await machine.FireAsync(SimpleAction.StartPaperwork);

        result.Success.ShouldBeTrue();
        result.PreviousState.ShouldBe(SimplePhase.Searching);
        result.CurrentState.ShouldBe(SimplePhase.Paperwork);
        machine.CurrentState.ShouldBe(SimplePhase.Paperwork);
    }

    [Test]
    public async Task FireAsync_InvalidTransition_ReturnsFailure()
    {
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork),
            new StateMachineOptions { AllowImplicitSelfTransitions = false });

        var result = await machine.FireAsync(SimpleAction.Complete);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("Invalid transition");
        machine.CurrentState.ShouldBe(SimplePhase.Searching);
    }

    [Test]
    public async Task FireAsync_GuardRejects_ReturnsGuardFailed()
    {
        var allow = false;
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork)
                    .When(() => allow));

        var result = await machine.FireAsync(SimpleAction.StartPaperwork);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("guard");
        machine.CurrentState.ShouldBe(SimplePhase.Searching);
    }

    [Test]
    public async Task FireAsync_GuardPasses_TransitionsSuccessfully()
    {
        var allow = true;
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork)
                    .When(() => allow));

        var result = await machine.FireAsync(SimpleAction.StartPaperwork);

        result.Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(SimplePhase.Paperwork);
    }

    [Test]
    public async Task FireAsync_InterruptTransition_PushesStackAndCarriesPayload()
    {
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.Interrupt).ToInterrupt(SimplePhase.Searching)
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume());

        var result = await machine.FireAsync(SimpleAction.Interrupt, "my payload");

        result.Success.ShouldBeTrue();
        result.WasInterrupt.ShouldBeTrue();
        result.InterruptPayload.ShouldBe("my payload");
        machine.IsInterrupted.ShouldBeTrue();
    }

    [Test]
    public async Task FireAsync_ResumeTransition_PopsStack()
    {
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.Interrupt).ToInterrupt(SimplePhase.Searching)
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume());

        await machine.FireAsync(SimpleAction.Interrupt, "payload");
        machine.IsInterrupted.ShouldBeTrue();

        var result = await machine.FireAsync(SimpleAction.Resume);

        result.Success.ShouldBeTrue();
        result.WasResume.ShouldBeTrue();
        machine.IsInterrupted.ShouldBeFalse();
        machine.CurrentState.ShouldBe(SimplePhase.Searching);
    }

    [Test]
    public async Task FireAsync_ResumeWithEmptyStack_Fails()
    {
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume());

        var result = await machine.FireAsync(SimpleAction.Resume);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("empty");
    }

    [Test]
    public async Task FireAsync_ExceedsMaxInterruptDepth_Fails()
    {
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.Interrupt).ToInterrupt(SimplePhase.Searching)
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume(),
            new StateMachineOptions { MaxInterruptDepth = 2 });

        (await machine.FireAsync(SimpleAction.Interrupt)).Success.ShouldBeTrue();
        (await machine.FireAsync(SimpleAction.Interrupt)).Success.ShouldBeTrue();
        var result = await machine.FireAsync(SimpleAction.Interrupt);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("depth");
    }

    [Test]
    public async Task FireAsync_MultipleTransitions_ChainCorrectly()
    {
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork)
                .From(SimplePhase.Paperwork).On(SimpleAction.Complete).To(SimplePhase.Done));

        await machine.FireAsync(SimpleAction.StartPaperwork);
        var result = await machine.FireAsync(SimpleAction.Complete);

        result.Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(SimplePhase.Done);
    }

    // ══════════════════════════════════════════════════════════════
    //  Phase 2: OnEnter with CancellationToken + CTS lifecycle
    // ══════════════════════════════════════════════════════════════

    [Test]
    public async Task OnEnter_WorkStartsWhenStateIsEntered()
    {
        var entered = new TaskCompletionSource();

        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork))
            .OnEnter(SimplePhase.Paperwork, async ct =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            });

        await machine.FireAsync(SimpleAction.StartPaperwork);

        // Work should have started
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
#pragma warning disable CS4014
        machine.CurrentWork.ShouldNotBeNull();
#pragma warning restore CS4014
    }

    [Test]
    public async Task OnEnter_WorkIsCancelledWhenStatExits()
    {
        var started = new TaskCompletionSource();
        var cancelled = new TaskCompletionSource();

        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork)
                .From(SimplePhase.Paperwork).On(SimpleAction.Complete).To(SimplePhase.Done))
            .OnEnter(SimplePhase.Paperwork, async ct =>
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    cancelled.SetResult();
                    throw;
                }
            });

        await machine.FireAsync(SimpleAction.StartPaperwork);

        // Wait for work to actually start before transitioning out
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Transition out — should cancel the work
        await machine.FireAsync(SimpleAction.Complete);

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        machine.CurrentState.ShouldBe(SimplePhase.Done);
    }

    [Test]
    public async Task OnEnter_InterruptCancelsCurrentWorkAndRestartsOnReenter()
    {
        var enterCount = 0;
        var cancelCount = 0;

        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.Interrupt).ToInterrupt(SimplePhase.Searching)
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume())
            .OnEnter(SimplePhase.Searching, async ct =>
            {
                Interlocked.Increment(ref enterCount);
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancelCount);
                    throw;
                }
            });

        // Start initial work (machine starts in Searching, but OnEnter only fires on transition)
        // Manually trigger by interrupting — this pushes Searching, goes to Searching,
        // cancels old work, starts new OnEnter
        await machine.FireAsync(SimpleAction.Interrupt);

        // Wait for OnEnter to start
        await Task.Delay(100);
        enterCount.ShouldBe(1);

        // Interrupt again — should cancel first OnEnter, start second
        await machine.FireAsync(SimpleAction.Interrupt);

        await Task.Delay(100);
        enterCount.ShouldBe(2);
        cancelCount.ShouldBe(1);
    }

    [Test]
    public async Task OnEnter_CompletedWorkIsObservableViaCurrentWork()
    {
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork))
            .OnEnter(SimplePhase.Paperwork, async _ =>
            {
                await Task.Delay(50);
                // work completes normally
            });

        await machine.FireAsync(SimpleAction.StartPaperwork);

        // Should be able to await the work
#pragma warning disable CS4014
        machine.CurrentWork.ShouldNotBeNull();
#pragma warning restore CS4014
        await machine.CurrentWork!.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task OnExit_RunsBeforeEnteringNewState()
    {
        var log = new List<string>();

        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork))
            .OnExit(SimplePhase.Searching, async () =>
            {
                log.Add("exit-searching");
                await Task.CompletedTask;
            })
            .OnEnter(SimplePhase.Paperwork, async _ =>
            {
                log.Add("enter-paperwork");
                await Task.CompletedTask;
            });

        await machine.FireAsync(SimpleAction.StartPaperwork);

        // Wait for background work to complete
        if (machine.CurrentWork is not null)
            await machine.CurrentWork.WaitAsync(TimeSpan.FromSeconds(2));

        log.ShouldBe(["exit-searching", "enter-paperwork"]);
    }

    [Test]
    public async Task OnEnter_FluentChaining_Works()
    {
        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
                SimplePhase.Searching, b => b
                    .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork)
                    .From(SimplePhase.Paperwork).On(SimpleAction.Complete).To(SimplePhase.Done))
            .OnEnter(SimplePhase.Searching, async ct => await Task.Delay(10, ct))
            .OnEnter(SimplePhase.Paperwork, async ct => await Task.Delay(10, ct))
            .OnExit(SimplePhase.Searching, () => Task.CompletedTask);

        machine.ShouldBeAssignableTo<IStateMachine<SimplePhase, SimpleAction>>();
    }

    // ══════════════════════════════════════════════════════════════
    //  Comparison: same protocol, old way vs new way
    // ══════════════════════════════════════════════════════════════

    [Test]
    public async Task Create_MatchesAbstractStateMachineBehavior()
    {
        // New way — 5 lines, no types
        var simple = StateMachine.Create<Light, LightAction>(Light.Off, b => b
            .From(Light.Off).On(LightAction.TurnOn).To(Light.On)
            .From(Light.On).On(LightAction.TurnOff).To(Light.Off)
            .From(Light.On).On(LightAction.Blink).To(Light.Blinking)
            .From(Light.Blinking).On(LightAction.Stabilize).To(Light.On));

        // Same transitions as LightMachine (TestFixtures.cs) which needs
        // ~20 lines: enum + record + class + overrides

        (await simple.FireAsync(LightAction.TurnOn)).Success.ShouldBeTrue();
        simple.CurrentState.ShouldBe(Light.On);

        (await simple.FireAsync(LightAction.Blink)).Success.ShouldBeTrue();
        simple.CurrentState.ShouldBe(Light.Blinking);

        (await simple.FireAsync(LightAction.Stabilize)).Success.ShouldBeTrue();
        simple.CurrentState.ShouldBe(Light.On);

        (await simple.FireAsync(LightAction.TurnOff)).Success.ShouldBeTrue();
        simple.CurrentState.ShouldBe(Light.Off);
    }

    // ══════════════════════════════════════════════════════════════
    //  Phase 3: OnInterrupt — callback + IInterruptSink
    // ══════════════════════════════════════════════════════════════

    [Test]
    public async Task OnInterrupt_CallbackFiresOnSuccessfulInterrupt()
    {
        object? receivedPayload = null;
        var callbackFired = false;

        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.Interrupt).ToInterrupt(SimplePhase.Searching)
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume())
            .OnInterrupt((payload, _) =>
            {
                callbackFired = true;
                receivedPayload = payload;
                return Task.CompletedTask;
            });

        await machine.FireAsync(SimpleAction.Interrupt, "my-payload");

        callbackFired.ShouldBeTrue();
        receivedPayload.ShouldBe("my-payload");
    }

    [Test]
    public async Task OnInterrupt_DoesNotFireOnNormalTransition()
    {
        var callbackFired = false;

        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork)
                .From(SimplePhase.Searching).On(SimpleAction.Interrupt).ToInterrupt(SimplePhase.Searching)
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume())
            .OnInterrupt((_, _) =>
            {
                callbackFired = true;
                return Task.CompletedTask;
            });

        await machine.FireAsync(SimpleAction.StartPaperwork);

        callbackFired.ShouldBeFalse();
    }

    [Test]
    public async Task OnInterrupt_DoesNotFireWhenGuardRejects()
    {
        var callbackFired = false;

        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.Interrupt).ToInterrupt(SimplePhase.Searching)
                    .When(() => false)
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume())
            .OnInterrupt((_, _) =>
            {
                callbackFired = true;
                return Task.CompletedTask;
            });

        var result = await machine.FireAsync(SimpleAction.Interrupt, "payload");

        result.Success.ShouldBeFalse();
        callbackFired.ShouldBeFalse();
    }

    [Test]
    public async Task OnInterrupt_CancelsWorkThenDeliversPayload()
    {
        var events = new List<string>();
        var workStarted = new TaskCompletionSource();

        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.Interrupt).ToInterrupt(SimplePhase.Searching)
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume())
            .OnEnter(SimplePhase.Searching, async ct =>
            {
                events.Add("work-started");
                workStarted.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { events.Add("work-cancelled"); throw; }
            })
            .OnInterrupt((payload, _) =>
            {
                events.Add($"interrupt:{payload}");
                return Task.CompletedTask;
            });

        // First interrupt starts the OnEnter work (machine starts in Searching
        // but OnEnter only fires on transition)
        await machine.FireAsync(SimpleAction.Interrupt, "first");
        await workStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Reset for second interrupt
        workStarted = new TaskCompletionSource();

        // Second interrupt — cancels old work, delivers payload, starts new work
        await machine.FireAsync(SimpleAction.Interrupt, "second");
        await workStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Allow background cancellation to complete
        await Task.Delay(50);

        // Both interrupts delivered
        events.ShouldContain("interrupt:first");
        events.ShouldContain("interrupt:second");

        // Old work was cancelled
        events.ShouldContain("work-cancelled");

        // New work started after second interrupt
        events.Count(e => e == "work-started").ShouldBe(2);
    }

    [Test]
    public async Task OnInterrupt_WithSink_DeliversTypedPayload()
    {
        var sink = new TestInterruptSink();

        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.Interrupt).ToInterrupt(SimplePhase.Searching)
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume())
            .OnInterrupt(sink);

        await machine.FireAsync(SimpleAction.Interrupt, "typed-payload");

        sink.ReceivedPayloads.Count.ShouldBe(1);
        sink.ReceivedPayloads[0].ShouldBe("typed-payload");
    }

    [Test]
    public async Task OnInterrupt_WithSink_IgnoresMismatchedPayloadType()
    {
        var sink = new TestInterruptSink();

        var machine = StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.Interrupt).ToInterrupt(SimplePhase.Searching)
                .From(SimplePhase.Searching).On(SimpleAction.Resume).ToResume())
            .OnInterrupt(sink);

        // Pass an int payload when sink expects string — should not throw
        await machine.FireAsync(SimpleAction.Interrupt, 42);

        sink.ReceivedPayloads.ShouldBeEmpty();
    }

    private sealed class TestInterruptSink : Ananke.Abstractions.IInterruptSink<string>
    {
        public List<string> ReceivedPayloads { get; } = [];

        public Task InterruptAsync(string payload, CancellationToken ct = default)
        {
            ReceivedPayloads.Add(payload);
            return Task.CompletedTask;
        }
    }
}
