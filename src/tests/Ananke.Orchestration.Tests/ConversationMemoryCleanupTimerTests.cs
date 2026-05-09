using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Orchestration.Extensions;
using Ananke.Orchestration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Tests for H-8 / H-9: the conversation memory cleanup background service must start
/// automatically as an <see cref="IHostedService"/>, never allow overlapping cleanup
/// passes, and stop cleanly when the host shuts down.
/// </summary>
[TestFixture]
public class ConversationMemoryCleanupTimerTests
{
    // ── IHostedService registration ──────────────────────────────────

    [Test]
    public void UseMemoryCleanup_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConversationMemory>(new InMemoryConversationMemory());
        services.AddWorkflowOrchestration(o =>
            o.UseMemoryCleanup(TimeSpan.FromMinutes(5)));

        var sp = services.BuildServiceProvider();

        // Must be resolvable as IHostedService — the contract AddHostedService relies on.
        var hostedServices = sp.GetServices<IHostedService>().ToList();
        hostedServices.ShouldNotBeEmpty();
    }

    // ── Automatic startup ────────────────────────────────────────────

    [Test]
    public async Task CleanupRuns_AfterHostStart_WithoutManualInvocation()
    {
        // One session that has already expired.
        var clock = new FakeTimeProvider();
        var memory = new InMemoryConversationMemory(ttl: TimeSpan.FromMilliseconds(1), timeProvider: clock);
        await memory.AddAsync("s1", AgentMessage.User("hello"));
        clock.Advance(TimeSpan.FromMilliseconds(10)); // virtual TTL elapsed

        memory.SessionCount.ShouldBe(1);

        var cleanupRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wrappedMemory = new CallbackCleanupMemory(async ct =>
        {
            await memory.CleanupExpiredAsync(ct);
            cleanupRan.TrySetResult();
        });

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IConversationMemory>(wrappedMemory);
        services.AddWorkflowOrchestration(o =>
            o.UseMemoryCleanup(TimeSpan.FromMilliseconds(40)));

        await using var host = new ServiceProviderHost(services.BuildServiceProvider());
        await host.StartAsync();
        await host.TimerReady.WaitAsync(TimeSpan.FromSeconds(5));

        // Advance past the tick interval and wait for cleanup to run.
        clock.Advance(TimeSpan.FromMilliseconds(40));
        await cleanupRan.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await host.StopAsync();

        // Cleanup ran automatically — expired session removed.
        memory.SessionCount.ShouldBe(0);
    }

    // ── No-overlap guarantee ─────────────────────────────────────────

    [Test]
    public async Task Ticks_DoNotOverlap_SlowCleanup()
    {
        var clock = new FakeTimeProvider();
        var concurrentCleanups = 0;
        var maxConcurrent = 0;
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Gate that the test controls: each cleanup waits until released.
        var cleanupGates = new System.Collections.Concurrent.ConcurrentQueue<TaskCompletionSource>();

        var memory = new CallbackCleanupMemory(onCleanup: async ct =>
        {
            var current = Interlocked.Increment(ref concurrentCleanups);
            Interlocked.Exchange(ref maxConcurrent, Math.Max(Volatile.Read(ref maxConcurrent), current));
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cleanupGates.Enqueue(gate);
            cleanupStarted.TrySetResult();
            await gate.Task.WaitAsync(ct);
            Interlocked.Decrement(ref concurrentCleanups);
        });

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IConversationMemory>(memory);
        services.AddWorkflowOrchestration(o =>
            o.UseMemoryCleanup(TimeSpan.FromMilliseconds(30)));

        await using var host = new ServiceProviderHost(services.BuildServiceProvider());
        await host.StartAsync();
        await host.TimerReady.WaitAsync(TimeSpan.FromSeconds(5));

        // Fire first tick and wait until the callback is actually blocked inside the gate.
        clock.Advance(TimeSpan.FromMilliseconds(30));
        await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Fire additional ticks while first cleanup is still in-flight.
        clock.Advance(TimeSpan.FromMilliseconds(30));
        clock.Advance(TimeSpan.FromMilliseconds(30));

        // PeriodicTimer guarantees at most 1 concurrent cleanup pass.
        maxConcurrent.ShouldBe(1);

        // Drain all pending gates so the host can stop cleanly.
        while (cleanupGates.TryDequeue(out var gate))
            gate.TrySetResult();

        await host.StopAsync();
    }

    // ── Clean shutdown ────────────────────────────────────────────────

    [Test]
    public async Task StopAsync_StopsLoop_CleanlyWithoutException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConversationMemory>(new InMemoryConversationMemory());
        services.AddWorkflowOrchestration(o =>
            o.UseMemoryCleanup(TimeSpan.FromSeconds(60))); // very long — tick never fires

        await using var host = new ServiceProviderHost(services.BuildServiceProvider());
        await host.StartAsync();

        // Must complete quickly and not throw even though no tick has fired.
        await Should.NotThrowAsync(() =>
            host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public async Task StopAsync_WhileCleanupInProgress_AwaitsCompletion()
    {
        var clock = new FakeTimeProvider();
        var cleanupStarted = new TaskCompletionSource();
        var cleanupMayFinish = new TaskCompletionSource();

        var memory = new CallbackCleanupMemory(onCleanup: async _ =>
        {
            cleanupStarted.TrySetResult();
            await cleanupMayFinish.Task;
        });

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IConversationMemory>(memory);
        services.AddWorkflowOrchestration(o =>
            o.UseMemoryCleanup(TimeSpan.FromMilliseconds(30)));

        await using var host = new ServiceProviderHost(services.BuildServiceProvider());
        await host.StartAsync();
        await host.TimerReady.WaitAsync(TimeSpan.FromSeconds(5));

        // Trigger the first tick.
        clock.Advance(TimeSpan.FromMilliseconds(30));

        // Wait until the in-flight cleanup has started.
        await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Unblock cleanup, then stop — StopAsync must wait for the pass to finish.
        cleanupMayFinish.TrySetResult();

        await Should.NotThrowAsync(() =>
            host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    // ── Error resilience ─────────────────────────────────────────────

    [Test]
    public async Task CleanupException_IsSwallowed_LoopContinues()
    {
        var clock = new FakeTimeProvider();
        var totalCalls = 0;
        // Signalled once per cleanup invocation so the test can wait reliably.
        var callCompleted = new SemaphoreSlim(0);

        var memory = new CallbackCleanupMemory(onCleanup: _ =>
        {
            var n = Interlocked.Increment(ref totalCalls);
            callCompleted.Release();
            if (n <= 2)
                throw new InvalidOperationException("simulated failure");
            return Task.CompletedTask;
        });

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IConversationMemory>(memory);
        services.AddWorkflowOrchestration(o =>
            o.UseMemoryCleanup(TimeSpan.FromMilliseconds(40)));

        await using var host = new ServiceProviderHost(services.BuildServiceProvider());
        await host.StartAsync();
        await host.TimerReady.WaitAsync(TimeSpan.FromSeconds(5));

        // Advance the clock one tick at a time
        // has fully completed (and the loop has re-registered WaitForNextTickAsync)
        // before advancing again.
        for (var i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(40));
            await callCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await host.StopAsync();

        // Loop survived both failures and continued running.
        totalCalls.ShouldBeGreaterThanOrEqualTo(3);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Minimal host wrapper that starts/stops all <see cref="IHostedService"/>s
    /// registered in the provided <see cref="IServiceProvider"/>.
    /// </summary>
    private sealed class ServiceProviderHost(IServiceProvider sp) : IAsyncDisposable
    {
        private readonly List<IHostedService> _services =
            sp.GetServices<IHostedService>().ToList();

        /// <summary>
        /// Completes once the cleanup timer's <see cref="PeriodicTimer"/> has been
        /// created and is awaiting its first tick — safe to advance the clock after this.
        /// </summary>
        public Task TimerReady =>
            _services.OfType<ConversationMemoryCleanupTimer>().FirstOrDefault()?.TimerReady
            ?? Task.CompletedTask;

        public async Task StartAsync()
        {
            foreach (var svc in _services)
                await svc.StartAsync(CancellationToken.None);
        }

        public async Task StopAsync()
        {
            foreach (var svc in _services)
                await svc.StopAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var svc in _services)
            {
                try { await svc.StopAsync(CancellationToken.None); } catch { }
                if (svc is IAsyncDisposable ad) await ad.DisposeAsync();
                else if (svc is IDisposable d) d.Dispose();
            }
            if (sp is IDisposable disposable) disposable.Dispose();
        }
    }

    // ── Test doubles ─────────────────────────────────────────────────

    /// <summary>
    /// Memory that delegates <c>CleanupExpiredAsync</c> to a provided async callback.
    /// All other members are no-ops.
    /// </summary>
    private sealed class CallbackCleanupMemory(Func<CancellationToken, Task> onCleanup) : IConversationMemory
    {
        public Task CleanupExpiredAsync(CancellationToken ct = default) => onCleanup(ct);

        public Task AddAsync(string sessionId, IEnumerable<AgentMessage> messages, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task AddAsync(string sessionId, AgentMessage message, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<AgentMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentMessage>>([]);
        public Task ClearAsync(string sessionId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
