using Ananke.Orchestration.Google;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Q21: <see cref="GeminiAgentModel.Create"/> gained an <c>endpoint</c> override matching
/// OpenAI's and Anthropic's <c>Create(apiKey, model, Uri? endpoint = null)</c> shape, backed by
/// <c>Google.GenAI.Types.HttpOptions.BaseUrl</c> — confirmed present via reflection against the
/// referenced SDK version before filing this as an addressable gap rather than an SDK limitation.
/// </summary>
[TestFixture]
public class GeminiAgentModelTests
{
    [Test]
    public void Create_WithCustomEndpoint_DoesNotThrow()
    {
        var model = GeminiAgentModel.Create("fake-key", "gemini-2.5-flash", new Uri("https://gemini-compatible.example.com/v1"));

        model.ShouldNotBeNull();
    }

    [Test]
    public void Create_WithoutEndpoint_DoesNotThrow()
    {
        var model = GeminiAgentModel.Create("fake-key", "gemini-2.5-flash");

        model.ShouldNotBeNull();
    }
}
