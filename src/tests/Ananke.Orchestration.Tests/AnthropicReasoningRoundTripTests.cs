using System.Reflection;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Anthropic;
using Anthropic.Models.Messages;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Q20: <c>AnthropicAgentModel.MapMessage</c> captures a reasoning block's <c>Signature</c> on the
/// response side (<see cref="ReasoningPart.Signature"/>), but nothing in the solution echoed it
/// back on a later turn — the capture existed with no consumer. These tests verify the request-side
/// mapping added for <see cref="ReasoningPart"/> (in the same switch <c>ProviderAdapterDocumentPartTests</c>
/// exercises for <see cref="DocumentPart"/>) actually carries the signature through.
/// </summary>
/// <remarks>
/// <para>
/// SA8 requires the signature to be echoed back for providers that need it re-supplied for
/// multi-turn continuation — Anthropic is one. The response-side capture at
/// <c>AnthropicAgentModel.cs:136</c> is unchanged by this; these tests cover only the request side.
/// </para>
/// <para>
/// <b>No production caller yet.</b> Nothing in <c>AgentJob</c>/<c>TextAgentJob</c>'s multi-turn
/// history-building copies a previous <c>AgentResponse.Parts</c> back into a subsequent
/// <c>AgentMessage.Parts</c> — that wiring is separate, unfiled work. Until it exists, this request-side
/// mapping has no way to be reached by a real conversation; these tests are its only exerciser and
/// exist so the capability is verified and doesn't silently regress before that wiring lands.
/// </para>
/// </remarks>
[TestFixture]
public class AnthropicReasoningRoundTripTests
{
    [Test]
    public void SignedReasoningPart_RoundTripsSignatureIntoThinkingBlockParam()
    {
        // Simulates a response-side ReasoningPart (as MapMessage would produce it) fed back as the
        // next turn's request content.
        var responsePart = new ReasoningPart("the model's prior reasoning") { Signature = "opaque-sig-123" };
        var request = RequestWith(responsePart);

        var messages = InvokeMapMessages(request);

        messages[0].Content!.TryPickContentBlockParams(out var blocks).ShouldBeTrue();
        var block = blocks!.Single();
        block.TryPickThinking(out var thinking).ShouldBeTrue();
        thinking!.Thinking.ShouldBe("the model's prior reasoning");
        thinking.Signature.ShouldBe("opaque-sig-123");
    }

    [Test]
    public void RedactedReasoningPart_RoundTripsOpaqueDataIntoRedactedThinkingBlockParam()
    {
        var responsePart = new ReasoningPart("opaque-redacted-payload") { IsRedacted = true };
        var request = RequestWith(responsePart);

        var messages = InvokeMapMessages(request);

        messages[0].Content!.TryPickContentBlockParams(out var blocks).ShouldBeTrue();
        var block = blocks!.Single();
        block.TryPickRedactedThinking(out var redacted).ShouldBeTrue();
        redacted!.Data.ShouldBe("opaque-redacted-payload");
    }

    [Test]
    public void UnsignedNonRedactedReasoningPart_ThrowsNotSupported()
    {
        // Anthropic rejects a thinking block sent back without a valid signature — a local
        // exception here is preferable to letting that surface as an opaque API 400.
        var responsePart = new ReasoningPart("reasoning with no signature");
        var request = RequestWith(responsePart);

        var ex = Should.Throw<TargetInvocationException>(() => InvokeMapMessages(request));

        ex.InnerException.ShouldBeOfType<NotSupportedException>();
        ex.InnerException!.Message.ShouldContain("Signature");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static AgentRequest RequestWith(ContentPart part) => new()
    {
        Messages = [AgentMessage.User([part])]
    };

    private static List<MessageParam> InvokeMapMessages(AgentRequest request)
    {
        var method = typeof(AnthropicAgentModel).GetMethod("MapMessages", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(AnthropicAgentModel), "MapMessages");
        return (List<MessageParam>)method.Invoke(null, [request])!;
    }
}
