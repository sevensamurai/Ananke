namespace Ananke.Abstractions.Providers;

/// <summary>
/// Schema-only view of a tool, used as input to <see cref="IToolSchemaTranslator"/>.
/// Carries the fields a provider needs to construct its API tool representation —
/// name, description, parameters schema, execution mode, and optional platform capability —
/// without the execution delegate or runtime prerequisites that live in
/// <c>Ananke.Orchestration.Tools.ToolDefinition</c>.
/// </summary>
public record ProviderTool(string Name, string Description, string ParametersJsonSchema)
{
    /// <summary>
    /// How the tool's implementation is reached at runtime.
    /// Translators use this to reject or specially-handle local-only tools.
    /// Defaults to <see cref="ToolExecutionMode.Local"/>.
    /// </summary>
    public ToolExecutionMode ExecutionMode { get; init; } = ToolExecutionMode.Local;

    /// <summary>
    /// Platform-native capability identifier (e.g. <c>"code_execution"</c>,
    /// <c>"web_search"</c>). Only meaningful when <see cref="ExecutionMode"/> is
    /// <see cref="ToolExecutionMode.PlatformNative"/>.
    /// </summary>
    public string? PlatformCapability { get; init; }
}
