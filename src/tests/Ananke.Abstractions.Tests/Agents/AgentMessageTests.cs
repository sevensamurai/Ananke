using System.Text.Json;
using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Abstractions.Tests.Agents;

// ══════════════════════════════════════════════════════════════════════
//  AgentMessage
// ══════════════════════════════════════════════════════════════════════

[TestFixture]
public class AgentMessageTests
{
    // ── Static factories ──────────────────────────────────────────────

    [Test]
    public void System_SetsRoleAndContent()
    {
        var msg = AgentMessage.System("Be helpful.");

        msg.Role.ShouldBe(AgentRole.System);
        msg.Content.ShouldBe("Be helpful.");
        msg.ToolCalls.ShouldBeNull();
        msg.Parts.ShouldBeNull();
    }

    [Test]
    public void User_WithText_SetsRoleAndContent()
    {
        var msg = AgentMessage.User("Hello");

        msg.Role.ShouldBe(AgentRole.User);
        msg.Content.ShouldBe("Hello");
        msg.Parts.ShouldBeNull();
    }

    [Test]
    public void User_WithParts_SetsPartsAndDerivesContentFromTextParts()
    {
        var parts = (IReadOnlyList<ContentPart>)[new TextPart("Hi"), new TextPart(" there")];
        var msg = AgentMessage.User(parts);

        msg.Role.ShouldBe(AgentRole.User);
        msg.Parts.ShouldBe(parts);
        msg.Content.ShouldBe("Hi there");
    }

    [Test]
    public void UserAudio_SetsAudioPart()
    {
        var data = new byte[] { 1, 2, 3 };
        var msg = AgentMessage.UserAudio(data, "audio/wav");

        msg.Role.ShouldBe(AgentRole.User);
        msg.Parts!.Count.ShouldBe(1);
        var audio = msg.Parts[0].ShouldBeOfType<AudioPart>();
        audio.Data.ShouldBe(data);
        audio.MimeType.ShouldBe("audio/wav");
    }

    [Test]
    public void UserImage_WithText_IncludesTextPartThenImagePart()
    {
        var data = new byte[] { 0xFF, 0xD8 };
        var msg = AgentMessage.UserImage(data, "image/jpeg", "A cat");

        msg.Parts!.Count.ShouldBe(2);
        msg.Parts[0].ShouldBeOfType<TextPart>().Text.ShouldBe("A cat");
        msg.Parts[1].ShouldBeOfType<ImagePart>().MimeType.ShouldBe("image/jpeg");
    }

    [Test]
    public void UserImage_WithoutText_OnlyImagePart()
    {
        var data = new byte[] { 0xFF, 0xD8 };
        var msg = AgentMessage.UserImage(data, "image/jpeg");

        msg.Parts!.Count.ShouldBe(1);
        msg.Parts[0].ShouldBeOfType<ImagePart>();
    }

    [Test]
    public void Assistant_SetsRoleAndContent()
    {
        var msg = AgentMessage.Assistant("I can help.");

        msg.Role.ShouldBe(AgentRole.Assistant);
        msg.Content.ShouldBe("I can help.");
        msg.ToolCalls.ShouldBeNull();
    }

    [Test]
    public void Assistant_WithToolCalls_SetsToolCalls()
    {
        var calls = (IReadOnlyList<AgentToolCall>)[new AgentToolCall("id1", "search", "{\"q\":\"cats\"}")];
        var msg = AgentMessage.Assistant("", calls);

        msg.ToolCalls.ShouldBe(calls);
    }

    [Test]
    public void ToolResult_SetsRoleContentAndCallId()
    {
        var msg = AgentMessage.ToolResult("call-1", "42");

        msg.Role.ShouldBe(AgentRole.Tool);
        msg.Content.ShouldBe("42");
        msg.ToolCallId.ShouldBe("call-1");
    }

    // ── Content property: Parts vs direct ────────────────────────────

    [Test]
    public void Content_DirectValue_ReturnsDirect()
    {
        var msg = new AgentMessage { Role = AgentRole.User, Content = "direct" };
        msg.Content.ShouldBe("direct");
    }

    [Test]
    public void Content_EmptyPartsList_FallsBackToDirectContent()
    {
        var msg = new AgentMessage { Role = AgentRole.User, Content = "fallback", Parts = [] };
        msg.Content.ShouldBe("fallback");
    }

    [Test]
    public void Content_OnlyNonTextParts_ReturnsNull()
    {
        var msg = AgentMessage.User([new ImagePart { MimeType = "image/png" }]);
        msg.Content.ShouldBeNull();
    }
}

// ══════════════════════════════════════════════════════════════════════
//  AgentResponse
// ══════════════════════════════════════════════════════════════════════

[TestFixture]
public class AgentResponseTests
{
    [Test]
    public void Text_DirectValue_ReturnsDirect()
    {
        var resp = new AgentResponse { Text = "hello" };
        resp.Text.ShouldBe("hello");
    }

    [Test]
    public void Text_WithMultipleTextParts_ConcatenatesAll()
    {
        var resp = new AgentResponse { Parts = [new TextPart("foo"), new TextPart("bar")] };
        resp.Text.ShouldBe("foobar");
    }

    [Test]
    public void Text_EmptyPartsList_FallsBackToDirect()
    {
        var resp = new AgentResponse { Text = "direct", Parts = [] };
        resp.Text.ShouldBe("direct");
    }

    [Test]
    public void Text_OnlyNonTextParts_ReturnsNull()
    {
        var resp = new AgentResponse { Parts = [new ImagePart { MimeType = "image/png" }] };
        resp.Text.ShouldBeNull();
    }

    [Test]
    public void Text_WithReasoningAndTextParts_ExcludesReasoning()
    {
        // Mirrors Anthropic's real block order (thinking before text), which is what caused
        // reasoning to leak into Text before ReasoningPart existed.
        var resp = new AgentResponse
        {
            Parts = [new ReasoningPart("let me think..."), new TextPart("the answer")]
        };

        resp.Text.ShouldBe("the answer");
    }

    [Test]
    public void Text_OnlyReasoningParts_ReturnsNull()
    {
        var resp = new AgentResponse { Parts = [new ReasoningPart("internal monologue")] };
        resp.Text.ShouldBeNull();
    }

    [Test]
    public void RequiresAction_WithToolCalls_ReturnsTrue()
    {
        var resp = new AgentResponse
        {
            ToolCalls = [new AgentToolCall("id1", "get_weather", "{}")]
        };
        resp.RequiresAction.ShouldBeTrue();
    }

    [Test]
    public void RequiresAction_EmptyToolCallsList_ReturnsFalse()
    {
        var resp = new AgentResponse { ToolCalls = [] };
        resp.RequiresAction.ShouldBeFalse();
    }

    [Test]
    public void RequiresAction_NullToolCalls_ReturnsFalse()
    {
        var resp = new AgentResponse { Text = "done" };
        resp.RequiresAction.ShouldBeFalse();
    }

    [Test]
    public void Usage_IsNullByDefault()
    {
        var resp = new AgentResponse();
        resp.Usage.ShouldBeNull();
    }
}

// ══════════════════════════════════════════════════════════════════════
//  TokenUsage
// ══════════════════════════════════════════════════════════════════════

[TestFixture]
public class TokenUsageTests
{
    [Test]
    public void TotalTokens_SumsInputAndOutput()
    {
        var usage = new TokenUsage { InputTokens = 100, OutputTokens = 50 };
        usage.TotalTokens.ShouldBe(150);
    }

    [Test]
    public void Add_SumsTokenCounts()
    {
        var a = new TokenUsage { InputTokens = 100, OutputTokens = 50 };
        var b = new TokenUsage { InputTokens = 200, OutputTokens = 75 };

        var sum = a.Add(b);

        sum.InputTokens.ShouldBe(300);
        sum.OutputTokens.ShouldBe(125);
        sum.TotalTokens.ShouldBe(425);
    }

    [Test]
    public void Zero_HasAllZeroFields()
    {
        TokenUsage.Zero.InputTokens.ShouldBe(0);
        TokenUsage.Zero.OutputTokens.ShouldBe(0);
        TokenUsage.Zero.TotalTokens.ShouldBe(0);
    }

    [Test]
    public void Zero_Added_ReturnsOtherValues()
    {
        var usage = new TokenUsage { InputTokens = 10, OutputTokens = 5 };
        var sum = TokenUsage.Zero.Add(usage);

        sum.InputTokens.ShouldBe(10);
        sum.OutputTokens.ShouldBe(5);
    }
}

// ══════════════════════════════════════════════════════════════════════
//  ContentPart subtypes
// ══════════════════════════════════════════════════════════════════════

[TestFixture]
public class ContentPartTests
{
    [Test]
    public void TextPart_RoundTrips()
    {
        var part = new TextPart("hello world");
        part.Text.ShouldBe("hello world");
    }

    [Test]
    public void AudioPart_RoundTrips()
    {
        var data = new byte[] { 1, 2, 3 };
        var part = new AudioPart(data, "audio/mp3")
        {
            Duration = TimeSpan.FromSeconds(5),
            Transcript = "hi there"
        };

        part.Data.ShouldBe(data);
        part.MimeType.ShouldBe("audio/mp3");
        part.Duration.ShouldBe(TimeSpan.FromSeconds(5));
        part.Transcript.ShouldBe("hi there");
    }

    [Test]
    public void AudioPart_OptionalFieldsDefaultToNull()
    {
        var part = new AudioPart([0x00], "audio/ogg");
        part.Duration.ShouldBeNull();
        part.Transcript.ShouldBeNull();
    }

    [Test]
    public void ImagePart_WithDataBytes_RoundTrips()
    {
        var data = new byte[] { 0xFF, 0xD8, 0xFF };
        var part = new ImagePart
        {
            Data = data,
            MimeType = "image/jpeg",
            AltText = "A landscape"
        };

        part.Data.ShouldBe(data);
        part.MimeType.ShouldBe("image/jpeg");
        part.AltText.ShouldBe("A landscape");
        part.Uri.ShouldBeNull();
    }

    [Test]
    public void ImagePart_WithUri_RoundTrips()
    {
        var uri = new Uri("https://example.com/img.png");
        var part = new ImagePart { Uri = uri, MimeType = "image/png" };

        part.Uri.ShouldBe(uri);
        part.Data.ShouldBeNull();
    }

    [Test]
    public void ImagePart_AltTextDefaultsToNull()
    {
        var part = new ImagePart { MimeType = "image/webp" };
        part.AltText.ShouldBeNull();
    }

    [Test]
    public void ReasoningPart_RoundTrips()
    {
        var part = new ReasoningPart("thinking about it") { Signature = "sig-123" };

        part.Text.ShouldBe("thinking about it");
        part.Signature.ShouldBe("sig-123");
        part.IsRedacted.ShouldBeFalse();
    }

    [Test]
    public void ReasoningPart_OptionalFieldsDefaultToNullAndFalse()
    {
        var part = new ReasoningPart("thinking");
        part.Signature.ShouldBeNull();
        part.IsRedacted.ShouldBeFalse();
    }

    [Test]
    public void ReasoningPart_Redacted_RoundTrips()
    {
        var part = new ReasoningPart("opaque-encrypted-payload") { IsRedacted = true };

        part.Text.ShouldBe("opaque-encrypted-payload");
        part.IsRedacted.ShouldBeTrue();
    }

    [Test]
    public void DocumentPart_WithDataBytes_RoundTrips()
    {
        var data = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var part = new DocumentPart
        {
            Data = data,
            MimeType = "application/pdf",
            Name = "report.pdf"
        };

        part.Data.ShouldBe(data);
        part.MimeType.ShouldBe("application/pdf");
        part.Name.ShouldBe("report.pdf");
        part.Uri.ShouldBeNull();
    }

    [Test]
    public void DocumentPart_WithUri_RoundTrips()
    {
        var uri = new Uri("https://example.com/report.pdf");
        var part = new DocumentPart { Uri = uri, MimeType = "application/pdf" };

        part.Uri.ShouldBe(uri);
        part.Data.ShouldBeNull();
    }

    [Test]
    public void DocumentPart_NameDefaultsToNull()
    {
        var part = new DocumentPart { MimeType = "application/pdf" };
        part.Name.ShouldBeNull();
    }

    // ── Polymorphic JSON round-trip: catches a forgotten [JsonDerivedType] registration ──

    [Test]
    public void ReasoningPart_SerializesAndDeserializesThroughContentPart()
    {
        ContentPart original = new ReasoningPart("thinking") { Signature = "sig", IsRedacted = true };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<ContentPart>(json);

        var reasoning = roundTripped.ShouldBeOfType<ReasoningPart>();
        reasoning.Text.ShouldBe("thinking");
        reasoning.Signature.ShouldBe("sig");
        reasoning.IsRedacted.ShouldBeTrue();
    }

    [Test]
    public void DocumentPart_SerializesAndDeserializesThroughContentPart()
    {
        ContentPart original = new DocumentPart { MimeType = "application/pdf", Name = "x.pdf" };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<ContentPart>(json);

        var document = roundTripped.ShouldBeOfType<DocumentPart>();
        document.MimeType.ShouldBe("application/pdf");
        document.Name.ShouldBe("x.pdf");
    }
}

// ══════════════════════════════════════════════════════════════════════
//  AgentToolCall
// ══════════════════════════════════════════════════════════════════════

[TestFixture]
public class AgentToolCallTests
{
    [Test]
    public void AgentToolCall_PropertiesRoundTrip()
    {
        var call = new AgentToolCall("call-123", "search_web", "{\"query\":\"cats\"}");

        call.Id.ShouldBe("call-123");
        call.FunctionName.ShouldBe("search_web");
        call.Arguments.ShouldBe("{\"query\":\"cats\"}");
    }

    [Test]
    public void AgentToolCall_EqualityByValue()
    {
        var a = new AgentToolCall("id", "fn", "{}");
        var b = new AgentToolCall("id", "fn", "{}");
        a.ShouldBe(b);
    }
}

// ══════════════════════════════════════════════════════════════════════
//  AgentRequest + AgentTool + AgentResponseFormat
// ══════════════════════════════════════════════════════════════════════

[TestFixture]
public class AgentRequestTests
{
    [Test]
    public void AgentRequest_StoreCompletions_DefaultsToFalse()
    {
        var request = new AgentRequest { Messages = [AgentMessage.User("hi")] };
        request.StoreCompletions.ShouldBeFalse();
    }

    [Test]
    public void AgentRequest_CanEnableStoreCompletions()
    {
        var request = new AgentRequest
        {
            Messages = [AgentMessage.User("hi")],
            StoreCompletions = true
        };
        request.StoreCompletions.ShouldBeTrue();
    }

    [Test]
    public void AgentRequest_OptionalPropertiesDefaultToNull()
    {
        var request = new AgentRequest { Messages = [AgentMessage.User("hi")] };

        request.SystemPrompt.ShouldBeNull();
        request.Tools.ShouldBeNull();
        request.ResponseFormat.ShouldBeNull();
        request.Metadata.ShouldBeNull();
    }

    [Test]
    public void AgentTool_PropertiesRoundTrip()
    {
        var tool = new AgentTool("search", "Search the web", "{\"type\":\"object\"}");

        tool.Name.ShouldBe("search");
        tool.Description.ShouldBe("Search the web");
        tool.ParametersJsonSchema.ShouldBe("{\"type\":\"object\"}");
    }

    [Test]
    public void AgentResponseFormat_StrictDefaultsToTrue()
    {
        var fmt = new AgentResponseFormat("schema-name", "{}");
        fmt.Strict.ShouldBeTrue();
    }

    [Test]
    public void AgentResponseFormat_CanDisableStrict()
    {
        var fmt = new AgentResponseFormat("schema-name", "{}", Strict: false);
        fmt.Strict.ShouldBeFalse();
    }

    [Test]
    public void AgentResponseFormat_PropertiesRoundTrip()
    {
        var fmt = new AgentResponseFormat("my-schema", "{\"type\":\"object\"}");

        fmt.SchemaName.ShouldBe("my-schema");
        fmt.JsonSchema.ShouldBe("{\"type\":\"object\"}");
    }
}
