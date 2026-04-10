using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class TaskRequirementsMultimodalTests
{
    [Test]
    public void InferFrom_WithAudioPart_RequiresAudioInput()
    {
        var request = new AgentRequest
        {
            Messages = [AgentMessage.UserAudio([1, 2, 3], "audio/wav")]
        };

        var requirements = TaskRequirements.InferFrom(request);

        requirements.RequiredCapabilities.HasFlag(ModelCapability.AudioInput).ShouldBeTrue();
        requirements.RequiredCapabilities.HasFlag(ModelCapability.TextGeneration).ShouldBeTrue();
    }

    [Test]
    public void InferFrom_WithImagePart_RequiresVision()
    {
        var request = new AgentRequest
        {
            Messages = [AgentMessage.User([
                new TextPart("What is this?"),
                new ImagePart { Data = [0xFF, 0xD8], MimeType = "image/jpeg" }
            ])]
        };

        var requirements = TaskRequirements.InferFrom(request);

        requirements.RequiredCapabilities.HasFlag(ModelCapability.Vision).ShouldBeTrue();
        requirements.RequiredCapabilities.HasFlag(ModelCapability.TextGeneration).ShouldBeTrue();
    }

    [Test]
    public void InferFrom_WithAudioAndImageParts_RequiresBoth()
    {
        var request = new AgentRequest
        {
            Messages = [AgentMessage.User([
                new TextPart("Describe both"),
                new AudioPart([1, 2], "audio/wav"),
                new ImagePart { Data = [0xFF], MimeType = "image/png" }
            ])]
        };

        var requirements = TaskRequirements.InferFrom(request);

        requirements.RequiredCapabilities.HasFlag(ModelCapability.AudioInput).ShouldBeTrue();
        requirements.RequiredCapabilities.HasFlag(ModelCapability.Vision).ShouldBeTrue();
        requirements.RequiredCapabilities.HasFlag(ModelCapability.TextGeneration).ShouldBeTrue();
    }

    [Test]
    public void InferFrom_WithTextOnlyParts_DoesNotRequireMultimodal()
    {
        var request = new AgentRequest
        {
            Messages = [AgentMessage.User([new TextPart("just text")])]
        };

        var requirements = TaskRequirements.InferFrom(request);

        requirements.RequiredCapabilities.ShouldBe(ModelCapability.TextGeneration);
    }

    [Test]
    public void InferFrom_WithNoParts_DoesNotRequireMultimodal()
    {
        var request = new AgentRequest
        {
            Messages = [AgentMessage.User("hello")]
        };

        var requirements = TaskRequirements.InferFrom(request);

        requirements.RequiredCapabilities.ShouldBe(ModelCapability.TextGeneration);
    }

    [Test]
    public void InferFrom_MultimodalAcrossMultipleMessages()
    {
        var request = new AgentRequest
        {
            Messages = [
                AgentMessage.User([new AudioPart([1], "audio/wav")]),
                AgentMessage.User([new ImagePart { Data = [2], MimeType = "image/png" }])
            ]
        };

        var requirements = TaskRequirements.InferFrom(request);

        requirements.RequiredCapabilities.HasFlag(ModelCapability.AudioInput).ShouldBeTrue();
        requirements.RequiredCapabilities.HasFlag(ModelCapability.Vision).ShouldBeTrue();
    }

    [Test]
    public void InferFrom_MultimodalWithTools_CombinesFlags()
    {
        var request = new AgentRequest
        {
            Messages = [AgentMessage.UserAudio([1, 2], "audio/wav")],
            Tools = [new AgentTool("search", "Search the web", "{}")]
        };

        var requirements = TaskRequirements.InferFrom(request);

        requirements.RequiredCapabilities.HasFlag(ModelCapability.AudioInput).ShouldBeTrue();
        requirements.RequiredCapabilities.HasFlag(ModelCapability.ToolCalling).ShouldBeTrue();
        requirements.RequiredCapabilities.HasFlag(ModelCapability.TextGeneration).ShouldBeTrue();
    }
}
