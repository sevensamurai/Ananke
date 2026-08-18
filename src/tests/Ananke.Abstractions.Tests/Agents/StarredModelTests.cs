using System.Reflection;
using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Abstractions.Tests.Agents;

/// <summary>
/// Enforces the invariants behind the per-provider <c>Starred</c> constants: exactly one per
/// provider, aliasing one of that provider's own models, distinct across providers, and
/// <b>Current</b> — never Legacy, Deprecated, or Retired.
/// </summary>
/// <remarks>
/// The compiler already covers half of this: <c>Starred</c> aliases another constant in the same
/// class, so pointing it at a <b>Deprecated</b> or <b>Retired</b> model stops the build. What it
/// cannot cover is <b>Legacy</b> — those constants carry no <see cref="ObsoleteAttribute"/>
/// (<c>Gpt54</c>, <c>Gemini35Flash</c> are declared plainly, "still fully supported"), so a star
/// sliding to Legacy compiles clean and would silently keep <c>nnke</c> scaffolding a superseded
/// model. That is the gap this fixture exists to close.
/// </remarks>
[TestFixture]
public class StarredModelTests
{
    private static readonly (string Provider, Type Type)[] ProviderClasses =
    [
        ("openai", typeof(Models.OpenAI)),
        ("anthropic", typeof(Models.Anthropic)),
        ("google", typeof(Models.Google)),
    ];

    private static IEnumerable<TestCaseData> Providers() =>
        ProviderClasses.Select(p => new TestCaseData(p.Provider, p.Type).SetName(p.Provider));

    private static string StarOf(Type providerType) =>
        (string)providerType
            .GetField(nameof(Models.OpenAI.Starred), BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    [TestCaseSource(nameof(Providers))]
    public void EveryProvider_DeclaresExactlyOneStarredConstant(string provider, Type providerType)
    {
        var starred = providerType
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name == nameof(Models.OpenAI.Starred))
            .ToArray();

        starred.Length.ShouldBe(1,
            $"provider '{provider}' must declare exactly one Starred constant");
    }

    [TestCaseSource(nameof(Providers))]
    public void StarredModel_IsCurrent_NotLegacyDeprecatedOrRetired(string provider, Type providerType)
    {
        var starred = StarOf(providerType);

        // Entries holds only non-Current models, so a hit here is the failure.
        if (ModelLifecycleData.Entries.TryGetValue(starred, out var entry))
        {
            var replacement = entry.ReplacedBy ?? "(no replacement recorded)";
            Assert.Fail(
                $"'{provider}' is starred on '{starred}', which is {entry.Status}. " +
                $"Move Models.{providerType.Name}.Starred to '{replacement}'. " +
                "Legacy does not break the build — this test is the only thing that catches it.");
        }
    }

    [TestCaseSource(nameof(Providers))]
    public void StarredModel_AliasesARealConstantOnTheSameProvider(string provider, Type providerType)
    {
        var starred = StarOf(providerType);

        var siblingValues = providerType
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(Models.OpenAI.Starred))
            .Select(f => (string)f.GetValue(null)!)
            .ToArray();

        siblingValues.ShouldContain(starred,
            $"'{provider}' is starred on '{starred}', which is not one of that provider's own model " +
            "constants — Starred must alias a sibling so the deprecation analyzer reaches it");
    }

    [Test]
    public void StarredModels_AreDistinctAcrossProviders()
    {
        // A copy-paste slip (e.g. Google.Starred = Sonnet5) would otherwise pass every check above.
        var stars = ProviderClasses.Select(p => StarOf(p.Type)).ToArray();

        stars.Distinct().Count().ShouldBe(stars.Length,
            $"each provider must be starred on its own model, got: {string.Join(", ", stars)}");
    }
}
