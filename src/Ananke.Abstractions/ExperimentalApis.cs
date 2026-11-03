namespace Ananke.Abstractions;

/// <summary>
/// Diagnostic ids and messages for the <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/>
/// markings in this assembly. An id is the same in every assembly that marks part of its surface.
/// </summary>
internal static class ExperimentalApis
{
    /// <summary><c>AgentRequest.RequestFields</c>: fields of an endpoint's own that one request carries.</summary>
    public const string RequestFields = "ANNKEEXP003";

    /// <summary>What a consumer of them is told when the diagnostic fires.</summary>
    public const string RequestFieldsMessage =
        "Fields of an endpoint's own are written through System.ClientModel's JsonPatch, marked SCME0001, which may change with the SDK. Suppress ANNKEEXP003 to use them.";

    /// <summary>Where each id is documented.</summary>
    public const string UrlFormat =
        "https://github.com/sevensamurai/Ananke/blob/main/docs/reference/experimental-apis.md";
}
