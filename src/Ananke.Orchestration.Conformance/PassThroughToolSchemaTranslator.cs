using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Conformance;

/// <summary>
/// Pass-through reference <see cref="IToolSchemaTranslator"/> — accepts everything and returns a
/// list of tool name/description pairs.
/// </summary>
/// <remarks>
/// This exists so the conformance suite can be self-validated: running a known-good subject through
/// every scenario is what distinguishes a broken fixture from a broken adapter.
/// </remarks>
public sealed class PassThroughToolSchemaTranslator : IToolSchemaTranslator
{
    /// <inheritdoc />
    public object Translate(IEnumerable<ProviderTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return tools.Select(t => new { t.Name, t.Description }).ToList();
    }
}
