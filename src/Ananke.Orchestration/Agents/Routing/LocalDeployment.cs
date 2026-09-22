namespace Ananke.Orchestration.Agents.Routing;

/// <summary>
/// How a self-hosted model is actually being served — the runtime, the quantization, the context
/// window it was launched with, and where it answers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a model id is not enough.</b> For a hosted model the identifier implies everything:
/// <c>gpt-4.1-mini</c> names one artefact, served one way, with one context window. For a locally
/// served model the identifier names only the weights, and the behaviour that matters belongs to the
/// tuple of <i>weights, runtime, quantization and launch flags</i>. Two servers running "the same
/// model" can differ in context window by two orders of magnitude and in tool-call reliability by
/// more than the gap between model generations.
/// </para>
/// <para>
/// <b>The context window is the sharp edge.</b> A catalogue template's
/// <see cref="ModelProfile.MaxContextTokens"/> describes the weights — commonly 128 K for a small
/// Llama. What truncates your prompt is the runtime's own setting: Ollama's <c>num_ctx</c>,
/// <c>llama-server</c>'s <c>-c</c>, vLLM's <c>--max-model-len</c>, whose defaults are routinely 4 K
/// or 8 K. Left unrecorded, routing decides a 100 K prompt "fits", the runtime silently drops most
/// of it, and the model answers confidently on a conversation it never saw.
/// <see cref="EffectiveContextTokens"/> is how that number gets told to the router, and
/// <see cref="ModelProfile.ContextTokens"/> is what routing reads once it has been.
/// </para>
/// <para>
/// <b>This describes the deployment, never the code.</b> Nothing here changes which adapter is used
/// — a self-hosted OpenAI-compatible server is reached by the ordinary OpenAI adapter with a base
/// URL, and that is the whole point.
/// </para>
/// </remarks>
/// <param name="Runtime">
/// The serving runtime — <c>"ollama"</c>, <c>"llama.cpp"</c>, <c>"vllm"</c>,
/// <c>"foundry-local"</c>. Free-form on purpose: a fixed enum would refuse a runtime that shipped
/// after this type did.
/// </param>
/// <param name="Quantization">
/// The quantization of the loaded artefact — <c>"Q4_K_M"</c>, <c>"bf16"</c> — or
/// <see langword="null"/> when unknown. Recorded rather than acted on: nothing derives capability
/// from it, because the honest thing to do with "this is a 4-bit quant" is to let a human weigh it,
/// not to silently downgrade a tier.
/// </param>
/// <param name="EffectiveContextTokens">
/// The context window the server was actually launched with, or <see langword="null"/> when it is
/// not known. When set, this is the number routing filters on.
/// </param>
/// <param name="ServedBy">
/// The endpoint this model was configured to reach.
/// <b>Configured, not observed</b> — it records what the caller set up, and cannot confirm that a
/// given request was served from there. Anything that needs to attest where a call actually went
/// needs a mechanism that sits on the request path; this field is not it, and must not be read as
/// though it were.
/// </param>
public sealed record LocalDeployment(
    string Runtime,
    string? Quantization = null,
    int? EffectiveContextTokens = null,
    Uri? ServedBy = null);
