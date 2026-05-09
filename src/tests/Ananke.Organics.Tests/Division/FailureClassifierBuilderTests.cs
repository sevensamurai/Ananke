using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Jobs;
using Ananke.Organics.Division;
using Ananke.Organics.Healing;
using Shouldly;

namespace Ananke.Organics.Tests.Division;

[TestFixture]
public sealed class FailureClassifierBuilderTests
{
    [Test]
    public async Task OpenAI_Profile_ClassifiesRateLimitAsUpstream()
    {
        var classifier = FailureClassifierProfiles.OpenAI().Build();
        var exec = await FaultedExecution("429 Too Many Requests");
        classifier.Classify(exec).ShouldBe(FailureOrigin.Upstream);
    }

    [Test]
    public async Task Anthropic_Profile_ClassifiesOverloadedErrorAsUpstream()
    {
        var classifier = FailureClassifierProfiles.Anthropic().Build();
        var exec = await FaultedExecution("overloaded_error");
        classifier.Classify(exec).ShouldBe(FailureOrigin.Upstream);
    }

    [Test]
    public async Task Google_Profile_ClassifiesResourceExhaustedAsUpstream()
    {
        var classifier = FailureClassifierProfiles.Google().Build();
        var exec = await FaultedExecution("RESOURCE_EXHAUSTED");
        classifier.Classify(exec).ShouldBe(FailureOrigin.Upstream);
    }

    [Test]
    public async Task CustomProfile_FrenchPattern_ClassifiesOnFrenchInput()
    {
        var classifier = new FailureClassifierBuilder()
            .WithLocale("fr")
            .AddPattern(FailureOrigin.Upstream, "Limite de débit dépassée")
            .Build();

        var exec = await FaultedExecution("Limite de débit dépassée");
        classifier.Classify(exec).ShouldBe(FailureOrigin.Upstream);
    }

    [Test]
    public async Task DefaultConstructor_BackwardsCompat_ClassifiesRateLimitAsUpstream()
    {
        var classifier = new FailureClassifier();
        var exec = await FaultedExecution("rate limit exceeded");
        classifier.Classify(exec).ShouldBe(FailureOrigin.Upstream);
    }

    [Test]
    public async Task PatternConstructor_WithUpstreamPattern_ClassifiesCorrectly()
    {
        var patterns = new[] { new FailurePattern(FailureOrigin.Upstream, "custom_error_code") };
        var classifier = new FailureClassifier(patterns);
        var exec = await FaultedExecution("custom_error_code from provider");
        classifier.Classify(exec).ShouldBe(FailureOrigin.Upstream);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static Task<WorkflowExecution<string>> FaultedExecution(string errorMessage)
    {
        var wf = new Workflow<string>("test")
            .Job("fail", (_, _) => throw new HttpRequestException(errorMessage))
            .Then("fail", Workflow.End);

        return wf.RunAsync(string.Empty);
    }
}
