namespace Ananke.Orchestration.Jobs;

internal sealed class DelegateJob<TState>(string name, Func<TState, CancellationToken, Task<TState>> execute) : IJob<TState>
{
    public string Name => name;

    public Task<TState> ExecuteAsync(TState state, CancellationToken ct = default) =>
        execute(state, ct);
}
