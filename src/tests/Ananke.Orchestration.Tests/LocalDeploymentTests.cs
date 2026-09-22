using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Covers the gap between a model's declared context window and the one its server was launched
/// with — the failure that produces a confident answer to a prompt the model never fully saw.
/// </summary>
[TestFixture]
public class LocalDeploymentTests
{
    [Test]
    public void WithoutADeployment_ContextTokensIsTheModelsOwnWindow()
    {
        var profile = new ModelProfile
        {
            Name = "hosted",
            Model = new FakeModel(),
            MaxContextTokens = 128_000
        };

        profile.ContextTokens.ShouldBe(128_000);
        profile.Deployment.ShouldBeNull();
    }

    [Test]
    public void EffectiveContextTokens_OverridesTheModelsDeclaredWindow()
    {
        // The catalogue says 128 K because that is what the weights support. Ollama's default
        // num_ctx is 4 K. The second number is the one that truncates the prompt.
        var profile = new ModelProfile
        {
            Name = "llama-3.2-1b",
            Model = new FakeModel(),
            MaxContextTokens = 128_000,
            Deployment = new LocalDeployment("ollama", EffectiveContextTokens: 4_096)
        };

        profile.ContextTokens.ShouldBe(4_096);
        profile.MaxContextTokens.ShouldBe(128_000, "The weights' own window stays readable");
    }

    [Test]
    public void ADeploymentThatDoesNotKnowItsWindow_FallsBackToTheModels()
    {
        var profile = new ModelProfile
        {
            Name = "llama-3.2-1b",
            Model = new FakeModel(),
            MaxContextTokens = 128_000,
            Deployment = new LocalDeployment("vllm", Quantization: "Q4_K_M")
        };

        profile.ContextTokens.ShouldBe(128_000);
    }

    [Test]
    public void Satisfies_MeasuresAgainstTheServersWindow_NotTheWeights()
    {
        var profile = new ModelProfile
        {
            Name = "llama-3.2-1b",
            Model = new FakeModel(),
            Capabilities = ModelCapability.TextGeneration,
            MaxContextTokens = 128_000,
            Deployment = new LocalDeployment("ollama", EffectiveContextTokens: 4_096)
        };

        var needsMoreThanTheServerHas = new TaskRequirements
        {
            RequiredCapabilities = ModelCapability.TextGeneration,
            MinContextTokens = 32_000
        };

        profile.Satisfies(needsMoreThanTheServerHas).ShouldBeFalse(
            "Routing must not select a model whose server will truncate the prompt");
    }

    [Test]
    public void Routing_SkipsALocalModelThatCannotHoldThePrompt()
    {
        var local = new ModelProfile
        {
            Name = "local-small",
            Model = new FakeModel(),
            Capabilities = ModelCapability.TextGeneration | ModelCapability.LargeContext,
            MaxContextTokens = 128_000,
            Deployment = new LocalDeployment("ollama", EffectiveContextTokens: 4_096)
        };
        var hosted = new ModelProfile
        {
            Name = "hosted-large",
            Model = new FakeModel(),
            Capabilities = ModelCapability.TextGeneration | ModelCapability.LargeContext,
            MaxContextTokens = 200_000,
            CostPer1KTokens = 0.01m
        };

        var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
            .AddModel(local)     // free, and would win on price if it qualified
            .AddModel(hosted);

        var request = new AgentRequest { Messages = [AgentMessage.User("hi")] }
            .WithMinContextTokens(64_000);

        router.Select(request).ShouldBe(hosted.Model);
    }

    [Test]
    public void ForTier_CarriesTheDeploymentAndItsWindow()
    {
        var deployment = new LocalDeployment(
            "llama.cpp",
            Quantization: "Q4_K_M",
            EffectiveContextTokens: 8_192,
            ServedBy: new Uri("http://model-host:8080/v1"));

        var profile = ModelProfile.ForTier(
            "qwen3-4b", new FakeModel(), ModelTier.ChatModel, deployment, maxContextTokens: 32_000);

        profile.Deployment.ShouldBe(deployment);
        profile.ContextTokens.ShouldBe(8_192);
        profile.DeclaredTier.ShouldBe(ModelTier.ChatModel);
        profile.Capabilities.ShouldBe(ModelTiers.ChatModel);
    }

    [Test]
    public void ForTier_StillRefusesToInferCapability()
    {
        // A deployment record says how a model is served. It says nothing about what it can do, and
        // must not be mistaken for a declaration.
        var profile = ModelProfile.ForTier(
            "unknown", new FakeModel(), ModelTier.Undeclared, new LocalDeployment("ollama"));

        profile.Capabilities.ShouldBe(ModelCapability.None);
        profile.Satisfies(new TaskRequirements()).ShouldBeFalse();
    }

    private sealed class FakeModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
