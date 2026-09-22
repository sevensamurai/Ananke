using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// a bring-your-own model may declare a coarse tier, and a model that
/// declares nothing stays unroutable rather than being assumed to work.
/// </summary>
[TestFixture]
public sealed class DeclaredModelTierTests
{
    [Test]
    public void Tiers_are_strictly_nested()
    {
        // The rungs must contain one another, or "declare a higher tier" stops being meaningful.
        ModelTiers.ChatModel.HasFlag(ModelTiers.TextBase).ShouldBeTrue();
        ModelTiers.FullModel.HasFlag(ModelTiers.ChatModel).ShouldBeTrue();
        ModelTiers.FrontierModel.HasFlag(ModelTiers.FullModel).ShouldBeTrue();
    }

    [TestCase(ModelTier.TextBase, ModelCapability.TextGeneration)]
    [TestCase(ModelTier.ChatModel, ModelCapability.ToolCalling)]
    [TestCase(ModelTier.FullModel, ModelCapability.CodeGeneration)]
    [TestCase(ModelTier.FrontierModel, ModelCapability.Reasoning)]
    public void Declared_tier_expands_to_real_capability_flags(ModelTier tier, ModelCapability expected) =>
        tier.ToCapabilities().HasFlag(expected).ShouldBeTrue();

    [Test]
    public void Undeclared_expands_to_nothing() =>
        ModelTier.Undeclared.ToCapabilities().ShouldBe(ModelCapability.None);

    [Test]
    public void A_declared_model_is_routable_for_what_it_declared()
    {
        var profile = ModelProfile.ForTier("byo", new StubModel(), ModelTier.ChatModel);

        profile.Satisfies(Requiring(ModelCapability.ToolCalling)).ShouldBeTrue();
    }

    [Test]
    public void A_declared_model_is_not_routable_beyond_what_it_declared()
    {
        var profile = ModelProfile.ForTier("byo", new StubModel(), ModelTier.ChatModel);

        // ChatModel does not include vision — declaring a tier is not a blanket claim.
        profile.Satisfies(Requiring(ModelCapability.Vision)).ShouldBeFalse();
    }

    [Test]
    public void An_undeclared_model_is_never_routable_even_for_an_empty_requirement()
    {
        // The subtle half of D9: "nobody said what this does" must not read as "this does enough".
        var profile = ModelProfile.Undeclared("byo", new StubModel());

        profile.Satisfies(Requiring(ModelCapability.None)).ShouldBeFalse();
        profile.Satisfies(Requiring(ModelCapability.TextGeneration)).ShouldBeFalse();
        profile.DeclaredTier.ShouldBe(ModelTier.Undeclared);
    }

    [Test]
    public void Catalogue_profiles_are_unaffected_by_the_undeclared_rule()
    {
        // Existing profiles state capabilities directly and must keep routing exactly as before.
        var profile = new ModelProfile { Name = "catalogue", Model = new StubModel() };

        profile.Capabilities.ShouldBe(ModelCapability.TextGeneration);
        profile.Satisfies(Requiring(ModelCapability.TextGeneration)).ShouldBeTrue();
    }

    [Test]
    public void A_declared_tier_leaves_the_other_axes_unset()
    {
        // Intelligence and speed are a different axis; a BYO model cannot honestly declare them.
        var profile = ModelProfile.ForTier("byo", new StubModel(), ModelTier.FrontierModel);

        profile.IntelligenceTier.ShouldBe(1);
        profile.MaxContextTokens.ShouldBe(0);
    }

    [Test]
    public void Router_refuses_an_undeclared_model_rather_than_selecting_it()
    {
        var router = new CapabilityModelRouter()
            .AddModel(ModelProfile.Undeclared("byo", new StubModel()));

        Should.Throw<InvalidOperationException>(() =>
            router.Select(new AgentRequest
            {
                Messages = [new AgentMessage { Role = AgentRole.User, Content = "hi" }]
            }))
            .Message.ShouldContain("No model satisfies");
    }

    private static TaskRequirements Requiring(ModelCapability capabilities) =>
        new() { RequiredCapabilities = capabilities };

    private sealed class StubModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "stub" });
    }
}
