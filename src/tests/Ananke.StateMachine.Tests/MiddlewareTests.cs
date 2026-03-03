using Ananke.Abstractions;
using Ananke.Abstractions.Distributed;
using Ananke.StateMachine.Middleware;
using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class MiddlewareTests
{
    private InMemoryDistributedLock _lock = new();

    [TearDown]
    public ValueTask TearDown() => _lock.DisposeAsync();

    [SetUp]
    public void SetUp() => _lock = new InMemoryDistributedLock();

    // ── LoggingMiddleware ────────────────────────────────────────────

    [Test]
    public async Task LoggingMiddleware_LogsTransitionAttemptAndResult()
    {
        var logs = new List<string>();
        var machine = new LightMachine(_lock);
        machine.UseMiddleware(new LoggingMiddleware<TestContext, Light, LightAction>(msg => logs.Add(msg)));
        var ctx = new TestContext(1);

        await machine.TransitionAsync(ctx, LightAction.TurnOn);

        logs.Count.ShouldBeGreaterThanOrEqualTo(2);
        logs[0].ShouldContain("Attempting");
        logs[1].ShouldContain("succeeded");
    }

    [Test]
    public async Task LoggingMiddleware_LogsFailedTransition()
    {
        var logs = new List<string>();
        var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };
        var machine = new LightMachine(_lock, options);
        machine.UseMiddleware(new LoggingMiddleware<TestContext, Light, LightAction>(msg => logs.Add(msg)));
        var ctx = new TestContext(1);

        await machine.TransitionAsync(ctx, LightAction.TurnOff); // invalid from Off

        logs.ShouldContain(l => l.Contains("failed"));
    }

    // ── MetricsMiddleware ────────────────────────────────────────────

    [Test]
    public async Task MetricsMiddleware_RecordsMetric()
    {
        LightAction? recorded = null;
        TimeSpan? elapsed = null;
        bool? wasSuccess = null;

        var machine = new LightMachine(_lock);
        machine.UseMiddleware(new MetricsMiddleware<TestContext, Light, LightAction>((t, e, s) =>
        {
            recorded = t;
            elapsed = e;
            wasSuccess = s;
        }));
        var ctx = new TestContext(1);

        await machine.TransitionAsync(ctx, LightAction.TurnOn);

        recorded.ShouldBe(LightAction.TurnOn);
        elapsed.ShouldNotBeNull();
        wasSuccess.ShouldBe(true);
    }

    // ── Custom middleware ─────────────────────────────────────────────

    [Test]
    public async Task CustomMiddleware_CanShortCircuit()
    {
        var machine = new LightMachine(_lock);
        machine.UseMiddleware(new BlockingMiddleware());
        var ctx = new TestContext(1);

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("Blocked");
    }

    [Test]
    public async Task MultipleMiddlewares_ExecuteInRegistrationOrder()
    {
        var order = new List<int>();
        var machine = new LightMachine(_lock);
        machine.UseMiddleware(new OrderedMiddleware(1, order));
        machine.UseMiddleware(new OrderedMiddleware(2, order));
        var ctx = new TestContext(1);

        await machine.TransitionAsync(ctx, LightAction.TurnOn);

        order.ShouldBe([1, 2]);
    }

    [Test]
    public async Task UseMiddleware_GenericOverload_WorksLikeDirect()
    {
        var machine = new LightMachine(_lock);
        machine.UseMiddleware<PassThroughMiddleware>();
        var ctx = new TestContext(1);

        var result = await machine.TransitionAsync(ctx, LightAction.TurnOn);

        result.Success.ShouldBeTrue();
    }

    // ── Test helpers ─────────────────────────────────────────────────

    private sealed class BlockingMiddleware : ITransitionMiddleware<TestContext, Light, LightAction>
    {
        public Task<TransitionResult<Light>> InvokeAsync(
            TestContext context, LightAction transition, Light currentState,
            Func<Task<TransitionResult<Light>>> next) =>
            Task.FromResult(TransitionResult<Light>.Failed(currentState, "Blocked by middleware"));
    }

    private sealed class OrderedMiddleware(int id, List<int> order)
        : ITransitionMiddleware<TestContext, Light, LightAction>
    {
        public async Task<TransitionResult<Light>> InvokeAsync(
            TestContext context, LightAction transition, Light currentState,
            Func<Task<TransitionResult<Light>>> next)
        {
            order.Add(id);
            return await next();
        }
    }

    private sealed class PassThroughMiddleware : TransitionMiddlewareBase<TestContext, Light, LightAction>;
}
