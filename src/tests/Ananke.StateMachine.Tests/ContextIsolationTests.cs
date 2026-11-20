using System.Collections.Concurrent;
using Ananke.Abstractions.Distributed;
using Shouldly;

namespace Ananke.StateMachine.Tests;

/// <summary>
/// One machine instance serves many contexts, and several instances (nodes) share one store:
/// status and state are read from the stored context, never from the instance.
/// </summary>
[TestFixture]
public class ContextIsolationTests
{
    private InMemoryDistributedLock _store = new();

    [SetUp]
    public void SetUp() => _store = new InMemoryDistributedLock();

    [TearDown]
    public ValueTask TearDown() => _store.DisposeAsync();

    [Test]
    public async Task FaultAsync_OneContext_OtherContextStillMoves()
    {
        var machine = new LightMachine(_store, _store);
        var device1 = new TestContext("device-1");
        var device2 = new TestContext("device-2");

        await machine.FaultAsync(device1, "sensor broken");
        var result = await machine.TransitionAsync(device2, LightAction.TurnOn);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.CurrentState.ShouldBe(Light.On);
    }

    [Test]
    public async Task FaultAsync_SecondInstanceOverSameStore_RefusesUntilReset()
    {
        var node1 = new LightMachine(_store, _store);
        var node2 = new LightMachine(_store, _store);
        var device1 = new TestContext("device-1");

        await node1.FaultAsync(device1, "sensor broken");

        var refused = await node2.TransitionAsync(device1, LightAction.TurnOn);
        refused.Success.ShouldBeFalse();
        refused.ErrorMessage!.ShouldContain("sensor broken");
        refused.CurrentState.ShouldBe(Light.Off);

        (await node2.ResetAsync(device1, "replaced")).Success.ShouldBeTrue();

        var moved = await node1.TransitionAsync(device1, LightAction.TurnOn);
        moved.Success.ShouldBeTrue(moved.ErrorMessage);
        moved.CurrentState.ShouldBe(Light.On);
    }

    [Test]
    public async Task TransitionAsync_ManyContextsInParallel_EachEndsInItsOwnState()
    {
        const int contexts = 2_000;
        var locker = new PerResourceLock();
        var machine = new LightMachine(locker, _store);
        var ids = Enumerable.Range(0, contexts).Select(i => new TestContext($"device-{i}")).ToArray();

        // Even ids: TurnOn, Blink, Stabilize, TurnOff → Off. Odd ids: TurnOn, Blink → Blinking.
        LightAction[][] rounds =
        [
            [LightAction.TurnOn, LightAction.TurnOn],
            [LightAction.Blink, LightAction.Blink],
            [LightAction.Stabilize, LightAction.Blink],
            [LightAction.TurnOff, LightAction.Blink],
        ];
        var failures = new ConcurrentBag<string>();
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = 16 };

        foreach (var round in rounds)
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, contexts), parallel, async (i, _) =>
            {
                var result = await machine.TransitionAsync(ids[i], round[i % 2]);
                if (!result.Success)
                    failures.Add($"{ids[i].Id}: {result.ErrorMessage}");
            });
        }

        failures.ShouldBeEmpty();
        for (var i = 0; i < contexts; i++)
        {
            var stored = await _store.GetValueAsync<LightMachine.PersistedContext<Light>>(machine.GetContextKey(ids[i].Id));
            stored.ShouldNotBeNull();
            stored.State.ShouldBe(i % 2 == 0 ? Light.Off : Light.Blinking, ids[i].Id);
        }
    }

    /// <summary>
    /// A lock per resource, as a Redis lock behaves: transitions for different ids run at once.
    /// </summary>
    private sealed class PerResourceLock : IDistributedLock
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public async Task<CoordinatedActionResult<R>> RunCoordinatedActionAsync<R>(
            string resourceId, Func<Task<R>> action, CancellationToken ct = default)
        {
            var gate = _locks.GetOrAdd(resourceId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                await Task.Yield();
                return CoordinatedActionResult<R>.Succeeded(await action());
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
