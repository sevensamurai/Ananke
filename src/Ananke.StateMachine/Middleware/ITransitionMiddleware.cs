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
    /// Call next() to continue the pipeline.
    /// </summary>
    /// <param name="context">The state machine context</param>
    /// <param name="transition">The transition being executed</param>
    /// <param name="currentState">The current state before transition</param>
    /// <param name="next">Delegate to invoke the next middleware or the actual transition</param>
    /// <returns>The transition result</returns>
    Task<TransitionResult<S>> InvokeAsync(
        C context,
        T transition,
        S currentState,
        Func<Task<TransitionResult<S>>> next);
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
    public virtual async Task<TransitionResult<S>> InvokeAsync(
        C context,
        T transition,
        S currentState,
        Func<Task<TransitionResult<S>>> next)
    {
        return await next();
    }
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
        Func<Task<TransitionResult<S>>> next)
    {
        _logAction?.Invoke($"[{context.Id}] Attempting transition: {currentState} --({transition})--> ?");
        
        var result = await next();
        
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

    public MetricsMiddleware(Action<T, TimeSpan, bool>? recordMetric = null)
    {
        _recordMetric = recordMetric;
    }

    public async Task<TransitionResult<S>> InvokeAsync(
        C context,
        T transition,
        S currentState,
        Func<Task<TransitionResult<S>>> next)
    {
        var startTime = DateTime.UtcNow;
        var result = await next();
        var elapsed = DateTime.UtcNow - startTime;
        
        _recordMetric?.Invoke(transition, elapsed, result.Success);
        
        return result;
    }
}
