using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class ModelCatalogTests
{
    // ── TryGet lookup ───────────────────────────────────────────

    [TestCase("gpt-4.1")]
    [TestCase("gpt-4.1-mini")]
    [TestCase("gpt-4.1-nano")]
    [TestCase("gpt-4o")]
    [TestCase("gpt-4o-mini")]
    [TestCase("o3")]
    [TestCase("o3-mini")]
    [TestCase("o4-mini")]
    [TestCase("claude-sonnet-4-20250514")]
    [TestCase("claude-opus-4-20250514")]
    [TestCase("claude-3-5-haiku-20241022")]
    [TestCase("gemini-2.5-pro")]
    [TestCase("gemini-2.5-flash")]
    [TestCase("gemini-2.0-flash")]
    [TestCase("llama-4-scout")]
    [TestCase("llama-3.2-3b")]
    [TestCase("mistral-large-latest")]
    [TestCase("deepseek-chat")]
    [TestCase("deepseek-reasoner")]
    public void TryGet_KnownModel_ReturnsTemplate(string modelName)
    {
        var template = ModelCatalog.TryGet(modelName);
        template.ShouldNotBeNull();
        template.Name.ShouldBe(modelName);
    }

    [Test]
    public void TryGet_CaseInsensitive()
    {
        var template = ModelCatalog.TryGet("GPT-4.1-MINI");
        template.ShouldNotBeNull();
        template.Name.ShouldBe("gpt-4.1-mini");
    }

    [Test]
    public void TryGet_UnknownModel_ReturnsNull()
    {
        ModelCatalog.TryGet("not-a-real-model").ShouldBeNull();
    }

    [Test]
    public void TryGet_NullName_Throws()
    {
        Should.Throw<ArgumentException>(() => ModelCatalog.TryGet(null!));
    }

    // ── ToProfile binding ───────────────────────────────────────

    [Test]
    public void ToProfile_WithRates_BindsModelAndCost()
    {
        var model = new FakeModel();
        var rates = new ModelCostRates(0.0004m, 0.0016m);
        var profile = ModelCatalog.OpenAI.Gpt4_1Mini.ToProfile(model, rates);

        profile.Name.ShouldBe("gpt-4.1-mini");
        profile.Model.ShouldBeSameAs(model);
        (profile.Capabilities & ModelCapability.TextGeneration).ShouldNotBe(ModelCapability.None);
        (profile.Capabilities & ModelCapability.StructuredOutput).ShouldNotBe(ModelCapability.None);
        profile.IntelligenceTier.ShouldBe(3);
        profile.CostPer1KInputTokens.ShouldBe(0.0004m);
        profile.CostPer1KOutputTokens.ShouldBe(0.0016m);
        profile.MaxContextTokens.ShouldBeGreaterThan(0);
    }

    [Test]
    public void ToProfile_NullModel_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            ModelCatalog.OpenAI.Gpt4_1Mini.ToProfile(null!, ModelCostRates.Zero));
    }

    [Test]
    public void ToProfile_NullRates_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            ModelCatalog.OpenAI.Gpt4_1Mini.ToProfile(new FakeModel(), null!));
    }

    [Test]
    public void ToProfile_ZeroCostShorthand_SetsZeroRates()
    {
        var model = new FakeModel();
        var profile = ModelCatalog.Meta.Llama3_2_3B.ToProfile(model);

        profile.CostPer1KInputTokens.ShouldBe(0m);
        profile.CostPer1KOutputTokens.ShouldBe(0m);
        profile.CostPer1KTokens.ShouldBe(0m);
    }

    [Test]
    public void ToProfile_BlendedRate_IsAveraged()
    {
        var model = new FakeModel();
        var profile = ModelCatalog.OpenAI.Gpt4_1.ToProfile(model, new ModelCostRates(0.002m, 0.008m));

        profile.CostPer1KTokens.ShouldBe(0.005m);
    }

    // ── Local models have zero cost ─────────────────────────────

    [Test]
    public void MetaModel_GetCostRates_ReturnsZero()
    {
        var profile = ModelCatalog.Meta.Llama3_2_3B.ToProfile(new FakeModel());
        profile.GetCostRates().ShouldBe(ModelCostRates.Zero);
    }

    // ── Templates have no pricing ───────────────────────────────

    [Test]
    public void Templates_ContainNoPricing()
    {
        foreach (var template in ModelCatalog.All)
        {
            // Templates should have stable metadata only — no hardcoded prices
            var profile = template.ToProfile(new FakeModel());
            profile.CostPer1KTokens.ShouldBe(0m, $"{template.Name} should not have hardcoded pricing");
            profile.CostPer1KInputTokens.ShouldBe(0m, $"{template.Name} should not have hardcoded pricing");
            profile.CostPer1KOutputTokens.ShouldBe(0m, $"{template.Name} should not have hardcoded pricing");
        }
    }

    // ── Catalog integration with router ─────────────────────────

    [Test]
    public void CatalogProfiles_WorkWithCapabilityModelRouter()
    {
        var cheapModel = new FakeModel();
        var expensiveModel = new FakeModel();

        var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
            .AddModel(ModelCatalog.OpenAI.Gpt4_1Mini
                .ToProfile(cheapModel, new ModelCostRates(0.0004m, 0.0016m)))
            .AddModel(ModelCatalog.OpenAI.Gpt4_1
                .ToProfile(expensiveModel, new ModelCostRates(0.002m, 0.008m)));

        var request = new AgentRequest
        {
            Messages = [AgentMessage.User("hello")]
        };

        // CheapestFit should select the mini model
        var selected = router.Select(request);
        selected.ShouldBeSameAs(cheapModel);
    }

    // ── All catalog entries ─────────────────────────────────────
    [Test]
    public void All_NamesAreUnique()
    {
        var names = ModelCatalog.All.Select(t => t.Name).ToList();
        names.Distinct().Count().ShouldBe(names.Count);
    }

    [Test]
    public void All_AllHaveTextGeneration()
    {
        foreach (var template in ModelCatalog.All)
        {
            (template.Capabilities & ModelCapability.TextGeneration).ShouldNotBe(
                ModelCapability.None, $"{template.Name} should have TextGeneration");
        }
    }

    [Test]
    public void All_AllHavePositiveContextWindow()
    {
        foreach (var template in ModelCatalog.All)
        {
            template.MaxContextTokens.ShouldBeGreaterThan(0,
                $"{template.Name} should have a positive context window");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private sealed class FakeModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "ok" });
    }
}
