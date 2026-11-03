namespace Ananke.Embeddings.Onnx;

/// <summary>
/// Diagnostic ids and messages for the <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/>
/// markings in this assembly. An id is the same in every assembly that marks part of its surface.
/// </summary>
internal static class ExperimentalApis
{
    /// <summary>The document knowledge base's additions: stores, embedders, header reading, records.</summary>
    public const string KnowledgeBase = "ANNKEEXP004";

    /// <summary>What a consumer of them is told when the diagnostic fires.</summary>
    public const string KnowledgeBaseMessage =
        "The document knowledge base's additions may change in any release before 1.0. Suppress ANNKEEXP004 to use them.";

    /// <summary>Where each id is documented.</summary>
    public const string UrlFormat =
        "https://github.com/sevensamurai/Ananke/blob/main/docs/reference/experimental-apis.md";
}
