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
}
