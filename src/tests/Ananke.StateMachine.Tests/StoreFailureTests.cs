using Ananke.Abstractions.Distributed;
using Ananke.StateMachine.Middleware;
using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class StoreFailureTests
{
    private InMemoryDistributedLock _lock = new();
    private FailingStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _lock = new InMemoryDistributedLock();
        _store = new FailingStore(_lock);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _store.DisposeAsync();
        await _lock.DisposeAsync();
    }

    [Test]
    public async Task TransitionAsync_StoreReadThrows_FailsAndWritesNothing()
    {
        var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };
        var machine = new LightMachine(_lock, _store, options);
        var ctx = new TestContext("1");
        _store.FailReads = true;

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeFalse();
        result.Exception.ShouldBeOfType<StoreUnavailableException>();
        _store.WriteCount.ShouldBe(0);
        (await _lock.ExistsAsync(machine.GetContextKey(ctx.Id))).ShouldBeFalse();
    }

    [Test]
    public async Task TransitionAsync_StoreReadThrowsWithImplicitSelfTransitions_FailsAndWritesNothing()
    {
        var machine = new LightMachine(_lock, _store);
        var ctx = new TestContext("1");
        _store.FailReads = true;

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOff);

        result.Success.ShouldBeFalse();
        _store.WriteCount.ShouldBe(0);
    }

    [Test]
    public async Task TransitionAsync_StoreBackAfterReadFailure_StateUnchanged()
    {
        var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };
        var machine = new LightMachine(_lock, _store, options);
        var ctx = new TestContext("1");
        (await machine.TransitionAsync(ctx, LightAction.TurnOn)).Success.ShouldBeTrue();
        (await machine.TransitionAsync(ctx, LightAction.Blink)).Success.ShouldBeTrue();

        _store.FailReads = true;
        (await machine.TransitionAsync(ctx, LightAction.Stabilize)).Success.ShouldBeFalse();
        _store.FailReads = false;

        var stored = await _lock.GetValueAsync<LightMachine.PersistedContext<Light>>(machine.GetContextKey(ctx.Id));
        stored.ShouldNotBeNull();
        stored.State.ShouldBe(Light.Blinking);
        stored.Step.ShouldBe(2);

        // A transition that is only valid from the initial state is still refused.
        var fromInitial = await machine.TransitionAsync(ctx, LightAction.TurnOn);
        fromInitial.Success.ShouldBeFalse();
        fromInitial.CurrentState.ShouldBe(Light.Blinking);
    }

    [Test]
    public async Task TransitionAsync_MissingKey_StartsAtInitialState()
    {
        var machine = new LightMachine(_lock, _store);
        var ctx = new TestContext("never-seen");

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeTrue();
        result.PreviousState.ShouldBe(Light.Off);
        result.CurrentState.ShouldBe(Light.On);
    }

    [Test]
    public async Task TransitionAsync_StoreWriteThrows_FailsAndCurrentStateUnchanged()
    {
        var machine = new LightMachine(_lock, _store);
        var ctx = new TestContext("1");
        _store.FailWrites = true;

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeFalse();
        result.CurrentState.ShouldBe(Light.Off);
        result.StateStored.ShouldBeFalse();
        (await _lock.ExistsAsync(machine.GetContextKey(ctx.Id))).ShouldBeFalse();
    }

    [Test]
    public async Task FaultAsync_StoreReadThrows_ThrowsAndStatusUnchanged()
    {
        var machine = new LightMachine(_lock, _store);
        var ctx = new TestContext("1");
        (await machine.TransitionAsync(ctx, LightAction.TurnOn)).Success.ShouldBeTrue();
        _store.FailReads = true;

        await Should.ThrowAsync<StoreUnavailableException>(() => machine.FaultAsync(ctx, "sensor"));

        _store.FailReads = false;
        var stored = await _lock.GetValueAsync<LightMachine.PersistedContext<Light>>(machine.GetContextKey(ctx.Id));
        stored.ShouldNotBeNull();
        stored.State.ShouldBe(Light.On);
        stored.OperationalStatus.ShouldBe(OperationalStatus.Operative);
    }

    [Test]
    public async Task ResetAsync_StoreReadThrows_ThrowsAndStaysFaulted()
    {
        var machine = new LightMachine(_lock, _store);
        var ctx = new TestContext("1");
        await machine.FaultAsync(ctx, "sensor");
        _store.FailReads = true;

        await Should.ThrowAsync<StoreUnavailableException>(() => machine.ResetAsync(ctx, "replaced"));

        _store.FailReads = false;
        var status = await machine.GetStatusAsync(ctx);
        status.OperationalStatus.ShouldBe(OperationalStatus.Faulted);
        status.OperationalStatusReason.ShouldBe("sensor");
    }

    [Test]
    public async Task TransitionAsync_MiddlewareRetriesAfterFailedWrite_RetryStartsFromStoredState()
    {
        var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };
        var machine = new LightMachine(_lock, _store, options);
        var ctx = new TestContext("1");
        machine.UseMiddleware(new RetryOnceMiddleware(() => _store.FailWrites = false));
        _store.FailWrites = true;

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.PreviousState.ShouldBe(Light.Off);
        result.CurrentState.ShouldBe(Light.On);
        var stored = await _lock.GetValueAsync<LightMachine.PersistedContext<Light>>(machine.GetContextKey(ctx.Id));
        stored.ShouldNotBeNull();
        stored.Step.ShouldBe(1);
    }

    private sealed class StoreUnavailableException() : Exception("store unavailable");

    /// <summary>
    /// Calls the rest of the pipeline again when the store throws.
    /// </summary>
    private sealed class RetryOnceMiddleware(Action beforeRetry) : ITransitionMiddleware<TestContext, Light, LightAction>
    {
        public async Task<TransitionResult<Light>> InvokeAsync(
            TestContext context, LightAction transition, Light currentState,
            Func<CancellationToken, Task<TransitionResult<Light>>> next, CancellationToken ct)
        {
            try
            {
                return await next(ct);
            }
            catch (StoreUnavailableException)
            {
                beforeRetry();
                return await next(ct);
            }
        }
    }

    /// <summary>
    /// Wraps an in-memory store; reads or writes throw while their flag is set.
    /// </summary>
    private sealed class FailingStore(InMemoryDistributedLock inner) : IKeyValueDataAdapter
    {
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }
        public int WriteCount { get; private set; }

        public Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
            FailReads ? throw new StoreUnavailableException() : inner.GetValueAsync(key, ct);

        public Task<T?> GetValueAsync<T>(string key, CancellationToken ct = default) =>
            FailReads ? throw new StoreUnavailableException() : inner.GetValueAsync<T>(key, ct);

        public Task SetValueAsync(string key, string value, TimeSpan? expiry, CancellationToken ct = default)
        {
            WriteCount++;
            return FailWrites ? throw new StoreUnavailableException() : inner.SetValueAsync(key, value, expiry, ct);
        }

        public Task SetValueAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken ct = default)
        {
            WriteCount++;
            return FailWrites ? throw new StoreUnavailableException() : inner.SetValueAsync(key, value, expiry, ct);
        }

        public Task<bool> RemoveAsync(string key, CancellationToken ct = default) =>
            FailWrites ? throw new StoreUnavailableException() : inner.RemoveAsync(key, ct);

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            FailReads ? throw new StoreUnavailableException() : inner.ExistsAsync(key, ct);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
