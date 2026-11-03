using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Conformance;

/// <summary>
/// Pass-through reference <see cref="IJsonSchemaTranslator"/> — returns the input schema unchanged.
/// </summary>
/// <remarks>
/// This exists so the conformance suite can be self-validated: running a known-good subject through
/// every scenario is what distinguishes a broken fixture from a broken adapter.
/// </remarks>
public sealed class PassThroughJsonSchemaTranslator : IJsonSchemaTranslator
{
    /// <inheritdoc />
    public object Translate(IReadOnlyDictionary<string, object> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return schema;
    }
}
