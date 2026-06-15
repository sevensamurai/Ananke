using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Tools;

/// <summary>
/// Pluggable dispatch strategy for remote-backed tools that have no local
/// <c>Execute</c> delegate (<see cref="ToolExecutionMode.Callback"/>,
/// <see cref="ToolExecutionMode.OpenApi"/>, etc.).
/// </summary>
/// <remarks>
/// <para>
/// Register an implementation in DI and pass it to
/// <see cref="ToolKit.WithExecutorStrategy"/> to intercept execution of any tool whose
/// <see cref="ToolDefinition.ExecutionMode"/> is not <see cref="ToolExecutionMode.Local"/>
/// and that has no local handler set. The framework calls <see cref="DispatchAsync"/>
/// instead of the built-in stub-error fallback.
/// </para>
/// <para>
/// The default implementation, <see cref="NullToolExecutorStrategy"/>, preserves the
/// existing behaviour: it returns a descriptive error, signalling to the LLM that the
/// tool must be invoked via its remote endpoint or platform.
/// </para>
/// <para>
/// Implementations are responsible for reading <see cref="ToolDefinition.Endpoint"/>
/// (including <see cref="ToolEndpoint.QueryParams"/>) and for reporting health changes
/// to <see cref="Ananke.Abstractions.Tools.IToolMemory"/> if one is available.
/// </para>
/// </remarks>
public interface IToolExecutorStrategy
{
    /// <summary>
    /// Dispatches a tool invocation to its remote endpoint.
    /// </summary>
    /// <param name="tool">The tool definition, including endpoint and execution-mode metadata.</param>
    /// <param name="args">Raw argument map passed by the LLM.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tool result to pass back to the model.</returns>
    Task<ToolResult> DispatchAsync(
        ToolDefinition tool,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct);
}

/// <summary>
/// Default <see cref="IToolExecutorStrategy"/> that returns a descriptive error for
/// remote-backed tools with no local handler — identical to the previous built-in stub.
/// Replace this with a real dispatcher (e.g. <c>HttpToolDispatcher</c>) in the host.
/// </summary>
public sealed class NullToolExecutorStrategy : IToolExecutorStrategy
{
    /// <summary>Shared instance; stateless and safe to reuse.</summary>
    public static readonly NullToolExecutorStrategy Instance = new();

    /// <inheritdoc/>
    public Task<ToolResult> DispatchAsync(
        ToolDefinition tool,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct) =>
        Task.FromResult(ToolResult.Error(
            $"Tool '{tool.Name}' has no local execute handler " +
            $"(execution mode: {tool.ExecutionMode}). " +
            "Run this tool via its remote endpoint or platform."));
}
