using Ananke.Design;
using Ananke.Federation.Execution;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Validation;

/// <summary>
/// An <see cref="IPlatformValidator"/> for the local execution target. Composes
/// <see cref="DeployabilityValidator"/> (offline structural checks) with
/// <see cref="PlatformNativeExecutorRegistry"/> availability checks.
/// </summary>
/// <remarks>
/// <para>
/// Diagnostic codes emitted by this validator:
/// </para>
/// <list type="bullet">
///   <item><c>FED061</c> — Error: a <see cref="ToolExecutionMode.PlatformNative"/> tool has no
///     executor registered for the local target.</item>
///   <item><c>FED062</c> — Warning: a <see cref="ToolExecutionMode.PlatformNative"/> tool is
///     covered by a stub executor only. Results are deterministic, not real platform behaviour.</item>
/// </list>
/// </remarks>
public sealed class LocalPlatformValidator(
    PlatformNativeExecutorRegistry? executorRegistry = null,
    IDeployabilityValidator? structural = null,
    string? emulatedPlatform = null) : IPlatformValidator
{
    private readonly IDeployabilityValidator _structural = structural ?? new DeployabilityValidator();
    private readonly PlatformNativeExecutorRegistry _executorRegistry =
        executorRegistry ?? new PlatformNativeExecutorRegistry();

    /// <inheritdoc />
    public string Platform => emulatedPlatform is null ? "local" : $"local-emulated:{emulatedPlatform}";

    /// <inheritdoc />
    public Task<DeployabilityReport> ValidateAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(toolKit);

        // Structural checks are only meaningful when an emulated platform is set;
        // the "local" pseudo-platform is not in platform-capabilities.json and has
        // no model/tool constraints. Skip when emulatedPlatform is null.
        var diagnostics = emulatedPlatform is not null
            ? new List<DeployDiagnostic>(_structural.Validate(manifest, toolKit, emulatedPlatform).Diagnostics)
            : new List<DeployDiagnostic>();

        // Executor availability checks for PlatformNative tools
        foreach (var (name, tool) in toolKit.Tools)
        {
            if (tool.ExecutionMode != ToolExecutionMode.PlatformNative
                || tool.PlatformCapability is null)
                continue;

            var executor = _executorRegistry.TryResolve(tool.PlatformCapability, emulatedPlatform);

            if (executor is null)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Error,
                    Code = "FED061",
                    Message = $"PlatformNative tool '{name}' declares capability '{tool.PlatformCapability}' " +
                              "but no IPlatformNativeExecutor is registered for the local target.",
                    Component = name,
                    Suggestion = $"Register an executor via PlatformNativeExecutorRegistry.Register(...) " +
                                 $"or use Ananke.Federation.LocalEmulators for capability '{tool.PlatformCapability}'."
                });
            }
            else if (executor.IsStub)
            {
                diagnostics.Add(new DeployDiagnostic
                {
                    Severity = DeployDiagnosticSeverity.Warning,
                    Code = "FED062",
                    Message = $"PlatformNative tool '{name}' (capability '{tool.PlatformCapability}') " +
                              "is covered by a stub executor. Results are deterministic, not real platform behaviour.",
                    Component = name,
                    Suggestion = "Replace the stub with a real emulator for integration-level testing."
                });
            }
        }

        return Task.FromResult(new DeployabilityReport { Diagnostics = diagnostics });
    }
}
