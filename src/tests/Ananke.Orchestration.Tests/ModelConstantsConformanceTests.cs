using System.Reflection;
using Ananke.Abstractions.Agents;
using Ananke.Design;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Drift-killer conformance test between the three consumers of a model identifier:
/// the shared <see cref="Models"/> constants, the design-time <see cref="ModelCatalog"/>
/// (manifest validation), and the runtime <c>Ananke.Orchestration.Agents.Routing.ModelCatalog</c>
/// (capability routing). If a new constant is added to <see cref="Models"/> without registering
/// it in the Design catalog, this test fails immediately instead of silently drifting.
/// </summary>
[TestFixture]
public class ModelConstantsConformanceTests
{
    /// <summary>
    /// Models with no corresponding entry in the Orchestration catalog. Pre-existing gaps,
    /// each for its own reason — not something this phase's catalog-unification work introduced
    /// or is meant to close. Kept as an explicit allowlist so a *new*, unacknowledged gap still
    /// fails the test below.
    /// </summary>
    private static readonly HashSet<string> NoOrchestrationTemplateExpected = new(StringComparer.OrdinalIgnoreCase)
    {
        // Non-chat / specialty Google models with no ModelProfileTemplate — Orchestration's
        // catalog is scoped to text/agent chat models, not image-gen, open-weight, or audio models.
        Models.Google.Gemini31FlashImage,
        Models.Google.Gemma4,
        Models.Google.Lyria3,
    };

    private static IEnumerable<TestCaseData> AllModelConstants()
    {
        foreach (var (providerKey, providerType) in new[]
                 {
                     ("openai", typeof(Models.OpenAI)),
                     ("anthropic", typeof(Models.Anthropic)),
                     ("google", typeof(Models.Google)),
                 })
        {
            foreach (var field in providerType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string))
                    continue;

                var value = (string)field.GetValue(null)!;
                yield return new TestCaseData(providerKey, value)
                    .SetName($"{providerType.Name}.{field.Name} ({value})");
            }
        }
    }

    [TestCaseSource(nameof(AllModelConstants))]
    public void ModelConstant_IsKnownToDesignCatalog(string provider, string modelId)
    {
        var result = ModelCatalog.Validate(provider, modelId);

        result.IsValid.ShouldBeTrue(
            $"'{modelId}' ({provider}) should validate as known (or, if retired, as invalid with a " +
            "replacement) — register it in ModelCatalog.KnownModels / Lifecycle.");

        // Known Current/Legacy models produce no message; known Deprecated models produce a
        // "…deprecated; use X instead" message. Either is fine — what must never happen is the
        // "not in the known catalog" passthrough message, which would mean the constant was
        // never registered in KnownModels at all.
        if (result.Message is not null)
            result.Message!.ShouldContain("deprecated", customMessage:
                $"'{modelId}' ({provider}) fell through as an unregistered passthrough instead of " +
                "resolving as a known model — register it in ModelCatalog.KnownModels.");
    }

    [TestCaseSource(nameof(AllModelConstants))]
    public void ModelConstant_HasOrchestrationTemplate_UnlessAllowlisted(string provider, string modelId)
    {
        var template = Ananke.Orchestration.Agents.Routing.ModelCatalog.TryGet(modelId);

        if (NoOrchestrationTemplateExpected.Contains(modelId))
        {
            // Documented gap — nothing to assert beyond "still doesn't have one, as expected."
            // If this starts failing (template now exists), remove the entry above.
            return;
        }

        template.ShouldNotBeNull(
            $"'{modelId}' ({provider}) has no Orchestration ModelCatalog template. Either add one, " +
            "or add it to NoOrchestrationTemplateExpected with a reason.");
    }

    /// <summary>
    /// Q25: <c>ModelCatalog.Families</c> and <c>ModelCatalog.KnownModels</c> are two hand-maintained
    /// tables with nothing syncing them, and <c>Families</c> had no test at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This asserts they agree <b>at provider level only</b>, which is where the damage is:
    /// <c>Validate()</c> reads <c>Families</c> <i>first</i> and returns "unknown provider — passed
    /// through as-is" when the lookup misses. A provider registered in <c>KnownModels</c> but
    /// missing from <c>Families</c> therefore never reaches the deprecation/retirement branch, so
    /// **every** retired-model check for that provider silently stops firing.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> asserted per-model. <c>Gemini31FlashImage</c>, <c>Gemma4</c> and
    /// <c>Lyria3</c> legitimately sit in <c>KnownModels</c> with no family — they are specialty
    /// models, not versioned families — so a per-model rule would need an allowlist that rots, to
    /// guard a much smaller failure: a weaker suggestion list when someone types a bare family name.
    /// </para>
    /// </remarks>
    [Test]
    public void ModelCatalog_FamiliesAndKnownModels_CoverTheSameProviders()
    {
        var families = PrivateProviderKeys("Families");
        var known = PrivateProviderKeys("KnownModels");

        // Guard against the vacuous pass: if reflection silently returned two empty sets (a renamed
        // field, a changed collection type), the equality below would succeed while checking nothing.
        families.ShouldNotBeEmpty("reflection found no providers in Families — the assertion below would be vacuous");
        known.ShouldNotBeEmpty("reflection found no providers in KnownModels — the assertion below would be vacuous");

        families.ShouldBe(known, ignoreOrder: true,
            "ModelCatalog.Families and ModelCatalog.KnownModels must list the same providers. " +
            "Validate() gates on Families first, so a provider present only in KnownModels silently " +
            "skips every deprecation and retirement check.");
    }

    private static string[] PrivateProviderKeys(string fieldName)
    {
        var field = typeof(ModelCatalog).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        field.ShouldNotBeNull($"ModelCatalog is expected to declare a private static '{fieldName}'");

        var dictionary = (System.Collections.IDictionary)field!.GetValue(null)!;
        return [.. dictionary.Keys.Cast<string>().Order()];
    }
}
