namespace Ananke.Orchestration.Translators;

/// <summary>
/// Translates an Ananke JSON schema dictionary into the schema dialect expected
/// by a specific provider.
/// </summary>
/// <remarks>
/// Most providers accept standard JSON Schema; implement this interface only when
/// the target provider requires a custom dialect (e.g. Google's Vertex AI GenAI
/// types use a proto-derived schema that differs from JSON Schema draft-07).
/// A pass-through default implementation is registered automatically by
/// <c>AddOrchestration{Provider}()</c> for providers that do not need translation.
/// </remarks>
public interface IJsonSchemaTranslator
{
    /// <summary>
    /// Translates a standard Ananke schema dictionary to the provider-specific
    /// schema representation.
    /// </summary>
    /// <param name="schema">
    /// A JSON Schema dictionary produced by <c>JsonSchemaGenerator</c>, with keys
    /// such as <c>"type"</c>, <c>"properties"</c>, <c>"required"</c>, etc.
    /// </param>
    /// <returns>
    /// An object or dictionary in the provider's expected schema format.
    /// For providers that accept standard JSON Schema, implementations may
    /// return the input dictionary unchanged.
    /// </returns>
    object Translate(IReadOnlyDictionary<string, object> schema);
}
