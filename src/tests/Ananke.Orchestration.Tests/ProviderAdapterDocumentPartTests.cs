using System.Reflection;
using System.Text;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Anthropic;
using Ananke.Orchestration.Google;
using Ananke.Orchestration.OpenAI;
using Anthropic.Models.Messages;
using Google.GenAI.Types;
using OpenAI.Chat;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Q19: verifies each adapter's request-side mapping for the <see cref="DocumentPart"/> cases
/// its SDK actually supports (see the corresponding <c>case DocumentPart</c> arms added to
/// <c>AnthropicAgentModel</c>, <c>GeminiAgentModel</c>, and <c>OpenAIChatAgentModel</c>).
/// </summary>
/// <remarks>
/// These assert on request <em>payload construction</em>, not on a response — invoked via
/// reflection against each adapter's private static mapping method so the tests never touch
/// the network (the alternative, calling <c>GenerateAsync</c> with a fake key on a genuinely
/// supported part, would proceed past mapping and attempt a real HTTP call).
/// </remarks>
[TestFixture]
public class ProviderAdapterDocumentPartTests
{
    // ── OpenAI ───────────────────────────────────────────────────────────────

#pragma warning disable OPENAI001 // File content parts are an experimental OpenAI SDK surface
    [Test]
    public void OpenAI_DocumentPartWithData_MapsToFilePartWithGivenName()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var request = RequestWith(new DocumentPart { MimeType = "application/pdf", Data = bytes, Name = "report.pdf" });

        var messages = InvokeMapMessages<ChatMessage>(typeof(OpenAIChatAgentModel), request);

        var content = ((UserChatMessage)messages[0]).Content;
        content.Count.ShouldBe(1);
        content[0].Kind.ShouldBe(ChatMessageContentPartKind.File);
        content[0].FileBytesMediaType.ShouldBe("application/pdf");
        content[0].Filename.ShouldBe("report.pdf");
        content[0].FileBytes.ToArray().ShouldBe(bytes);
    }

    [Test]
    public void OpenAI_DocumentPartWithData_NoNameGiven_DefaultsFilenameToDocument()
    {
        var request = RequestWith(new DocumentPart { MimeType = "application/pdf", Data = [1, 2, 3] });

        var messages = InvokeMapMessages<ChatMessage>(typeof(OpenAIChatAgentModel), request);

        ((UserChatMessage)messages[0]).Content[0].Filename.ShouldBe("document");
    }
#pragma warning restore OPENAI001

    // ── Anthropic ────────────────────────────────────────────────────────────

    [Test]
    public void Anthropic_PdfDocumentWithData_MapsToBase64PdfSource()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var request = RequestWith(new DocumentPart { MimeType = "application/pdf", Data = bytes });

        var messages = InvokeMapMessages<MessageParam>(typeof(AnthropicAgentModel), request);

        messages[0].Content!.TryPickContentBlockParams(out var blocks).ShouldBeTrue();
        var block = blocks!.Single();
        block.TryPickDocument(out var doc).ShouldBeTrue();
        doc!.Source.TryPickBase64Pdf(out var pdfSource).ShouldBeTrue();
        pdfSource.Data.ShouldBe(Convert.ToBase64String(bytes));
    }

    [Test]
    public void Anthropic_PlainTextDocumentWithData_MapsToPlainTextSource()
    {
        var request = RequestWith(new DocumentPart { MimeType = "text/plain", Data = Encoding.UTF8.GetBytes("hello doc") });

        var messages = InvokeMapMessages<MessageParam>(typeof(AnthropicAgentModel), request);

        messages[0].Content!.TryPickContentBlockParams(out var blocks).ShouldBeTrue();
        var block = blocks!.Single();
        block.TryPickDocument(out var doc).ShouldBeTrue();
        doc!.Source.TryPickPlainText(out var textSource).ShouldBeTrue();
        textSource.Data.ShouldBe("hello doc");
    }

    [Test]
    public void Anthropic_PdfDocumentWithUri_MapsToUrlPdfSource()
    {
        var request = RequestWith(new DocumentPart { MimeType = "application/pdf", Uri = new Uri("https://example.com/doc.pdf") });

        var messages = InvokeMapMessages<MessageParam>(typeof(AnthropicAgentModel), request);

        messages[0].Content!.TryPickContentBlockParams(out var blocks).ShouldBeTrue();
        var block = blocks!.Single();
        block.TryPickDocument(out var doc).ShouldBeTrue();
        doc!.Source.TryPickUrlPdf(out var urlSource).ShouldBeTrue();
        urlSource.Url.ShouldBe("https://example.com/doc.pdf");
    }

    [Test]
    public void Anthropic_PlainTextDocumentWithUriOnly_IsStillUnsupported()
    {
        // Only the PDF source has a URL variant in the SDK — plain text does not.
        var request = RequestWith(new DocumentPart { MimeType = "text/plain", Uri = new Uri("https://example.com/doc.txt") });

        var ex = Should.Throw<TargetInvocationException>(() =>
            InvokeMapMessages<MessageParam>(typeof(AnthropicAgentModel), request));

        ex.InnerException.ShouldBeOfType<NotSupportedException>();
    }

    // ── Google ───────────────────────────────────────────────────────────────

    [Test]
    public void Gemini_DocumentPartWithData_MapsToInlineData()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var msg = AgentMessage.User([new DocumentPart { MimeType = "application/pdf", Data = bytes }]);

        var parts = InvokeMapParts(msg);

        parts.Count.ShouldBe(1);
        parts[0].InlineData!.MimeType.ShouldBe("application/pdf");
        parts[0].InlineData!.Data.ShouldBe(bytes);
    }

    [Test]
    public void Gemini_DocumentPartWithUri_MapsToFileData()
    {
        var msg = AgentMessage.User([new DocumentPart { MimeType = "application/pdf", Uri = new Uri("https://example.com/doc.pdf") }]);

        var parts = InvokeMapParts(msg);

        parts.Count.ShouldBe(1);
        parts[0].FileData!.MimeType.ShouldBe("application/pdf");
        parts[0].FileData!.FileUri.ShouldBe("https://example.com/doc.pdf");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static AgentRequest RequestWith(ContentPart part) => new()
    {
        Messages = [AgentMessage.User([part])]
    };

    private static List<T> InvokeMapMessages<T>(System.Type adapterType, AgentRequest request)
    {
        var method = adapterType.GetMethod("MapMessages", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(adapterType.Name, "MapMessages");
        return (List<T>)method.Invoke(null, [request])!;
    }

    private static List<Part> InvokeMapParts(AgentMessage msg)
    {
        var method = typeof(GeminiAgentModel).GetMethod("MapParts", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(GeminiAgentModel), "MapParts");
        return (List<Part>)method.Invoke(null, [msg])!;
    }
}
