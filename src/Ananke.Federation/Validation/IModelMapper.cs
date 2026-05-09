using Ananke.Design;

namespace Ananke.Federation.Validation;

/// <summary>
/// Maps model references across providers. Used during deployment to translate
/// a manifest's model definitions to platform-specific model identifiers.
/// </summary>
public interface IModelMapper
{
    /// <summary>Platform identifier this mapper targets.</summary>
    string Platform { get; }

    /// <summary>
    /// Maps a <see cref="ModelDefinition"/> to the platform-specific model identifier.
    /// Returns <see langword="null"/> if no mapping exists.
    /// </summary>
    string? Map(ModelDefinition model);
}
