using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Anthropic;
using Ananke.Orchestration.Google;
using Ananke.Orchestration.OpenAI;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Q19: every provider adapter's request-side content-part mapping now throws on an
/// unrecognised <see cref="ContentPart"/> instead of silently dropping it. All three
/// clients build their request payload (and can therefore throw) before making any
/// network call, so these run against a dummy API key with no live credentials needed.
/// </summary>
[TestFixture]
public class ProviderAdapterUnsupportedPartTests
{
    private static AgentRequest RequestWithDocumentPart() => new()
    {
        Messages =
        [
            AgentMessage.User([new DocumentPart { MimeType = "application/pdf", Data = [0x25, 0x50, 0x44, 0x46] }])
        ]
    };

    [Test]
    public async Task OpenAI_UnsupportedPart_ThrowsNotSupported()
    {
        var model = OpenAIChatAgentModel.Create("fake-key", "gpt-4.1-mini");

        var ex = await Should.ThrowAsync<NotSupportedException>(
            () => model.GenerateAsync(RequestWithDocumentPart()));

        ex.Message.ShouldContain(nameof(DocumentPart));
    }

    [Test]
    public async Task Anthropic_UnsupportedPart_ThrowsNotSupported()
    {
        var model = AnthropicAgentModel.Create("fake-key", "claude-sonnet-4-5");

        var ex = await Should.ThrowAsync<NotSupportedException>(
            () => model.GenerateAsync(RequestWithDocumentPart()));

        ex.Message.ShouldContain(nameof(DocumentPart));
    }

    [Test]
    public async Task Gemini_UnsupportedPart_ThrowsNotSupported()
    {
        var model = GeminiAgentModel.Create("fake-key", "gemini-2.5-flash");

        var ex = await Should.ThrowAsync<NotSupportedException>(
            () => model.GenerateAsync(RequestWithDocumentPart()));

        ex.Message.ShouldContain(nameof(DocumentPart));
    }
}
