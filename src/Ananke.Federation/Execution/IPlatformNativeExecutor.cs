using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Execution;

/// <summary>
/// Provides an in-process (local) implementation of a platform-native capability,
/// enabling workflows that declare <see cref="ToolExecutionMode.PlatformNative"/>
/// to run locally without deploying to a remote platform.
/// </summary>
/// <remarks>
/// Each executor handles a single <see cref="Capability"/> identifier (e.g.
/// <c>"web_search"</c>, <c>"code_execution"</c>). Register executors via
/// <see cref="PlatformNativeExecutorRegistry"/>. The workflow runtime resolves
/// the appropriate executor at tool-call time.
/// </remarks>
public interface IPlatformNativeExecutor
{
    /// <summary>
    /// The capability identifier this executor handles (e.g. <c>"code_execution"</c>,
    /// <c>"web_search"</c>, <c>"bash"</c>).
    /// </summary>
    string Capability { get; }

    /// <summary>
    /// Whether this executor is a stub (non-real, deterministic) implementation.
    /// Stub executors cause the validator to emit <c>FED062</c> warnings.
    /// </summary>
    bool IsStub { get; }

    /// <summary>
    /// Executes the capability locally and returns a <see cref="ToolResult"/>.
    /// </summary>
    /// <param name="args">Tool arguments as parsed from the model's tool-call JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default);
}
