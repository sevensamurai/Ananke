using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Covers the seam that makes truncation incidence and headroom derivable at all. Before it, the
/// usage seam recorded consumption without capacity, so "did the assembled prompt fit the window
/// it was sent to" had no answer.
/// </summary>
[TestFixture]
public class ContextObservationTests
{
    // ── ContextObservation ───────────────────────────────────────

    [Test]
    public void ContextObservation_PromptWithinWindow_DoesNotExceed()
    {
        var observation = new ContextObservation { PromptTokens = 800, ContextTokens = 4096 };

        observation.WindowKnown.ShouldBeTrue();
        observation.ExceededWindow.ShouldBeFalse();
        observation.HeadroomRatio.ShouldNotBeNull();
        observation.HeadroomRatio!.Value.ShouldBe(800d / 4096d, 0.0001d);
    }

    [Test]
    public void ContextObservation_PromptOverWindow_ExceedsWindow()
    {
        var observation = new ContextObservation { PromptTokens = 5000, ContextTokens = 4096 };

        observation.ExceededWindow.ShouldBeTrue();
        observation.HeadroomRatio!.Value.ShouldBeGreaterThan(1d);
    }

    [Test]
    public void ContextObservation_PromptExactlyAtWindow_DoesNotExceed()
    {
        new ContextObservation { PromptTokens = 4096, ContextTokens = 4096 }
            .ExceededWindow.ShouldBeFalse();
    }

    [Test]
    public void ContextObservation_UnknownWindow_ReportsNeitherExceedanceNorHeadroom()
    {
        var observation = new ContextObservation { PromptTokens = 5000, ContextTokens = 0 };

        observation.WindowKnown.ShouldBeFalse();
        // An unknown window must never read as "fits" — that would report a false zero for
        // truncation incidence, which is exactly the number this seam exists to make honest.
        observation.ExceededWindow.ShouldBeFalse();
        observation.HeadroomRatio.ShouldBeNull();
    }

    // ── RequestTokenEstimator ────────────────────────────────────

    [Test]
    public void RequestTokenEstimator_CountsToolSchemas_NotJustMessages()
    {
        var withoutTools = new AgentRequest
        {
            SystemPrompt = "sys",
            Messages = [AgentMessage.User("hello")]
        };
        var withTools = withoutTools with
        {
            Tools = [new AgentTool("search", "Searches the web", "{\"type\":\"object\"}")]
        };

        // Tool schemas ship on every call and are frequently the largest contributor, so an
        // estimate that ignores them understates the prompt precisely when it matters.
        RequestTokenEstimator.Estimate(withTools)
            .ShouldBeGreaterThan(RequestTokenEstimator.Estimate(withoutTools));
    }

    [Test]
    public void ApproximateTokenCounter_MessageWithTextParts_CountsTheTextOnce()
    {
        // AgentMessage.Content is *computed* from TextPart entries when Parts is set, so Parts and
        // Content are two views of one payload. Counting both charged multimodal messages twice.
        var text = new string('a', 400);
        var viaParts = new AgentMessage { Role = AgentRole.User, Parts = [new TextPart(text)] };
        var viaContent = AgentMessage.User(text);

        ApproximateTokenCounter.Instance.EstimateTokens(viaParts)
            .ShouldBe(ApproximateTokenCounter.Instance.EstimateTokens(viaContent));
    }

    [Test]
    public void ApproximateTokenCounter_MessageWithNonTextParts_CountsOnlyTheText()
    {
        var message = new AgentMessage
        {
            Role = AgentRole.User,
            Parts = [new TextPart("hello"), new ImagePart { Data = [1, 2, 3], MimeType = "image/png" }]
        };

        ApproximateTokenCounter.Instance.EstimateTokens(message)
            .ShouldBe(ApproximateTokenCounter.Instance.EstimateTokens("hello"));
    }

    // ── IModelContextResolver ────────────────────────────────────

    [Test]
    public void ResolveContextWindow_ReturnsTheSelectedModelsWindow()
    {
        var router = new CapabilityModelRouter(RoutingStrategy.BestFit)
            .AddModel(ModelProfile.ForTier("big", new FakeModel(), ModelTier.FullModel, 200_000));

        var window = ((IModelContextResolver)router).ResolveContextWindow(Request());

        window.IsKnown.ShouldBeTrue();
        window.ModelName.ShouldBe("big");
        window.ContextTokens.ShouldBe(200_000);
    }

    [Test]
    public void ResolveContextWindow_LocalDeployment_ReportsEffectiveWindowNotDeclared()
    {
        // The weights allow 128k; the server was launched with 8k. A prompt is measured by what
        // the server will actually accept.
        var profile = ModelProfile.ForTier(
            "local", new FakeModel(), ModelTier.FullModel,
            new LocalDeployment("llama.cpp", EffectiveContextTokens: 8_192),
            maxContextTokens: 128_000);

        var router = new CapabilityModelRouter(RoutingStrategy.BestFit).AddModel(profile);

        ((IModelContextResolver)router).ResolveContextWindow(Request())
            .ContextTokens.ShouldBe(8_192);
    }

    // ── End to end through RoutedAgentModel ──────────────────────

    [Test]
    public async Task GenerateAsync_WithObserverScoped_ReportsOneObservation()
    {
        var router = new CapabilityModelRouter(RoutingStrategy.BestFit)
            .AddModel(ModelProfile.ForTier("small", new FakeModel(), ModelTier.FullModel, 4_096));
        var observer = new RecordingObserver();

        using (ContextObserving.BeginScope(observer))
            await router.ToAgentModel().GenerateAsync(Request());

        observer.Observations.Count.ShouldBe(1);
        var observed = observer.Observations[0];
        observed.ModelName.ShouldBe("small");
        observed.ContextTokens.ShouldBe(4_096);
        observed.PromptTokens.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task GenerateAsync_WithoutObserver_ReportsNothing()
    {
        // The opt-in guarantee: a workflow that installs no observer behaves exactly as before.
        var router = new CapabilityModelRouter(RoutingStrategy.BestFit)
            .AddModel(ModelProfile.ForTier("small", new FakeModel(), ModelTier.FullModel, 4_096));
        var observer = new RecordingObserver();

        await router.ToAgentModel().GenerateAsync(Request());

        observer.Observations.ShouldBeEmpty();
        ContextObserving.Current.ShouldBeNull();
    }

    [Test]
    public async Task GenerateAsync_RouterWithoutContextResolver_ObservesWithUnknownWindow()
    {
        // A plain ModelRouter cannot name a window. The observation is still emitted, and it
        // reports the window as unknown rather than inventing one.
        var router = new ModelRouter().Otherwise(new FakeModel());
        var observer = new RecordingObserver();

        using (ContextObserving.BeginScope(observer))
            await router.ToAgentModel().GenerateAsync(Request());

        observer.Observations.Count.ShouldBe(1);
        observer.Observations[0].WindowKnown.ShouldBeFalse();
    }

    [Test]
    public async Task GenerateAsync_OversizedPrompt_IsObservedAsExceedingTheWindow()
    {
        var router = new CapabilityModelRouter(RoutingStrategy.BestFit)
            .AddModel(ModelProfile.ForTier("tiny", new FakeModel(), ModelTier.FullModel, 16));
        var observer = new RecordingObserver();
        var oversized = new AgentRequest
        {
            Messages = [AgentMessage.User(new string('x', 4_000))]
        };

        using (ContextObserving.BeginScope(observer))
            await router.ToAgentModel().GenerateAsync(oversized);

        observer.Observations[0].ExceededWindow.ShouldBeTrue();
    }

    // ── Ambient scope ────────────────────────────────────────────

    [Test]
    public void BeginScope_WhenAlreadyScoped_KeepsTheOuterObserver()
    {
        // Mirrors UsageRecording: the outermost scope owns the flow, so a sub-workflow's calls
        // are observed by the run that started it rather than vanishing into a shadowing scope.
        var outer = new RecordingObserver();
        var inner = new RecordingObserver();

        using (ContextObserving.BeginScope(outer))
        {
            using var innerScope = ContextObserving.BeginScope(inner);
            innerScope.IsOwner.ShouldBeFalse();
            ContextObserving.Current.ShouldBeSameAs(outer);
        }

        ContextObserving.Current.ShouldBeNull();
    }

    private static AgentRequest Request() => new()
    {
        SystemPrompt = "You are a helpful assistant.",
        Messages = [AgentMessage.User("hello")]
    };

    private sealed class RecordingObserver : IContextObserver
    {
        private readonly List<ContextObservation> _observations = [];

        public IReadOnlyList<ContextObservation> Observations => _observations;

        public ValueTask OnContextAssembledAsync(
            ContextObservation observation, CancellationToken ct = default)
        {
            lock (_observations)
                _observations.Add(observation);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "ok" });
    }
}
