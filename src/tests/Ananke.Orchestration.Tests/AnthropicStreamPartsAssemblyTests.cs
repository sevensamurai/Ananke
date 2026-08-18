using System.Collections;
using System.Reflection;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Anthropic;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// ADR-arch-029 D1/D3: <c>AnthropicAgentModel</c> populated <c>AgentResponse.Parts</c> on its unary
/// path and left it <see langword="null"/> on its streaming path, so a caller who switched
/// <c>GenerateAsync</c> → <c>GenerateStreamAsync</c> silently lost reasoning content — including the
/// <see cref="ReasoningPart.Signature"/> Q20 wired for multi-turn continuation.
/// </summary>
/// <remarks>
/// <para>
/// These tests exercise the private static <c>BuildStreamParts</c> by reflection rather than driving
/// <c>GenerateStreamAsync</c>, because the stream loop calls <c>_client.Messages.CreateStreaming</c>
/// and cannot run without a live endpoint. The assembly step is where the shape rule lives, so it is
/// the part worth pinning — same approach as <c>ProviderAdapterDocumentPartTests</c> (Q19).
/// </para>
/// <para>
/// <b>Why block order matters.</b> Anthropic sends a reasoning block's text over thinking deltas and
/// its signature as a *separate* delta afterwards, both keyed by block index. Assembling by index
/// is what keeps a streamed response the same shape as the unary one.
/// </para>
/// </remarks>
[TestFixture]
public class AnthropicStreamPartsAssemblyTests
{
    [Test]
    public void TextOnlyStream_LeavesPartsNull_SoTextCarriesIt()
    {
        // D1: parts are required only when the response holds something that is not a TextPart.
        var parts = BuildStreamParts((0, "Text", "hello world", null));

        parts.ShouldBeNull();
    }

    [Test]
    public void EmptyStream_LeavesPartsNull()
    {
        BuildStreamParts().ShouldBeNull();
    }

    [Test]
    public void ReasoningStream_PopulatesParts_AndCarriesSignature()
    {
        var parts = BuildStreamParts(
            (0, "Reasoning", "let me think about this", "opaque-sig-123"),
            (1, "Text", "the answer is 42", null));

        parts.ShouldNotBeNull();
        parts!.Count.ShouldBe(2);

        var reasoning = parts[0].ShouldBeOfType<ReasoningPart>();
        reasoning.Text.ShouldBe("let me think about this");
        reasoning.Signature.ShouldBe("opaque-sig-123",
            "the signature is what makes multi-turn reasoning continuation possible (Q20/SA8)");
        reasoning.IsRedacted.ShouldBeFalse();

        parts[1].ShouldBeOfType<TextPart>().Text.ShouldBe("the answer is 42");
    }

    [Test]
    public void ReasoningStream_PreservesProviderBlockOrder_NotArrivalOrder()
    {
        // The signature delta for block 0 arrives *after* block 1 has started in a real stream;
        // assembly must still order by block index.
        var parts = BuildStreamParts(
            (1, "Text", "answer", null),
            (0, "Reasoning", "thinking", "sig"));

        parts.ShouldNotBeNull();
        parts![0].ShouldBeOfType<ReasoningPart>();
        parts[1].ShouldBeOfType<TextPart>();
    }

    [Test]
    public void RedactedReasoning_IsFlagged_AndCarriesNoSignature()
    {
        var parts = BuildStreamParts((0, "RedactedReasoning", "opaque-payload", null));

        var reasoning = parts.ShouldNotBeNull()[0].ShouldBeOfType<ReasoningPart>();
        reasoning.IsRedacted.ShouldBeTrue();
        reasoning.Text.ShouldBe("opaque-payload");
        reasoning.Signature.ShouldBeNull();
    }

    [Test]
    public void UnsignedReasoning_StillProducesAPart_WithNullSignature()
    {
        // Capture stays lossless; it is the *request* side that rejects an unsigned block (Q20).
        var parts = BuildStreamParts((0, "Reasoning", "thinking with no signature", null));

        parts.ShouldNotBeNull()[0].ShouldBeOfType<ReasoningPart>().Signature.ShouldBeNull();
    }

    [Test]
    public void AssembledParts_DeriveTheSameText_AsTheUnaryPathWould()
    {
        // AgentResponse.Text is computed from TextPart entries when Parts is set, so a streamed
        // response carrying reasoning must still surface the plain answer through Text.
        var parts = BuildStreamParts(
            (0, "Reasoning", "internal", "sig"),
            (1, "Text", "visible answer", null));

        new AgentResponse { Parts = parts }.Text.ShouldBe("visible answer");
    }

    // ── reflection plumbing ──────────────────────────────────────────────────

    private static readonly Type ModelType = typeof(AnthropicAgentModel);

    private static readonly Type BlockKindType =
        ModelType.GetNestedType("StreamBlockKind", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AnthropicAgentModel.StreamBlockKind not found");

    private static readonly Type BlockType =
        ModelType.GetNestedType("StreamBlock", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AnthropicAgentModel.StreamBlock not found");

    /// <summary>
    /// Builds the <c>SortedDictionary&lt;long, StreamBlock&gt;</c> the stream loop accumulates, then
    /// invokes <c>BuildStreamParts</c> over it.
    /// </summary>
    private static IReadOnlyList<ContentPart>? BuildStreamParts(
        params (long Index, string Kind, string Text, string? Signature)[] blocks)
    {
        var dictType = typeof(SortedDictionary<,>).MakeGenericType(typeof(long), BlockType);
        var dict = (IDictionary)Activator.CreateInstance(dictType)!;

        foreach (var (index, kind, text, signature) in blocks)
        {
            var block = Activator.CreateInstance(
                BlockType,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.CreateInstance,
                binder: null,
                args: [Enum.Parse(BlockKindType, kind)],
                culture: null)!;

            var builder = (System.Text.StringBuilder)BlockType.GetProperty("Text")!.GetValue(block)!;
            builder.Append(text);

            if (signature is not null)
                BlockType.GetProperty("Signature")!.SetValue(block, signature);

            dict[index] = block;
        }

        var method = ModelType.GetMethod("BuildStreamParts", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("AnthropicAgentModel.BuildStreamParts not found");

        return (IReadOnlyList<ContentPart>?)method.Invoke(null, [dict]);
    }
}
