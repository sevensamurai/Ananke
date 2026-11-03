namespace Ananke.Orchestration;

/// <summary>
/// Diagnostic ids and messages for the <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/>
/// markings in this assembly.
/// </summary>
internal static class ExperimentalApis
{
    /// <summary>The document knowledge base's additions, and the spending limit on model calls they rely on.</summary>
    public const string KnowledgeBase = "ANNKEEXP004";

    /// <summary>What a consumer of them is told when the diagnostic fires.</summary>
    public const string KnowledgeBaseMessage =
        "The document knowledge base's additions may change in any release before 1.0. Suppress ANNKEEXP004 to use them.";

    /// <summary>The plan tier: <c>Ananke.Orchestration.Planning</c> and its entry points.</summary>
    public const string PlanTier = "ANNKEEXP001";

    /// <summary>What a consumer of the plan tier is told when the diagnostic fires.</summary>
    public const string PlanTierMessage =
        "The plan tier is experimental: its public surface may change in any release. Suppress ANNKEEXP001 to use it.";

    /// <summary>
    /// <c>OpenAIChatAgentModel.Create</c> over the OpenAI SDK's credential constructors.
    /// </summary>
    public const string OpenAICredentials = "ANNKEEXP002";

    /// <summary>What a consumer of those overloads is told when the diagnostic fires.</summary>
    public const string OpenAICredentialsMessage =
        "Relies on OpenAI SDK constructors marked OPENAI001, which may change with the SDK. Suppress ANNKEEXP002 to use it.";

    /// <summary><c>OpenAIChatAgentModel.WithRequestFields</c>, over the SDK's <c>JsonPatch</c>.</summary>
    public const string OpenAIRequestFields = "ANNKEEXP003";

    /// <summary>What a consumer of that method is told when the diagnostic fires.</summary>
    public const string OpenAIRequestFieldsMessage =
        "Relies on System.ClientModel's JsonPatch, marked SCME0001, which may change with the SDK. Suppress ANNKEEXP003 to use it.";

    /// <summary>
    /// <c>AgentJob.Builder.WithRequestFields</c>: fields of an endpoint's own on every request a job
    /// makes. The same id as <see cref="OpenAIRequestFields"/>, which marks the adapter's side of it.
    /// </summary>
    public const string RequestFields = "ANNKEEXP003";

    /// <summary>What a consumer of that method is told when the diagnostic fires.</summary>
    public const string RequestFieldsMessage =
        "Fields of an endpoint's own are written through System.ClientModel's JsonPatch, marked SCME0001, which may change with the SDK. Suppress ANNKEEXP003 to use them.";

    /// <summary>Where each id is documented.</summary>
    public const string UrlFormat =
        "https://github.com/sevensamurai/Ananke/blob/main/docs/reference/experimental-apis.md";
}
