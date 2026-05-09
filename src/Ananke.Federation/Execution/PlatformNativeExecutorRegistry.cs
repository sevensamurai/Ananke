using System.Collections.Concurrent;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Execution;

/// <summary>
/// Registry of <see cref="IPlatformNativeExecutor"/> instances, keyed by capability identifier.
/// Used by the workflow runtime to resolve local executors for
/// <see cref="ToolExecutionMode.PlatformNative"/> tools.
/// </summary>
/// <remarks>
/// <para>
/// Executors are looked up in two stages:
/// <list type="number">
///   <item>Exact match on <c>(platform, capability)</c> — used when a cell is running
///     under a specific emulated platform (e.g. <c>local-emulated:azure-ai</c>).</item>
///   <item>Capability-only fallback — used for platform-agnostic emulators such as
///     <c>code_execution</c> or <c>bash</c>.</item>
/// </list>
/// </para>
/// <para>
/// This registry is intentionally not a DI singleton — callers can instantiate
/// one per test or per host configuration. The default DI registration
/// (<c>services.AddPlatformNativeExecutorRegistry()</c>) wires a shared instance.
/// </para>
/// </remarks>
public sealed class PlatformNativeExecutorRegistry
{
    // key: "capability" for generic, "platform::capability" for platform-scoped
    private readonly ConcurrentDictionary<string, IPlatformNativeExecutor> _executors = new(
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers an executor for its declared capability (platform-agnostic).</summary>
    /// <param name="executor">The executor to register.</param>
    /// <returns>This registry for fluent chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when an executor for the same capability is already registered.
    /// </exception>
    public PlatformNativeExecutorRegistry Register(IPlatformNativeExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        if (!_executors.TryAdd(executor.Capability, executor))
            throw new ArgumentException(
                $"An executor for capability '{executor.Capability}' is already registered.");

        return this;
    }

    /// <summary>
    /// Registers an executor scoped to a specific platform. Takes priority over the
    /// platform-agnostic registration when resolving for that platform.
    /// </summary>
    /// <param name="platform">Platform identifier (e.g. <c>"azure-ai"</c>).</param>
    /// <param name="executor">The executor to register.</param>
    /// <returns>This registry for fluent chaining.</returns>
    public PlatformNativeExecutorRegistry RegisterForPlatform(string platform, IPlatformNativeExecutor executor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentNullException.ThrowIfNull(executor);

        var key = MakePlatformKey(platform, executor.Capability);
        if (!_executors.TryAdd(key, executor))
            throw new ArgumentException(
                $"An executor for capability '{executor.Capability}' on platform '{platform}' is already registered.");

        return this;
    }

    /// <summary>
    /// Tries to resolve an executor for the given capability, optionally scoped to a platform.
    /// Returns <see langword="null"/> when no executor is registered.
    /// </summary>
    /// <param name="capability">Capability identifier (e.g. <c>"web_search"</c>).</param>
    /// <param name="platform">
    /// Optional platform identifier. When provided, platform-scoped executors are preferred.
    /// </param>
    public IPlatformNativeExecutor? TryResolve(string capability, string? platform = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        // Platform-scoped lookup takes priority.
        if (platform is not null &&
            _executors.TryGetValue(MakePlatformKey(platform, capability), out var scoped))
            return scoped;

        _executors.TryGetValue(capability, out var generic);
        return generic;
    }

    /// <summary>
    /// Returns all registered capability identifiers (including platform-scoped keys).
    /// </summary>
    public IReadOnlyList<string> RegisteredKeys => [.. _executors.Keys];

    /// <summary>
    /// Patches every <see cref="ToolExecutionMode.PlatformNative"/> tool in
    /// <paramref name="toolKit"/> whose <see cref="ToolDefinition.PlatformCapability"/>
    /// resolves to a registered executor, replacing the tool's stub execute delegate
    /// with the local emulator.
    /// </summary>
    /// <param name="toolKit">The kit to patch in-place.</param>
    /// <param name="platform">
    /// Optional emulated platform identifier. When provided, platform-scoped executors
    /// take priority over generic ones.
    /// </param>
    /// <returns>
    /// The number of tools that were successfully patched.
    /// </returns>
    public int ApplyTo(ToolKit toolKit, string? platform = null)
    {
        ArgumentNullException.ThrowIfNull(toolKit);

        var patched = 0;

        foreach (var (name, tool) in toolKit.Tools)
        {
            if (tool.ExecutionMode != ToolExecutionMode.PlatformNative
                || tool.PlatformCapability is null)
                continue;

            var executor = TryResolve(tool.PlatformCapability, platform);
            if (executor is null)
                continue;

            toolKit.ReplaceExecutor(name, executor.ExecuteAsync);
            patched++;
        }

        return patched;
    }

    private static string MakePlatformKey(string platform, string capability) =>
        $"{platform}::{capability}";
}
