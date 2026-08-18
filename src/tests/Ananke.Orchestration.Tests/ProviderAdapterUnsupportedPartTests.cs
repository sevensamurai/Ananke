using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Anthropic;
using Ananke.Orchestration.Google;
using Ananke.Orchestration.OpenAI;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Q19: every provider adapter's request-side content-part mapping throws on a content part
/// it genuinely cannot send, instead of silently dropping it. All three clients build their
/// request payload (and can therefore throw) before making any network call, so these run
/// against a dummy API key with no live credentials needed.
/// </summary>
/// <remarks>
/// The minimum fix (2026-08-03) covered <em>every</em> unrecognised part with one <c>DocumentPart</c>
/// fixture per adapter. The full fix (2026-08-17) taught each adapter to map <c>DocumentPart</c> in
/// the cases its SDK actually supports (see <c>ProviderAdapterDocumentPartTests</c>), so that shared
/// fixture would no longer throw anywhere. Each adapter below now uses the narrowest case that is
/// still genuinely unsupported for that specific adapter, chosen so it stays unsupported even after
/// Q20 (Anthropic-only <c>ReasoningPart</c> echoing) lands.
/// </remarks>
[TestFixture]
public class ProviderAdapterUnsupportedPartTests
{
    private static AgentRequest RequestWith(ContentPart part) => new()
    {
        Messages = [AgentMessage.User([part])]
    };

    [Test]
    public async Task OpenAI_DocumentPartWithOnlyUri_ThrowsNotSupported()
    {
        // OpenAI's file content part has no URI overload — only bytes or a pre-uploaded file ID.
        var model = OpenAIChatAgentModel.Create("fake-key", "gpt-4.1-mini");
        var request = RequestWith(new DocumentPart
        {
            MimeType = "application/pdf",
            Uri = new Uri("https://example.com/doc.pdf")
        });

        var ex = await Should.ThrowAsync<NotSupportedException>(() => model.GenerateAsync(request));

        ex.Message.ShouldContain(nameof(DocumentPart));
    }

    [Test]
    public async Task Anthropic_DocumentPartWithUnsupportedMimeType_ThrowsNotSupported()
    {
        // Anthropic only supports application/pdf and text/plain document sources.
        var model = AnthropicAgentModel.Create("fake-key", "claude-sonnet-4-5");
        var request = RequestWith(new DocumentPart
        {
            MimeType = "application/msword",
            Data = [0x25, 0x50, 0x44, 0x46]
        });

        var ex = await Should.ThrowAsync<NotSupportedException>(() => model.GenerateAsync(request));

        ex.Message.ShouldContain(nameof(DocumentPart));
        ex.Message.ShouldContain("application/msword");
    }

    [Test]
    public async Task Gemini_UnsupportedPart_ThrowsNotSupported()
    {
        // Neither Gemini nor OpenAI emit reasoning content, so ReasoningPart stays unsupported
        // on the request side for both — unaffected by Q19 or Q20.
        var model = GeminiAgentModel.Create("fake-key", "gemini-2.5-flash");
        var request = RequestWith(new ReasoningPart("some reasoning") { Signature = "sig" });

        var ex = await Should.ThrowAsync<NotSupportedException>(() => model.GenerateAsync(request));

        ex.Message.ShouldContain(nameof(ReasoningPart));
    }
}
