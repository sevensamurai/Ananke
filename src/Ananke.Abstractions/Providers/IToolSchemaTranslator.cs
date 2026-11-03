namespace Ananke.Abstractions.Providers;

/// <summary>
/// Translates <see cref="ProviderTool"/> instances into the JSON/object
/// representation expected by a specific provider's API.
/// </summary>
/// <remarks>
/// Implementations live in <c>Ananke.Orchestration.{Provider}</c> and are consumed by the
/// matching <c>{Provider}AgentModel</c>. <c>AddOrchestration{Provider}()</c> registers the
/// implementation through DI.
/// </remarks>
public interface IToolSchemaTranslator
{
    /// <summary>
    /// Converts a collection of provider tools into provider-specific schema objects.
    /// The return type is <see langword="object"/> to remain SDK-agnostic at the interface
    /// boundary; implementations document the concrete type in their XML docs.
    /// </summary>
    /// <param name="tools">Tool definitions to translate.</param>
    /// <returns>
    /// A provider-specific representation of the tool list (e.g. a
    /// <c>JsonArray</c>, a list of SDK types, etc.).
    /// </returns>
    object Translate(IEnumerable<ProviderTool> tools);
}
