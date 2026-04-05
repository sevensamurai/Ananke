namespace Ananke.Orchestration.Jobs;

public record JobDescriptor<TState>
{
    public required string Name { get; init; }
    public required IJob<TState> Job { get; init; }
    public Func<TState, Task>? OnEnter { get; init; }
    public Func<TState, Task>? OnExit { get; init; }
    public Func<TState, Exception, Task>? OnFault { get; init; }
    public TimeSpan? Timeout { get; init; }
    public InterruptMode? Interrupt { get; init; }
}
