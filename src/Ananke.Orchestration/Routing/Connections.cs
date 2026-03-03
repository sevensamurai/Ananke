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
