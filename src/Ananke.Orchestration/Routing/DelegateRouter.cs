namespace Ananke.Orchestration.Routing;

internal sealed class DelegateRouter<TState>(Func<TState, string> route) : IRouter<TState>
{
    public Task<string> RouteAsync(TState state, CancellationToken ct) =>
        Task.FromResult(route(state));
}

internal sealed class AsyncDelegateRouter<TState>(Func<TState, Task<string>> route) : IRouter<TState>
{
    public Task<string> RouteAsync(TState state, CancellationToken ct) =>
        route(state);
}
