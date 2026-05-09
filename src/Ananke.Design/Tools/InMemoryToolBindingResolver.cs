using Ananke.Orchestration.Tools;

namespace Ananke.Design.Tools;

/// <summary>
/// Default in-memory implementation of <see cref="IToolBindingResolver"/>.
/// Use <see cref="Register"/> to map manifest binding references to concrete tools.
/// </summary>
public sealed class InMemoryToolBindingResolver : IToolBindingResolver
{
    private readonly Dictionary<string, ToolDefinition> _bindings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a concrete tool against a manifest binding reference.
    /// </summary>
    public InMemoryToolBindingResolver Register(string reference, ToolDefinition tool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(tool);
        _bindings[reference] = tool;
        return this;
    }

    /// <inheritdoc />
    public Task<ToolDefinition?> ResolveAsync(ToolManifestEntry tool, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (tool.Binding.Reference is null)
            return Task.FromResult<ToolDefinition?>(null);

        _bindings.TryGetValue(tool.Binding.Reference, out var resolved);
        return Task.FromResult(resolved);
    }
}
