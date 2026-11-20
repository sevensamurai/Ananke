using Ananke.Abstractions;

namespace Ananke.StateMachine.Middleware;

/// <summary>
/// Middleware for intercepting state machine transitions.
/// Allows adding cross-cutting concerns like logging, metrics, validation.
/// </summary>
/// <typeparam name="C">Context type</typeparam>
/// <typeparam name="S">State enum type</typeparam>
/// <typeparam name="T">Transition enum type</typeparam>
public interface ITransitionMiddleware<C, S, T>
    where C : IBaseContext
    where S : Enum
    where T : Enum
{
    /// <summary>
    /// Invokes the middleware with the transition context.
    /// Call next(ct) to continue the pipeline. Runs inside the context's lock, after the context
    /// is read and found Operative: a middleware must not call <c>TransitionAsync</c>,
    /// <c>FaultAsync</c> or <c>ResetAsync</c> for the same context: each takes that lock, which is
    /// held, and fails once its retries run out. The lock is held until the middleware returns, so
    /// a middleware that waits passes <paramref name="ct"/> to what it awaits. The token given to
    /// <paramref name="next"/> is the one the transition uses; a token of the middleware's own that is
    /// cancelled while <paramref name="ct"/> is not fails the transition instead of throwing. Calling
    /// <paramref name="next"/> again evaluates the transition from the record as stored then.
    /// The attributed event timestamp is available on the returned
    /// <see cref="TransitionResult{S}.EventTimestamp"/> after the pipeline completes.
    /// </summary>
    /// <param name="context">The state machine context</param>
    /// <param name="transition">The transition being executed</param>
    /// <param name="currentState">The context's stored state before the transition</param>
    /// <param name="next">Invokes the next middleware or the actual transition with the token it is given</param>
    /// <param name="ct">The transition's token, from <c>TransitionAsync</c></param>
    /// <returns>The transition result</returns>
    Task<TransitionResult<S>> InvokeAsync(
        C context,
        T transition,
        S currentState,
        Func<CancellationToken, Task<TransitionResult<S>>> next,
        CancellationToken ct);
}

/// <summary>
/// Middleware for intercepting notifications.
/// </summary>
/// <typeparam name="C">Context type</typeparam>
/// <typeparam name="N">Notification enum type</typeparam>
public interface INotificationMiddleware<C, N>
    where C : IBaseContext
    where N : Enum
{
    /// <summary>
    /// Invokes the middleware with the notification context.
    /// </summary>
    Task InvokeAsync(C context, N notification, Func<Task> next);
}

/// <summary>
/// Base class for transition middleware with default pass-through behavior
/// </summary>
public abstract class TransitionMiddlewareBase<C, S, T> : ITransitionMiddleware<C, S, T>
    where C : IBaseContext
    where S : Enum
    where T : Enum
{
    public virtual Task<TransitionResult<S>> InvokeAsync(
        C context,
        T transition,
        S currentState,
        Func<CancellationToken, Task<TransitionResult<S>>> next,
        CancellationToken ct) =>
        next(ct);
}

/// <summary>
/// Logging middleware for state transitions
/// </summary>
public class LoggingMiddleware<C, S, T> : ITransitionMiddleware<C, S, T>
    where C : IBaseContext
    where S : Enum
    where T : Enum
{
    private readonly Action<string>? _logAction;

    public LoggingMiddleware(Action<string>? logAction = null)
    {
        _logAction = logAction;
    }

    public async Task<TransitionResult<S>> InvokeAsync(
        C context,
        T transition,
        S currentState,
        Func<CancellationToken, Task<TransitionResult<S>>> next,
        CancellationToken ct)
    {
        _logAction?.Invoke($"[{context.Id}] Attempting transition: {currentState} --({transition})--> ?");

        var result = await next(ct).ConfigureAwait(false);

        if (result.Success)
        {
            _logAction?.Invoke($"[{context.Id}] Transition succeeded: {result.PreviousState} --({transition})--> {result.CurrentState}");
        }
        else
        {
            _logAction?.Invoke($"[{context.Id}] Transition failed: {result.ErrorMessage}");
        }

        return result;
    }
}

/// <summary>
/// Metrics middleware for measuring transition timing
/// </summary>
public class MetricsMiddleware<C, S, T> : ITransitionMiddleware<C, S, T>
    where C : IBaseContext
    where S : Enum
    where T : Enum
{
    private readonly Action<T, TimeSpan, bool>? _recordMetric;
    private readonly TimeProvider _clock;

    public MetricsMiddleware(
        Action<T, TimeSpan, bool>? recordMetric = null,
        TimeProvider? clock = null)
    {
        _recordMetric = recordMetric;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<TransitionResult<S>> InvokeAsync(
        C context,
        T transition,
        S currentState,
        Func<CancellationToken, Task<TransitionResult<S>>> next,
        CancellationToken ct)
    {
        var startTime = _clock.GetUtcNow();
        var result = await next(ct).ConfigureAwait(false);
        var elapsed = _clock.GetUtcNow() - startTime;

        _recordMetric?.Invoke(transition, elapsed, result.Success);

        return result;
    }
}
