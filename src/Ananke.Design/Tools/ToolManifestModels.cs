using Ananke.Orchestration.Tools;

namespace Ananke.Design.Tools;

/// <summary>
/// Portable binding metadata for a manifest-declared tool.
/// The binding identifies how the runtime should resolve an executable tool later,
/// without serializing delegates or secrets.
/// </summary>
public sealed record ToolManifestBinding
{
    /// <summary>
    /// Binding kind understood by a runtime resolver (for example <c>code</c>,
    /// <c>mcp</c>, <c>skill</c>, <c>http</c>, or <c>builtin</c>).
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Opaque resolver reference interpreted by the runtime at load time.
    /// </summary>
    public string? Reference { get; init; }
}

/// <summary>
/// Portable metadata for a manifest-declared tool.
/// </summary>
public sealed record ToolManifestEntry
{
    /// <summary>
    /// Stable manifest key used by jobs to reference this tool.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Runtime/model-visible tool name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Human-readable tool description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Discovery tags used for semantic routing, categorisation, and export/import fidelity.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Portable binding metadata for later runtime resolution.
    /// </summary>
    public ToolManifestBinding Binding { get; init; } = new();
}

/// <summary>
/// Resolves a manifest-declared tool to a runtime <see cref="ToolDefinition"/>.
/// </summary>
public interface IToolBindingResolver
{
    /// <summary>
    /// Resolves the executable tool definition for the given manifest entry.
    /// Returns <see langword="null"/> when no binding is available.
    /// </summary>
    Task<ToolDefinition?> ResolveAsync(ToolManifestEntry tool, CancellationToken ct = default);
}
