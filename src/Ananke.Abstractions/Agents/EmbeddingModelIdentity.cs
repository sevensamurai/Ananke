namespace Ananke.Abstractions.Agents;

/// <summary>
/// Which model makes an <see cref="IEmbeddingModel"/>'s vectors. Vectors from two different models aren't
/// comparable, so a store records the identity it was written with and can refuse another.
/// </summary>
/// <param name="Provider">Who serves the model: <c>openai</c>, <c>google</c>, an endpoint's host, or <c>onnx</c> for a local model.</param>
/// <param name="Model">The model's name as the provider knows it.</param>
/// <param name="Dimensions">The length of each vector, when known before the first call.</param>
/// <param name="Revision">The model's version or commit, when the provider names one.</param>
public sealed record EmbeddingModelIdentity(string Provider, string Model, int? Dimensions = null, string? Revision = null)
{
    /// <summary>The identity as <c>provider/model@revision (dimensions)</c>, leaving out what isn't known.</summary>
    public override string ToString() =>
        $"{Provider}/{Model}{(Revision is null ? "" : "@" + Revision)}{(Dimensions is null ? "" : $" ({Dimensions})")}";
}
