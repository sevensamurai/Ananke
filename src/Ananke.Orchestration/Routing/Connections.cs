namespace Ananke.Orchestration.Routing;

public abstract record Connection
{
    public required string From { get; init; }
}

public sealed record DirectConnection : Connection
{
    public required string To { get; init; }
}

public sealed record RouterConnection<TState> : Connection
{
    public required IRouter<TState> Router { get; init; }
}

public sealed record ForkConnection : Connection
{
    public required IReadOnlyList<string> Targets { get; init; }
    public ForkMode Mode { get; init; } = ForkMode.FailFast;
}

/// <summary>
/// A connection that cycles execution from <see cref="Connection.From"/> back to
/// <see cref="LoopTarget"/> until <see cref="Until"/> returns <c>true</c>
/// or <see cref="MaxIterations"/> is reached.
/// </summary>
public sealed record LoopConnection<TState> : Connection
{
    /// <summary>Job to loop back to when the condition is not met.</summary>
    public required string LoopTarget { get; init; }

    /// <summary>Job to continue to when the loop exits.</summary>
    public required string ExitTarget { get; init; }

    /// <summary>Termination predicate evaluated after <see cref="Connection.From"/> completes.</summary>
    public required Func<TState, bool> Until { get; init; }

    /// <summary>Maximum iterations before forced exit. Prevents infinite loops.</summary>
    public required int MaxIterations { get; init; }
}
