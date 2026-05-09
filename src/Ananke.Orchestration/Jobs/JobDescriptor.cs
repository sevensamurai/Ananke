namespace Ananke.Orchestration.Jobs;

public record JobDescriptor<TState>
{
    public required string Name { get; init; }
    public required IJob<TState> Job { get; init; }

    /// <summary>Lifecycle hook invoked before the job executes. Token-less overload.</summary>
    public Func<TState, Task>? OnEnter { get; init; }

    /// <summary>Lifecycle hook invoked before the job executes, with cancellation support.</summary>
    public Func<TState, CancellationToken, Task>? OnEnterAsync { get; init; }

    /// <summary>Lifecycle hook invoked after the job succeeds. Token-less overload.</summary>
    public Func<TState, Task>? OnExit { get; init; }

    /// <summary>Lifecycle hook invoked after the job succeeds, with cancellation support.</summary>
    public Func<TState, CancellationToken, Task>? OnExitAsync { get; init; }

    /// <summary>Lifecycle hook invoked when the job faults. Token-less overload.</summary>
    public Func<TState, Exception, Task>? OnFault { get; init; }

    /// <summary>Lifecycle hook invoked when the job faults, with cancellation support.</summary>
    public Func<TState, Exception, CancellationToken, Task>? OnFaultAsync { get; init; }

    public TimeSpan? Timeout { get; init; }
    public InterruptMode? Interrupt { get; init; }
}
