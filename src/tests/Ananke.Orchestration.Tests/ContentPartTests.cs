using Ananke.Orchestration.Agents;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class ContentPartTests
{
    [Test]
    public void User_WithStringContent_PreservesBackwardCompat()
    {
        var msg = AgentMessage.User("hello");

        msg.Role.ShouldBe(AgentRole.User);
        msg.Content.ShouldBe("hello");
        msg.Parts.ShouldBeNull();
    }

    [Test]
    public void User_WithParts_ComputesContentFromTextParts()
    {
        var msg = AgentMessage.User([
            new TextPart("hello "),
            new AudioPart([1, 2, 3], "audio/wav"),
            new TextPart("world")
        ]);

        msg.Role.ShouldBe(AgentRole.User);
        msg.Parts.ShouldNotBeNull();
        msg.Parts!.Count.ShouldBe(3);
        msg.Content.ShouldBe("hello world");
    }

    [Test]
    public void User_WithOnlyAudioPart_ContentIsNull()
    {
        var msg = AgentMessage.User([new AudioPart([1, 2, 3], "audio/wav")]);

        msg.Parts.ShouldNotBeNull();
        msg.Content.ShouldBeNull();
    }

    [Test]
    public void UserAudio_CreatesMessageWithAudioPart()
    {
        byte[] data = [10, 20, 30];
        var msg = AgentMessage.UserAudio(data, "audio/wav");

        msg.Role.ShouldBe(AgentRole.User);
        msg.Parts.ShouldNotBeNull();
        msg.Parts!.Count.ShouldBe(1);
        msg.Parts[0].ShouldBeOfType<AudioPart>();

        var audio = (AudioPart)msg.Parts[0];
        audio.Data.ShouldBe(data);
        audio.MimeType.ShouldBe("audio/wav");
    }

    [Test]
    public void AudioPart_SupportsOptionalProperties()
    {
        var audio = new AudioPart([1, 2], "audio/mp3")
        {
            Duration = TimeSpan.FromSeconds(3.5),
            Transcript = "hello world"
        };

        audio.Duration.ShouldBe(TimeSpan.FromSeconds(3.5));
        audio.Transcript.ShouldBe("hello world");
    }

    [Test]
    public void ImagePart_WithData()
    {
        byte[] data = [0xFF, 0xD8];
        var image = new ImagePart { Data = data, MimeType = "image/jpeg", AltText = "a photo" };

        image.Data.ShouldBe(data);
        image.Uri.ShouldBeNull();
        image.MimeType.ShouldBe("image/jpeg");
        image.AltText.ShouldBe("a photo");
    }

    [Test]
    public void ImagePart_WithUri()
    {
        var uri = new Uri("https://example.com/image.png");
        var image = new ImagePart { Uri = uri, MimeType = "image/png" };

        image.Data.ShouldBeNull();
        image.Uri.ShouldBe(uri);
        image.MimeType.ShouldBe("image/png");
    }

    [Test]
    public void User_WithMixedParts_RoundTrip()
    {
        byte[] audioData = [1, 2, 3, 4];
        byte[] imageData = [0xFF, 0xD8, 0xFF];

        var msg = AgentMessage.User([
            new TextPart("Describe this image and audio:"),
            new AudioPart(audioData, "audio/wav") { Transcript = "test audio" },
            new ImagePart { Data = imageData, MimeType = "image/jpeg", AltText = "test image" }
        ]);

        msg.Parts!.Count.ShouldBe(3);

        var text = msg.Parts[0].ShouldBeOfType<TextPart>();
        text.Text.ShouldBe("Describe this image and audio:");

        var audio = msg.Parts[1].ShouldBeOfType<AudioPart>();
        audio.Data.ShouldBe(audioData);
        audio.MimeType.ShouldBe("audio/wav");
        audio.Transcript.ShouldBe("test audio");

        var image = msg.Parts[2].ShouldBeOfType<ImagePart>();
        image.Data.ShouldBe(imageData);
        image.MimeType.ShouldBe("image/jpeg");
        image.AltText.ShouldBe("test image");

        msg.Content.ShouldBe("Describe this image and audio:");
    }

    [Test]
    public void AgentResponse_WithParts_ComputesTextFromTextParts()
    {
        var response = new AgentResponse
        {
            Parts = [
                new TextPart("response "),
                new AudioPart([5, 6], "audio/pcm"),
                new TextPart("text")
            ]
        };

        response.Text.ShouldBe("response text");
        response.Parts!.Count.ShouldBe(3);
    }

    [Test]
    public void AgentResponse_WithTextOnly_PreservesBackwardCompat()
    {
        var response = new AgentResponse { Text = "hello" };

        response.Text.ShouldBe("hello");
        response.Parts.ShouldBeNull();
    }

    [Test]
    public void AgentResponse_WithOnlyAudioParts_TextIsNull()
    {
        var response = new AgentResponse
        {
            Parts = [new AudioPart([1, 2], "audio/wav")]
        };

        response.Text.ShouldBeNull();
    }

    [Test]
    public void AgentStreamChunk_AudioDelta_Properties()
    {
        byte[] audio = [10, 20, 30];
        var chunk = new AgentStreamChunk
        {
            AudioDelta = audio,
            AudioMimeType = "audio/pcm",
            TranscriptDelta = "hello"
        };

        chunk.AudioDelta.ShouldBe(audio);
        chunk.AudioMimeType.ShouldBe("audio/pcm");
        chunk.TranscriptDelta.ShouldBe("hello");
        chunk.TextDelta.ShouldBeNull();
        chunk.CompletedResponse.ShouldBeNull();
    }

    [Test]
    public void ExistingTextOnly_AgentMessage_User_WorksUnchanged()
    {
        var msg = AgentMessage.User("test");
        msg.Content.ShouldBe("test");
        msg.Role.ShouldBe(AgentRole.User);
    }

    [Test]
    public void ExistingTextOnly_AgentMessage_System_WorksUnchanged()
    {
        var msg = AgentMessage.System("system prompt");
        msg.Content.ShouldBe("system prompt");
        msg.Role.ShouldBe(AgentRole.System);
    }

    [Test]
    public void ExistingTextOnly_AgentMessage_Assistant_WorksUnchanged()
    {
        var msg = AgentMessage.Assistant("response");
        msg.Content.ShouldBe("response");
        msg.Role.ShouldBe(AgentRole.Assistant);
    }

    [Test]
    public void ExistingTextOnly_AgentMessage_ToolResult_WorksUnchanged()
    {
        var msg = AgentMessage.ToolResult("tc1", "result");
        msg.Content.ShouldBe("result");
        msg.Role.ShouldBe(AgentRole.Tool);
        msg.ToolCallId.ShouldBe("tc1");
    }
}
