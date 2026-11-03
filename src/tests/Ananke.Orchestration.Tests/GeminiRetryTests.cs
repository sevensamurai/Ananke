using Ananke.Orchestration.Google;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>What Gemini's factories hand the SDK about retrying.</summary>
[TestFixture]
public class GeminiRetryTests
{
    [Test]
    public void HttpOptionsFor_TheDeveloperApi_TurnsOnTheSdksRetries()
    {
        // The SDK makes a single attempt when no retry options are given, and the engine leaves a
        // plain rate limit to the SDK.
        GeminiAgentModel.HttpOptionsFor(endpoint: null).RetryOptions.ShouldNotBeNull().Attempts.ShouldBe(3);
    }

    [Test]
    public void HttpOptionsFor_ACustomEndpoint_KeepsItAndRetries()
    {
        var options = GeminiAgentModel.HttpOptionsFor(new Uri("https://gemini.example/"));

        options.BaseUrl.ShouldBe("https://gemini.example/");
        options.RetryOptions.ShouldNotBeNull().Attempts.ShouldBe(3);
    }
}
