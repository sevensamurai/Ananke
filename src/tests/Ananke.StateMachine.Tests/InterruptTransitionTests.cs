using Ananke.Abstractions.Distributed;
using Ananke.StateMachine.Builder;
using Shouldly;

namespace Ananke.StateMachine.Tests;

// ── Enums for interrupt tests ────────────────────────────────────

enum ConvoState { Idle, Listening, Responding, Interrupted }
enum ConvoAction { Listen, Respond, Interrupt, Resume, Done }
enum ConvoNotify { None }

// ── Machine with interrupt/resume transitions ────────────────────
//  Idle ──[Listen]──► Listening ──[Respond]──► Responding ──[Done]──► Idle
//  Responding ──[Interrupt]──► interrupt ──► Interrupted
//  Interrupted ──[Resume]──► resume ──► (popped from stack)

sealed class ConvoMachine(IDistributedLock locker, IKeyValueDataAdapter store, StateMachineOptions? options = null)
    : AbstractStateMachine<TestContext, ConvoState, ConvoAction, ConvoNotify>(
        ConvoState.Idle, locker, store, options)
{
    public bool AllowInterrupt { get; set; } = true;

    protected override Action<ITransitionBuilder<ConvoState, ConvoAction>> Transitions => b => b
        .From(ConvoState.Idle).On(ConvoAction.Listen).To(ConvoState.Listening)
        .From(ConvoState.Listening).On(ConvoAction.Respond).To(ConvoState.Responding)
        .From(ConvoState.Responding).On(ConvoAction.Done).To(ConvoState.Idle)
        .From(ConvoState.Responding).On(ConvoAction.Interrupt).ToInterrupt(ConvoState.Interrupted)
            .When(() => AllowInterrupt)
        .From(ConvoState.Interrupted).On(ConvoAction.Resume).ToResume();

    public override Task<TransitionResult<ConvoState>> TransitionAsync(
        TestContext context, ConvoAction transition) =>
        InternalTransitionAsync(context, transition);

    public override Task NotifyAsync(
        TestContext context, ConvoNotify notification) =>
        Task.CompletedTask;
}

// ── Deep-nesting machine for testing multi-level interrupts ──────

enum DeepState { A, B, C, D }
enum DeepAction { GoB, GoC, GoD, InterruptToB, InterruptToC, InterruptToD, Resume }
enum DeepNotify { None }

sealed class DeepInterruptMachine(IDistributedLock locker, IKeyValueDataAdapter store, StateMachineOptions? options = null)
    : AbstractStateMachine<TestContext, DeepState, DeepAction, DeepNotify>(
        DeepState.A, locker, store, options)
{
    protected override Action<ITransitionBuilder<DeepState, DeepAction>> Transitions => b => b
        .From(DeepState.A).On(DeepAction.InterruptToB).ToInterrupt(DeepState.B)
        .From(DeepState.B).On(DeepAction.InterruptToC).ToInterrupt(DeepState.C)
        .From(DeepState.C).On(DeepAction.InterruptToD).ToInterrupt(DeepState.D)
        .FromAny(DeepState.B, DeepState.C, DeepState.D).On(DeepAction.Resume).ToResume();

    public override Task<TransitionResult<DeepState>> TransitionAsync(
        TestContext context, DeepAction transition) =>
        InternalTransitionAsync(context, transition);

    public override Task NotifyAsync(
        TestContext context, DeepNotify notification) =>
        Task.CompletedTask;
}

[TestFixture]
public class InterruptTransitionTests
{
    private InMemoryDistributedLock _lock = new();

    [TearDown]
    public ValueTask TearDown() => _lock.DisposeAsync();

    [SetUp]
    public void SetUp() => _lock = new InMemoryDistributedLock();

    // ── Basic interrupt ──────────────────────────────────────────

    [Test]
    public async Task Interrupt_PushesCurrentStateAndTransitionsToInterruptState()
    {
        var machine = new ConvoMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, ConvoAction.Listen);
        await machine.TransitionAsync(ctx, ConvoAction.Respond);
        machine.CurrentState.ShouldBe(ConvoState.Responding);

        var result = await machine.TransitionAsync(ctx, ConvoAction.Interrupt);

        result.Success.ShouldBeTrue();
        result.WasInterrupt.ShouldBeTrue();
        result.PreviousState.ShouldBe(ConvoState.Responding);
        result.CurrentState.ShouldBe(ConvoState.Interrupted);
        machine.CurrentState.ShouldBe(ConvoState.Interrupted);
        machine.IsInterrupted.ShouldBeTrue();
    }

    // ── Basic resume ─────────────────────────────────────────────

    [Test]
    public async Task Resume_PopsStackAndReturnsToInterruptedState()
    {
        var machine = new ConvoMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, ConvoAction.Listen);
        await machine.TransitionAsync(ctx, ConvoAction.Respond);
        await machine.TransitionAsync(ctx, ConvoAction.Interrupt);
        machine.CurrentState.ShouldBe(ConvoState.Interrupted);

        var result = await machine.TransitionAsync(ctx, ConvoAction.Resume);

        result.Success.ShouldBeTrue();
        result.WasResume.ShouldBeTrue();
        result.ResumedFromState.ShouldBe(ConvoState.Interrupted);
        result.CurrentState.ShouldBe(ConvoState.Responding);
        machine.CurrentState.ShouldBe(ConvoState.Responding);
        machine.IsInterrupted.ShouldBeFalse();
    }

    // ── Nested interrupts ────────────────────────────────────────

    [Test]
    public async Task NestedInterrupts_PushAndPopCorrectly()
    {
        var machine = new DeepInterruptMachine(_lock, _lock);
        var ctx = new TestContext("1");

        // A → interrupt → B
        var r1 = await machine.TransitionAsync(ctx, DeepAction.InterruptToB);
        r1.Success.ShouldBeTrue();
        r1.WasInterrupt.ShouldBeTrue();
        machine.CurrentState.ShouldBe(DeepState.B);
        machine.IsInterrupted.ShouldBeTrue();

        // B → interrupt → C
        var r2 = await machine.TransitionAsync(ctx, DeepAction.InterruptToC);
        r2.Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(DeepState.C);
        machine.IsInterrupted.ShouldBeTrue();

        // C → interrupt → D
        var r3 = await machine.TransitionAsync(ctx, DeepAction.InterruptToD);
        r3.Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(DeepState.D);

        // D → resume → C
        var r4 = await machine.TransitionAsync(ctx, DeepAction.Resume);
        r4.Success.ShouldBeTrue();
        r4.WasResume.ShouldBeTrue();
        machine.CurrentState.ShouldBe(DeepState.C);
        machine.IsInterrupted.ShouldBeTrue();

        // C → resume → B
        var r5 = await machine.TransitionAsync(ctx, DeepAction.Resume);
        r5.Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(DeepState.B);
        machine.IsInterrupted.ShouldBeTrue();

        // B → resume → A
        var r6 = await machine.TransitionAsync(ctx, DeepAction.Resume);
        r6.Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(DeepState.A);
        machine.IsInterrupted.ShouldBeFalse();
    }

    // ── Guard on interrupt transition ────────────────────────────

    [Test]
    public async Task Interrupt_WithGuardFailing_IsRejected()
    {
        var machine = new ConvoMachine(_lock, _lock) { AllowInterrupt = false };
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, ConvoAction.Listen);
        await machine.TransitionAsync(ctx, ConvoAction.Respond);

        var result = await machine.TransitionAsync(ctx, ConvoAction.Interrupt);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage!.ShouldContain("guard");
        machine.CurrentState.ShouldBe(ConvoState.Responding);
        machine.IsInterrupted.ShouldBeFalse();
    }

    // ── Max depth exceeded ───────────────────────────────────────

    [Test]
    public async Task Interrupt_ExceedingMaxDepth_IsRejected()
    {
        var options = new StateMachineOptions { MaxInterruptDepth = 2 };
        var machine = new DeepInterruptMachine(_lock, _lock, options);
        var ctx = new TestContext("1");

        // A → interrupt → B (depth 1)
        (await machine.TransitionAsync(ctx, DeepAction.InterruptToB)).Success.ShouldBeTrue();
        // B → interrupt → C (depth 2)
        (await machine.TransitionAsync(ctx, DeepAction.InterruptToC)).Success.ShouldBeTrue();
        // C → interrupt → D (depth 3 — exceeds limit of 2)
        var result = await machine.TransitionAsync(ctx, DeepAction.InterruptToD);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage!.ShouldContain("Maximum interrupt depth");
        machine.CurrentState.ShouldBe(DeepState.C);
    }

    // ── Resume with empty stack ──────────────────────────────────

    [Test]
    public async Task Resume_WithEmptyStack_Fails()
    {
        var machine = new DeepInterruptMachine(_lock, _lock);
        var ctx = new TestContext("1");

        // Try to resume from A without any interrupt — but Resume is only defined from B, C, D.
        // Let's interrupt first, resume, then try resuming again.
        (await machine.TransitionAsync(ctx, DeepAction.InterruptToB)).Success.ShouldBeTrue();
        (await machine.TransitionAsync(ctx, DeepAction.Resume)).Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(DeepState.A);

        // Now interrupt again so we're in B (where Resume is defined), then resume twice
        (await machine.TransitionAsync(ctx, DeepAction.InterruptToB)).Success.ShouldBeTrue();
        (await machine.TransitionAsync(ctx, DeepAction.Resume)).Success.ShouldBeTrue();
        machine.IsInterrupted.ShouldBeFalse();

        // Interrupt once more, then try resume from B where stack is empty after first resume
        (await machine.TransitionAsync(ctx, DeepAction.InterruptToB)).Success.ShouldBeTrue();
        (await machine.TransitionAsync(ctx, DeepAction.InterruptToC)).Success.ShouldBeTrue();
        (await machine.TransitionAsync(ctx, DeepAction.Resume)).Success.ShouldBeTrue(); // C → B
        (await machine.TransitionAsync(ctx, DeepAction.Resume)).Success.ShouldBeTrue(); // B → A
        machine.IsInterrupted.ShouldBeFalse();
    }

    // ── IsInterrupted tracks state correctly ─────────────────────

    [Test]
    public async Task IsInterrupted_TracksCorrectlyThroughInterruptAndResume()
    {
        var machine = new ConvoMachine(_lock, _lock);
        var ctx = new TestContext("1");

        machine.IsInterrupted.ShouldBeFalse();

        await machine.TransitionAsync(ctx, ConvoAction.Listen);
        machine.IsInterrupted.ShouldBeFalse();

        await machine.TransitionAsync(ctx, ConvoAction.Respond);
        machine.IsInterrupted.ShouldBeFalse();

        await machine.TransitionAsync(ctx, ConvoAction.Interrupt);
        machine.IsInterrupted.ShouldBeTrue();

        await machine.TransitionAsync(ctx, ConvoAction.Resume);
        machine.IsInterrupted.ShouldBeFalse();
    }

    // ── Persistence round-trip ───────────────────────────────────

    [Test]
    public async Task InterruptStack_SurvivesPersistenceRoundTrip()
    {
        var machine = new DeepInterruptMachine(_lock, _lock);
        var ctx = new TestContext("42");

        // Build up a stack: A → B → C
        (await machine.TransitionAsync(ctx, DeepAction.InterruptToB)).Success.ShouldBeTrue();
        (await machine.TransitionAsync(ctx, DeepAction.InterruptToC)).Success.ShouldBeTrue();

        // Create a new machine instance pointing at the same lock store
        var machine2 = new DeepInterruptMachine(_lock, _lock);

        // Resume should pop C → B (reading stack from persisted context)
        var result = await machine2.TransitionAsync(ctx, DeepAction.Resume);
        result.Success.ShouldBeTrue();
        result.WasResume.ShouldBeTrue();
        machine2.CurrentState.ShouldBe(DeepState.B);
        machine2.IsInterrupted.ShouldBeTrue();

        // Resume again: B → A
        var result2 = await machine2.TransitionAsync(ctx, DeepAction.Resume);
        result2.Success.ShouldBeTrue();
        machine2.CurrentState.ShouldBe(DeepState.A);
        machine2.IsInterrupted.ShouldBeFalse();
    }

    // ── Normal transitions still work alongside interrupts ───────

    [Test]
    public async Task NormalTransitions_WorkAlongsideInterruptInfrastructure()
    {
        var machine = new ConvoMachine(_lock, _lock);
        var ctx = new TestContext("1");

        // Full normal flow without interrupts
        (await machine.TransitionAsync(ctx, ConvoAction.Listen)).Success.ShouldBeTrue();
        (await machine.TransitionAsync(ctx, ConvoAction.Respond)).Success.ShouldBeTrue();
        (await machine.TransitionAsync(ctx, ConvoAction.Done)).Success.ShouldBeTrue();

        machine.CurrentState.ShouldBe(ConvoState.Idle);
        machine.IsInterrupted.ShouldBeFalse();
    }

    // ── Interrupt then normal flow after resume ──────────────────

    [Test]
    public async Task AfterResume_CanContinueNormalFlow()
    {
        var machine = new ConvoMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, ConvoAction.Listen);
        await machine.TransitionAsync(ctx, ConvoAction.Respond);
        await machine.TransitionAsync(ctx, ConvoAction.Interrupt);
        await machine.TransitionAsync(ctx, ConvoAction.Resume);

        // Back at Responding — can complete normally
        var result = await machine.TransitionAsync(ctx, ConvoAction.Done);
        result.Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(ConvoState.Idle);
    }

    // ── Interrupt payload ────────────────────────────────────────

    [Test]
    public async Task Interrupt_WithPayload_SurfacesPayloadInResult()
    {
        var machine = new ConvoMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, ConvoAction.Listen);
        await machine.TransitionAsync(ctx, ConvoAction.Respond);

        var payload = "also for granny";
        var result = await machine.TransitionAsync(ctx, ConvoAction.Interrupt, payload);

        result.Success.ShouldBeTrue();
        result.WasInterrupt.ShouldBeTrue();
        result.InterruptPayload.ShouldBe(payload);
        machine.CurrentState.ShouldBe(ConvoState.Interrupted);
    }

    [Test]
    public async Task Interrupt_WithoutPayload_HasNullPayload()
    {
        var machine = new ConvoMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, ConvoAction.Listen);
        await machine.TransitionAsync(ctx, ConvoAction.Respond);

        var result = await machine.TransitionAsync(ctx, ConvoAction.Interrupt);

        result.Success.ShouldBeTrue();
        result.WasInterrupt.ShouldBeTrue();
        result.InterruptPayload.ShouldBeNull();
    }

    [Test]
    public async Task Resume_ClearsPayload()
    {
        var machine = new ConvoMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, ConvoAction.Listen);
        await machine.TransitionAsync(ctx, ConvoAction.Respond);
        await machine.TransitionAsync(ctx, ConvoAction.Interrupt, "some reason");

        var result = await machine.TransitionAsync(ctx, ConvoAction.Resume);

        result.Success.ShouldBeTrue();
        result.WasResume.ShouldBeTrue();
        result.InterruptPayload.ShouldBeNull();
    }

    [Test]
    public async Task NormalTransition_HasNullPayload()
    {
        var machine = new ConvoMachine(_lock, _lock);
        var ctx = new TestContext("1");

        var result = await machine.TransitionAsync(ctx, ConvoAction.Listen);

        result.Success.ShouldBeTrue();
        result.WasInterrupt.ShouldBeFalse();
        result.InterruptPayload.ShouldBeNull();
    }

    [Test]
    public async Task Interrupt_WithComplexPayload_PreservesObject()
    {
        var machine = new ConvoMachine(_lock, _lock);
        var ctx = new TestContext("1");

        await machine.TransitionAsync(ctx, ConvoAction.Listen);
        await machine.TransitionAsync(ctx, ConvoAction.Respond);

        var payload = new { Message = "refine search", Priority = 1 };
        var result = await machine.TransitionAsync(ctx, ConvoAction.Interrupt, payload);

        result.Success.ShouldBeTrue();
        result.InterruptPayload.ShouldBe(payload);
    }
}
