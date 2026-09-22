namespace Ananke.Orchestration.Agents.Routing;

/// <summary>
/// A coarse, developer-declared statement of what a model can do, for models Ananke has no
/// catalogue entry for — anything reached by a bring-your-own model id, such as an Amazon Bedrock
/// model or a Microsoft Foundry deployment name.
/// </summary>
/// <remarks>
/// <para>
/// The rungs mirror the capability bundles the built-in catalogue has always used internally, so a
/// declared tier is not a second vocabulary — it expands to the same
/// <see cref="ModelCapability"/> flags <see cref="CapabilityModelRouter"/> already routes on.
/// </para>
/// <para>
/// <b>A tier is a declaration by the developer, never an inference by Ananke.</b> The default is
/// <see cref="Undeclared"/>, and a model that declares nothing stays unroutable rather than being
/// assumed to do anything — see <see cref="ModelProfile.Undeclared"/>. The danger this guards
/// against is not a profile over-claiming; it is Ananke quietly assuming that tool calling or vision
/// will work. SeeD9.
/// </para>
/// </remarks>
public enum ModelTier
{
    /// <summary>
    /// Nothing has been declared. Capability routing cannot select the model; calling it directly
    /// still works. This is the default, deliberately.
    /// </summary>
    Undeclared = 0,

    /// <summary>Text generation only. Assume no tool calling and no structured output.</summary>
    TextBase,

    /// <summary>Text generation plus structured output and tool calling.</summary>
    ChatModel,

    /// <summary>A chat model that also handles code generation and a large context window.</summary>
    FullModel,

    /// <summary>A full model that also handles multi-step reasoning and image input.</summary>
    FrontierModel,
}

/// <summary>Maps a declared <see cref="ModelTier"/> onto the capability flags it implies.</summary>
public static class ModelTiers
{
    /// <summary>Text generation only.</summary>
    public const ModelCapability TextBase = ModelCapability.TextGeneration;

    /// <summary><see cref="TextBase"/> plus structured output and tool calling.</summary>
    public const ModelCapability ChatModel =
        TextBase | ModelCapability.StructuredOutput | ModelCapability.ToolCalling;

    /// <summary><see cref="ChatModel"/> plus code generation and a large context window.</summary>
    public const ModelCapability FullModel =
        ChatModel | ModelCapability.CodeGeneration | ModelCapability.LargeContext;

    /// <summary><see cref="FullModel"/> plus reasoning and vision.</summary>
    public const ModelCapability FrontierModel =
        FullModel | ModelCapability.Reasoning | ModelCapability.Vision;

    /// <summary>
    /// Returns the capabilities implied by <paramref name="tier"/>.
    /// <see cref="ModelTier.Undeclared"/> yields <see cref="ModelCapability.None"/> — an absence,
    /// not a claim that the model can do nothing.
    /// </summary>
    /// <param name="tier">The declared tier.</param>
    public static ModelCapability ToCapabilities(this ModelTier tier) => tier switch
    {
        ModelTier.TextBase => TextBase,
        ModelTier.ChatModel => ChatModel,
        ModelTier.FullModel => FullModel,
        ModelTier.FrontierModel => FrontierModel,
        _ => ModelCapability.None,
    };
}
