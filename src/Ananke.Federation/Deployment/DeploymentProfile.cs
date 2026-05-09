using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Deployment;

/// <summary>
/// Describes how a tool should be bound when deploying to a specific platform.
/// Parsed from the <c>profiles:</c> section of an <c>.ananke.yml</c> manifest.
/// </summary>
/// <example>
/// <code>
/// profiles:
///   azure-ai:
///     tools:
///       search: { platform: bing_search }
///       code:   { platform: code_interpreter }
///   vertex-ai:
///     tools:
///       search: { platform: google_search }
///       code:   { platform: code_execution }
///   local:
///     tools:
///       search: { execute: local }
///       code:   { execute: local }
/// </code>
/// </example>
public sealed record ToolBinding
{
    /// <summary>
    /// The execution mode to use for this tool in the target profile.
    /// Maps to <see cref="ToolExecutionMode"/> values: <c>"local"</c>, <c>"callback"</c>,
    /// <c>"mcp"</c>, <c>"openapi"</c>, <c>"platform"</c>.
    /// </summary>
    public required string Execute { get; init; }

    /// <summary>
    /// Platform-native capability identifier (e.g. <c>"code_interpreter"</c>, <c>"bing_search"</c>).
    /// Only meaningful when <see cref="Execute"/> is <c>"platform"</c>.
    /// Passed through verbatim to the platform API — Ananke does not validate the value,
    /// the platform will reject unknown capabilities at deploy time.
    /// </summary>
    public string? Platform { get; init; }

    /// <summary>
    /// Endpoint URI override for <c>"callback"</c>, <c>"mcp"</c>, or <c>"openapi"</c> modes.
    /// </summary>
    public string? Endpoint { get; init; }
}

/// <summary>
/// A named deployment profile that rebinds tool execution modes for a specific
/// target environment (e.g. <c>local</c>, <c>azure-ai</c>, <c>vertex-ai</c>).
/// </summary>
/// <remarks>
/// <para>
/// Profiles decouple the workflow definition from platform-specific tool wiring.
/// The same workflow runs locally with in-process lambdas and deploys to Azure
/// with platform-native Bing grounding — no code changes, just a different profile.
/// </para>
/// <para>
/// When no profile is specified, tools are deployed using their original
/// <see cref="ToolDefinition.ExecutionMode"/> and <see cref="ToolDefinition.PlatformCapability"/>.
/// </para>
/// </remarks>
public sealed record DeploymentProfile
{
    /// <summary>Profile name (e.g. <c>"azure-ai"</c>, <c>"local"</c>, <c>"staging"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Tool bindings keyed by tool name. Only tools listed here are rebound;
    /// unlisted tools keep their original execution mode.
    /// </summary>
    public IReadOnlyDictionary<string, ToolBinding> Tools { get; init; } =
        new Dictionary<string, ToolBinding>();

    /// <summary>
    /// Applies this profile's tool bindings to a toolkit, producing a new toolkit
    /// with rebound execution modes and platform capabilities.
    /// </summary>
    /// <param name="source">The original toolkit to rebind.</param>
    /// <returns>A new <see cref="ToolKit"/> with tools rebound per this profile.</returns>
    public ToolKit Bind(ToolKit source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var bound = new ToolKit(source.Name);

        foreach (var (name, tool) in source.Tools)
        {
            if (Tools.TryGetValue(name, out var binding))
            {
                bound.AddTool(ApplyBinding(tool, binding));
            }
            else
            {
                bound.AddTool(tool);
            }
        }

        return bound;
    }

    private static ToolDefinition ApplyBinding(ToolDefinition tool, ToolBinding binding)
    {
        var mode = ParseExecutionMode(binding.Execute);

        var endpoint = binding.Endpoint is not null
            ? new ToolEndpoint { Uri = new Uri(binding.Endpoint) }
            : mode == ToolExecutionMode.Local || mode == ToolExecutionMode.PlatformNative
                ? null
                : tool.Endpoint; // preserve original endpoint for callback/mcp/openapi

        return tool with
        {
            ExecutionMode = mode,
            PlatformCapability = binding.Platform ?? (mode == ToolExecutionMode.PlatformNative ? tool.PlatformCapability : null),
            Endpoint = endpoint
        };
    }

    private static ToolExecutionMode ParseExecutionMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "local" => ToolExecutionMode.Local,
            "callback" => ToolExecutionMode.Callback,
            "mcp" => ToolExecutionMode.Mcp,
            "openapi" => ToolExecutionMode.OpenApi,
            "platform" => ToolExecutionMode.PlatformNative,
            _ => throw new InvalidOperationException(
                $"Unknown execution mode '{value}' in deployment profile. " +
                "Supported: local, callback, mcp, openapi, platform.")
        };
}
