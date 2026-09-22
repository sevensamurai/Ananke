using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Covers the descriptive facets — size, self-hostability, licence, family — and the policy filter
/// they exist to feed.
/// </summary>
[TestFixture]
public class ModelClassificationTests
{
    [Test]
    public void EveryTemplate_CarriesAClassification()
    {
        // The facets are only worth querying if they are populated everywhere. A template with the
        // default would silently drop out of every filter written against them.
        ModelCatalog.All.ShouldAllBe(t => t.Classification != ModelClassification.Unspecified);
    }

    [Test]
    public void EveryTemplate_DeclaresWhetherItsWeightsAreObtainable()
    {
        ModelCatalog.All.ShouldAllBe(t => t.Classification.Weights != ModelWeights.Unknown);
    }

    [Test]
    public void EveryTemplate_NamesItsFamily()
    {
        ModelCatalog.All.ShouldAllBe(t => !string.IsNullOrWhiteSpace(t.Classification.Family));
    }

    [Test]
    public void HostedModels_ClaimNoLicence()
    {
        // A proprietary model has no weights licence to record. Recording one would be a claim
        // about something the vendor has not published.
        foreach (var template in ModelCatalog.All.Where(t => t.Classification.Weights == ModelWeights.ApiOnly))
        {
            template.Classification.LicenseSpdx.ShouldBeNull($"{template.Name} is hosted-only");
            template.Classification.LicenseIsOsiApproved.ShouldBeFalse($"{template.Name} is hosted-only");
        }
    }

    [Test]
    public void OsiApproved_ImpliesBothOpenWeightsAndARecordedLicence()
    {
        foreach (var template in ModelCatalog.All.Where(t => t.Classification.LicenseIsOsiApproved))
        {
            template.Classification.Weights.ShouldBe(ModelWeights.OpenWeights, template.Name);
            template.Classification.LicenseSpdx.ShouldNotBeNullOrWhiteSpace(template.Name);
        }
    }

    /// <summary>
    /// The finding that motivates the whole facet: "open weights" and "open source" are not the same
    /// thing, and the two most widely deployed open-weight families are not OSI-approved. A team
    /// whose policy turns on that distinction had no way to express it before.
    /// </summary>
    [Test]
    public void OpenWeights_IsNotTheSameClaimAsOsiApproved()
    {
        var openWeights = ModelCatalog.All
            .Where(t => t.Classification.Weights == ModelWeights.OpenWeights)
            .ToList();

        openWeights.ShouldContain(t => t.Classification.LicenseIsOsiApproved);
        openWeights.ShouldContain(t => !t.Classification.LicenseIsOsiApproved);
    }

    [Test]
    public void SmallModels_AreTheMicroAndSmallTiersOnly()
    {
        ModelCatalog.SmallModels.ShouldNotBeEmpty();
        ModelCatalog.SmallModels.ShouldAllBe(t =>
            t.Classification.SizeClass == ModelSizeClass.Micro ||
            t.Classification.SizeClass == ModelSizeClass.Small);
    }

    [Test]
    public void LicensedUnder_MatchesCaseInsensitivelyAndOnlyOnRecordedIdentifiers()
    {
        ModelCatalog.LicensedUnder("APACHE-2.0").ShouldBe(ModelCatalog.LicensedUnder("apache-2.0"));
        ModelCatalog.LicensedUnder("apache-2.0").ShouldAllBe(t => t.Classification.LicenseSpdx == "apache-2.0");
        ModelCatalog.LicensedUnder("not-a-licence").ShouldBeEmpty();
    }

    [Test]
    public void ToProfile_CarriesTheClassificationThrough()
    {
        var template = ModelCatalog.Meta.Llama3_2_1B;
        var profile = template.ToProfile(new FakeModel(), ModelCostRates.Zero);

        profile.Classification.ShouldBe(template.Classification);
        profile.Classification.Family.ShouldBe("llama");
        profile.Classification.SizeClass.ShouldBe(ModelSizeClass.Micro);
    }

    [Test]
    public void HandBuiltProfile_IsUnclassifiedRatherThanAssumedAnything()
    {
        var profile = new ModelProfile { Name = "byo", Model = new FakeModel() };

        profile.Classification.ShouldBe(ModelClassification.Unspecified);
        profile.Classification.Weights.ShouldBe(ModelWeights.Unknown);
    }

    // ── The policy filter ────────────────────────────────────────

    [Test]
    public void WithPolicy_ExcludesModelsTheDeploymentMayNotUse()
    {
        var permitted = Profile("permitted", ModelWeights.OpenWeights, osiApproved: true);
        var forbidden = Profile("forbidden", ModelWeights.ApiOnly, osiApproved: false);

        var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
            .AddModel(forbidden with { CostPer1KTokens = 0m })   // cheaper, and would otherwise win
            .AddModel(permitted with { CostPer1KTokens = 0.01m })
            .WithPolicy(p => p.Classification.LicenseIsOsiApproved);

        router.Select(TextRequest()).ShouldBe(permitted.Model);
    }

    [Test]
    public void WithPolicy_ExcludingEverything_FallsBackRatherThanRoutingAnyway()
    {
        // The fallback is deliberately exempt: it exists to answer what nothing else can, and
        // applying the policy to it would report "no model satisfies" for a policy violation.
        var fallback = Profile("fallback", ModelWeights.ApiOnly, osiApproved: false);

        var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
            .AddModel(Profile("candidate", ModelWeights.ApiOnly, osiApproved: false))
            .WithFallback(fallback)
            .WithPolicy(_ => false);

        router.Select(TextRequest()).ShouldBe(fallback.Model);
    }

    [Test]
    public void WithoutPolicy_NothingIsFiltered()
    {
        var cheap = Profile("cheap", ModelWeights.ApiOnly, osiApproved: false);

        var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
            .AddModel(cheap with { CostPer1KTokens = 0m })
            .AddModel(Profile("dear", ModelWeights.OpenWeights, osiApproved: true) with { CostPer1KTokens = 0.01m });

        router.Select(TextRequest()).ShouldBe(cheap.Model);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static ModelProfile Profile(string name, ModelWeights weights, bool osiApproved) => new()
    {
        Name = name,
        Model = new FakeModel(),
        IntelligenceTier = 2,
        Classification = new ModelClassification
        {
            Weights = weights,
            LicenseIsOsiApproved = osiApproved,
            Family = "test"
        }
    };

    private static AgentRequest TextRequest() => new() { Messages = [AgentMessage.User("hi")] };

    private sealed class FakeModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
