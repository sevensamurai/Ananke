namespace Ananke.Abstractions.Providers;

/// <summary>
/// Maps an Ananke logical model identifier to the provider-native model id
/// and associated capability flags.
/// </summary>
/// <remarks>
/// Logical model ids follow the pattern <c>"{provider}/{model}"</c>
/// (e.g. <c>"openai/gpt-4.1"</c>, <c>"google/gemini-2.5-pro"</c>).
/// Provider implementations return the string the SDK expects in API requests
/// and expose capability flags so callers can skip unsupported features.
/// </remarks>
public interface IModelMapper
{
    /// <summary>Platform identifier this mapper targets (e.g. <c>"openai"</c>).</summary>
    string Platform { get; }

    /// <summary>
    /// Maps a logical model id to the provider-native model id.
    /// </summary>
    /// <param name="logicalModelId">
    /// Logical model id in <c>"{provider}/{model}"</c> form, or just the bare model
    /// name when the caller already knows the provider.
    /// </param>
    /// <returns>
    /// The provider-native model id string, or <see langword="null"/> if the id is
    /// not recognised by this mapper.
    /// </returns>
    string? MapModelId(string logicalModelId);

    /// <summary>
    /// Returns the capability flags supported by the specified model.
    /// </summary>
    /// <param name="nativeModelId">Provider-native model id returned by <see cref="MapModelId"/>.</param>
    /// <returns>Capability flags, or <see langword="null"/> when unknown.</returns>
    ModelCapabilityFlags? GetCapabilities(string nativeModelId);
}

/// <summary>
/// Capability flags that a provider model may support.
/// </summary>
[Flags]
public enum ModelCapabilityFlags
{
    /// <summary>No special capabilities beyond plain text generation.</summary>
    None = 0,

    /// <summary>The model supports tool/function calling.</summary>
    ToolCalling = 1 << 0,

    /// <summary>The model supports constrained structured JSON output.</summary>
    StructuredOutput = 1 << 1,

    /// <summary>The model supports image inputs.</summary>
    Vision = 1 << 2,

    /// <summary>The model supports audio inputs.</summary>
    AudioInput = 1 << 3,

    /// <summary>The model supports streaming responses.</summary>
    Streaming = 1 << 4,
}
