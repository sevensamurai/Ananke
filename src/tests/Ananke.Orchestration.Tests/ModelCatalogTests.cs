using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Microsoft.Extensions.Logging;
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
    [TestCase("claude-opus-4-8")]
    [TestCase("claude-sonnet-4-6")]
    [TestCase("claude-haiku-4-5")]
    [TestCase("claude-sonnet-5")]
    [TestCase("claude-fable-5")]
    [TestCase("gpt-5")]
    [TestCase("gpt-5-mini")]
    [TestCase("gpt-5-nano")]
    [TestCase("gpt-5.2")]
    [TestCase("gpt-5.4")]
    [TestCase("gpt-5.4-mini")]
    [TestCase("gpt-5.4-nano")]
    [TestCase("gpt-5.5")]
    [TestCase("gpt-5.6-sol")]
    [TestCase("gpt-5.6-terra")]
    [TestCase("gpt-5.6-luna")]
    [TestCase("gemini-2.5-pro")]
    [TestCase("gemini-2.5-flash")]
    [TestCase("gemini-3.5-flash")]
    [TestCase("gemini-3.1-flash-lite")]
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

    [Test]
    public void TryGet_ClaudeOpus4_8_CarriesReasoning()
    {
        var template = ModelCatalog.TryGet("claude-opus-4-8");
        template.ShouldNotBeNull();
        (template.Capabilities & ModelCapability.Reasoning).ShouldNotBe(ModelCapability.None);
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

    [Test]
    public void CurrentGenAnthropicProfiles_RouteByCapabilityAndTier()
    {
        var opusModel = new FakeModel();
        var sonnetModel = new FakeModel();
        var haikuModel = new FakeModel();

        var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
            .AddModel(ModelCatalog.Anthropic.ClaudeHaiku4_5
                .ToProfile(haikuModel, new ModelCostRates(0.001m, 0.005m)))
            .AddModel(ModelCatalog.Anthropic.ClaudeSonnet4_6
                .ToProfile(sonnetModel, new ModelCostRates(0.003m, 0.015m)))
            .AddModel(ModelCatalog.Anthropic.ClaudeOpus4_8
                .ToProfile(opusModel, new ModelCostRates(0.005m, 0.025m)));

        var planRequest = new AgentRequest { Messages = [AgentMessage.User("plan")] }
            .WithRequiredCapabilities(ModelCapability.Reasoning)
            .WithMinIntelligence(5);
        router.Select(planRequest).ShouldBeSameAs(opusModel);

        var codeRequest = new AgentRequest { Messages = [AgentMessage.User("code")] }
            .WithRequiredCapabilities(ModelCapability.CodeGeneration)
            .WithMinIntelligence(4);
        router.Select(codeRequest).ShouldBeSameAs(sonnetModel);

        var toolRequest = new AgentRequest { Messages = [AgentMessage.User("call a tool")] }
            .WithRequiredCapabilities(ModelCapability.ToolCalling);
        router.Select(toolRequest).ShouldBeSameAs(haikuModel);
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

    // ── Lifecycle status ─────────────────────────────────────────

    [Test]
    public void All_NonCurrentTemplatesHaveReplacedBy()
    {
        // A Legacy/Deprecated template without a ReplacedBy gives a consumer nowhere to go —
        // catch that at the point a template is added, not when someone reads a blank warning.
        foreach (var template in ModelCatalog.All)
        {
            if (template.Status != ModelStatus.Current)
                template.ReplacedBy.ShouldNotBeNull(
                    $"{template.Name} is {template.Status} but has no ReplacedBy");
        }
    }

    [Test]
    public void ToProfile_CarriesDeprecatedStatusAndReplacedBy()
    {
        var profile = ModelCatalog.Anthropic.ClaudeSonnet4_6.ToProfile(new FakeModel());

        profile.Status.ShouldBe(ModelStatus.Legacy);
        profile.ReplacedBy.ShouldBe(Models.Anthropic.Sonnet5);
    }

    [Test]
    public void ToProfile_CurrentTemplate_HasNoReplacedBy()
    {
        var profile = ModelCatalog.Anthropic.ClaudeFable5.ToProfile(new FakeModel());

        profile.Status.ShouldBe(ModelStatus.Current);
        profile.ReplacedBy.ShouldBeNull();
    }

    [Test]
    public void ModelProfile_DirectlyConstructed_DefaultsToCurrent()
    {
        // A profile built by hand (not from the catalog) shouldn't silently warn as deprecated.
        var profile = new ModelProfile { Name = "custom-model", Model = new FakeModel() };

        profile.Status.ShouldBe(ModelStatus.Current);
        profile.ReplacedBy.ShouldBeNull();
    }

    [Test]
    public void Select_DeprecatedProfile_LogsWarningOnceAcrossMultipleCalls()
    {
        var logger = new CollectingLogger();
        var deprecatedName = $"test-deprecated-{Guid.NewGuid():N}";
        var profile = new ModelProfile
        {
            Name = deprecatedName,
            Model = new FakeModel(),
            Status = ModelStatus.Deprecated,
            ReplacedBy = "test-current-replacement"
        };
        var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit, logger).AddModel(profile);
        var request = new AgentRequest { Messages = [AgentMessage.User("hi")] };

        router.Select(request);
        router.Select(request);
        router.Select(request);

        logger.Messages.Count(m => m.Contains(deprecatedName)).ShouldBe(1);
        logger.Messages.Single(m => m.Contains(deprecatedName)).ShouldContain("test-current-replacement");
    }

    [Test]
    public void Select_CurrentProfile_NeverLogsWarning()
    {
        var logger = new CollectingLogger();
        var currentName = $"test-current-{Guid.NewGuid():N}";
        var profile = new ModelProfile { Name = currentName, Model = new FakeModel() };
        var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit, logger).AddModel(profile);
        var request = new AgentRequest { Messages = [AgentMessage.User("hi")] };

        router.Select(request);

        logger.Messages.ShouldNotContain(m => m.Contains(currentName));
    }

    // ── Helpers ──────────────────────────────────────────────────

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class FakeModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "ok" });
    }
}
