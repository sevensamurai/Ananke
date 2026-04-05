using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class WeightedRoutingTests
{
    // ── RoutingWeights.Score ─────────────────────────────────────

    [Test]
    public void RoutingWeights_DefaultWeights_AllEqualToOne()
    {
        var w = new RoutingWeights();
        w.CostWeight.ShouldBe(1.0m);
        w.SpeedWeight.ShouldBe(1.0m);
        w.IntelligenceWeight.ShouldBe(1.0m);
    }

    // ── Weighted strategy: favour speed ─────────────────────────

    [Test]
    public void Weighted_HighSpeedWeight_SelectsFastestModel()
    {
        var fastModel = new FakeModel();
        var cheapModel = new FakeModel();
        var smartModel = new FakeModel();

        var router = new CapabilityModelRouter(new RoutingWeights
            {
                CostWeight = 0.1m,
                SpeedWeight = 5.0m,
                IntelligenceWeight = 0m
            })
            .AddModel(new ModelProfile
            {
                Name = "fast", Model = fastModel,
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KTokens = 1.0m, SpeedTier = 5, IntelligenceTier = 1
            })
            .AddModel(new ModelProfile
            {
                Name = "cheap", Model = cheapModel,
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KTokens = 0.01m, SpeedTier = 1, IntelligenceTier = 1
            })
            .AddModel(new ModelProfile
            {
                Name = "smart", Model = smartModel,
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KTokens = 5.0m, SpeedTier = 2, IntelligenceTier = 5
            });

        var request = new AgentRequest { Messages = [AgentMessage.User("test")] };
        router.Select(request).ShouldBeSameAs(fastModel);
    }

    // ── Weighted strategy: favour intelligence ──────────────────

    [Test]
    public void Weighted_HighIntelligenceWeight_SelectsSmartestModel()
    {
        var fastModel = new FakeModel();
        var cheapModel = new FakeModel();
        var smartModel = new FakeModel();

        var router = new CapabilityModelRouter(new RoutingWeights
            {
                CostWeight = 0m,
                SpeedWeight = 0m,
                IntelligenceWeight = 10.0m
            })
            .AddModel(new ModelProfile
            {
                Name = "fast", Model = fastModel,
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KTokens = 0.1m, SpeedTier = 5, IntelligenceTier = 2
            })
            .AddModel(new ModelProfile
            {
                Name = "smart", Model = smartModel,
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KTokens = 5.0m, SpeedTier = 1, IntelligenceTier = 5
            });

        var request = new AgentRequest { Messages = [AgentMessage.User("test")] };
        router.Select(request).ShouldBeSameAs(smartModel);
    }

    // ── Weighted strategy: balanced ─────────────────────────────

    [Test]
    public void Weighted_Balanced_SelectsBestOverallModel()
    {
        var balanced = new FakeModel();
        var extreme = new FakeModel();

        // Balanced: cost=0.5, speed=4, intelligence=3
        //   score = -(0.5 * 1) + (4 * 1) + (3 * 1) = -0.5 + 4 + 3 = 6.5
        // Extreme: cost=10.0, speed=5, intelligence=5
        //   score = -(10 * 1) + (5 * 1) + (5 * 1) = -10 + 5 + 5 = 0

        var router = new CapabilityModelRouter(new RoutingWeights())
            .AddModel(new ModelProfile
            {
                Name = "balanced", Model = balanced,
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KTokens = 0.5m, SpeedTier = 4, IntelligenceTier = 3
            })
            .AddModel(new ModelProfile
            {
                Name = "extreme", Model = extreme,
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KTokens = 10.0m, SpeedTier = 5, IntelligenceTier = 5
            });

        var request = new AgentRequest { Messages = [AgentMessage.User("test")] };
        router.Select(request).ShouldBeSameAs(balanced);
    }

    // ── Custom scorer ───────────────────────────────────────────

    [Test]
    public void Custom_ScorerDelegate_SelectsByHighestScore()
    {
        var bigContext = new FakeModel();
        var smallContext = new FakeModel();

        // Custom scorer: max context per cost unit
        var router = new CapabilityModelRouter(
                p => p.MaxContextTokens / Math.Max(p.CostPer1KTokens, 0.001m))
            .AddModel(new ModelProfile
            {
                Name = "big", Model = bigContext,
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KTokens = 0.1m, MaxContextTokens = 1_000_000
            })
            .AddModel(new ModelProfile
            {
                Name = "small", Model = smallContext,
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KTokens = 0.05m, MaxContextTokens = 8_000
            });

        var request = new AgentRequest { Messages = [AgentMessage.User("test")] };
        // big: 1_000_000 / 0.1 = 10_000_000
        // small: 8_000 / 0.05 = 160_000
        router.Select(request).ShouldBeSameAs(bigContext);
    }

    // ── Custom scorer: null throws ──────────────────────────────

    [Test]
    public void Custom_NullScorer_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new CapabilityModelRouter((Func<ModelProfile, decimal>)null!));
    }

    // ── Weighted: null weights throws ───────────────────────────

    [Test]
    public void Weighted_NullWeights_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new CapabilityModelRouter((RoutingWeights)null!));
    }

    // ── Weighted still respects capability filtering ─────────────

    [Test]
    public void Weighted_StillFiltersOnCapabilities()
    {
        var capable = new FakeModel();
        var incapable = new FakeModel();

        var router = new CapabilityModelRouter(new RoutingWeights { SpeedWeight = 100m })
            .AddModel(new ModelProfile
            {
                Name = "incapable", Model = incapable,
                Capabilities = ModelCapability.TextGeneration,
                SpeedTier = 5
            })
            .AddModel(new ModelProfile
            {
                Name = "capable", Model = capable,
                Capabilities = ModelCapability.TextGeneration | ModelCapability.StructuredOutput,
                SpeedTier = 1
            });

        // Request requires structured output — only "capable" qualifies
        var request = new AgentRequest
        {
            Messages = [AgentMessage.User("test")],
            ResponseFormat = new AgentResponseFormat("Test", "{}")
        };

        router.Select(request).ShouldBeSameAs(capable);
    }

    // ── Weighted with cost-aware routing resolves rates ──────────

    [Test]
    public void Weighted_ImplementsIModelCostResolver()
    {
        var model = new FakeModel();
        var router = new CapabilityModelRouter(new RoutingWeights())
            .AddModel(new ModelProfile
            {
                Name = "test", Model = model,
                Capabilities = ModelCapability.TextGeneration,
                CostPer1KInputTokens = 0.01m,
                CostPer1KOutputTokens = 0.03m
            });

        var resolver = router as IModelCostResolver;
        resolver.ShouldNotBeNull();

        var rates = resolver!.ResolveCostRates(
            new AgentRequest { Messages = [AgentMessage.User("test")] });
        rates.CostPer1KInputTokens.ShouldBe(0.01m);
        rates.CostPer1KOutputTokens.ShouldBe(0.03m);
    }

    // ── Speed vs capability real-world scenario ─────────────────

    [Test]
    public void Weighted_RealWorldScenario_LocalModelWinsForSimpleTasks()
    {
        var cloudModel = new FakeModel();
        var localModel = new FakeModel();

        // Weights: strongly favour speed and low cost, intelligence less important
        var router = new CapabilityModelRouter(new RoutingWeights
            {
                CostWeight = 2.0m,
                SpeedWeight = 3.0m,
                IntelligenceWeight = 0.5m
            })
            .AddModel(ModelCatalog.OpenAI.Gpt4_1Mini
                .ToProfile(cloudModel, new ModelCostRates(0.0004m, 0.0016m)))
            .AddModel(ModelCatalog.Meta.Llama3_2_3B
                .ToProfile(localModel));

        // Simple text generation — both qualify, local model should win
        // (zero cost + high speed)
        var request = new AgentRequest { Messages = [AgentMessage.User("hello")] };
        router.Select(request).ShouldBeSameAs(localModel);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private sealed class FakeModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "ok" });
    }
}
